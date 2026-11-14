using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Reflection;
using System.Web.Script.Serialization;

static class UpdateRetryTests {
 sealed class Reporter<T>:IProgress<T> {
  internal readonly List<T> Values=new List<T>();
  internal Action<T> OnReport;
  public void Report(T value){Values.Add(value);if(OnReport!=null)OnReport(value);}
 }
 sealed class Server:IDisposable {
  readonly TcpListener listener=new TcpListener(IPAddress.Loopback,0);
  readonly Action<NetworkStream,int> handler;
  volatile bool stopped;
  internal int Requests;
  internal string Url;
  internal Server(Action<NetworkStream,int> reply){
   handler=reply;
   listener.Start();
   Url="http://127.0.0.1:"+((IPEndPoint)listener.LocalEndpoint).Port+"/test";
   var thread=new Thread(Run){IsBackground=true};thread.Start();
  }
  void Run(){
   while(!stopped){try{
    using(var client=listener.AcceptTcpClient()){
     client.ReceiveTimeout=2000;
     using(var stream=client.GetStream()){
      var reader=new StreamReader(stream,Encoding.ASCII,false,1024,true);
      while(!String.IsNullOrEmpty(reader.ReadLine())){}
      handler(stream,Interlocked.Increment(ref Requests));
     }
    }
   }catch{if(stopped)return;}}
  }
  public void Dispose(){stopped=true;listener.Stop();}
 }
 static readonly byte[] Payload=Encoding.UTF8.GetBytes("fictional-update-package-for-network-regression");
 static string testRoot;
 static int passed;
 static string Digest(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
 static ReleaseInfo Release(){return new ReleaseInfo{Version="9.0.0",Size=Payload.Length,Sha256=Digest(Payload)};}
 static Dictionary<string,object> Manifest(){return new Dictionary<string,object>{{"schema",1},{"repository",UpdateCore.Repository},{"version","9.0.0"},{"file","packages/order-workbench-v9.0.0-portable.zip"},{"releaseUrl","https://github.com/"+UpdateCore.Repository+"/releases/tag/v9.0.0"},{"notes","虚构版本"},{"size",Payload.Length},{"sha256",Digest(Payload)}};}
 static string Json(object value){return new JavaScriptSerializer().Serialize(value);}
 static string GitHubRelease(){return Json(new Dictionary<string,object>{{"tag_name","v9.0.0"},{"html_url","https://github.com/"+UpdateCore.Repository+"/releases/tag/v9.0.0"},{"body","虚构备用源"},{"assets",new object[]{new Dictionary<string,object>{{"name","order-workbench-v9.0.0-portable.zip"},{"state","uploaded"},{"browser_download_url","https://github.com/"+UpdateCore.Repository+"/releases/download/v9.0.0/order-workbench-v9.0.0-portable.zip"},{"size",Payload.Length},{"digest","sha256:"+Digest(Payload)}}}}});}
 static void Check(bool condition,string message){if(!condition)throw new Exception(message);}
 static void Done(string text){passed++;Console.WriteLine("通过："+text);}
 static void Reply(NetworkStream stream,int code,byte[] bytes,int advertised=-1){
  byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 "+code+" Test\r\nContent-Length: "+(advertised<0?bytes.Length:advertised)+"\r\nConnection: close\r\n\r\n");
  stream.Write(header,0,header.Length);stream.Write(bytes,0,bytes.Length);stream.Flush();
 }
 static HttpWebResponse Open(Server server){
  var request=(HttpWebRequest)WebRequest.Create(server.Url);
  request.Proxy=null;request.Timeout=2000;request.ReadWriteTimeout=2000;
  return (HttpWebResponse)request.GetResponse();
 }
 static string FileFor(string name){return Path.Combine(testRoot,name+".zip");}
 static void Download(Server server,string file,CancellationToken cancel,Reporter<string> status=null,int[] delays=null,Reporter<int> progress=null,ReleaseInfo release=null){
  UpdateCore.DownloadPackage(release??Release(),file,progress,cancel,status,()=>Open(server),delays??new int[UpdateCore.DownloadRetryDelays().Length]);
 }
 static T Failure<T>(Action action) where T:Exception {
  try{action();}catch(T error){return error;}
  throw new Exception("Expected "+typeof(T).Name);
 }
 static int Main(){
  testRoot=Path.Combine(Path.GetTempPath(),"OrderWorkbench-RetryTests-"+Guid.NewGuid().ToString("N"));
  Directory.CreateDirectory(testRoot);
  try{
   int[] schedule=UpdateCore.DownloadRetryDelays();
   Check(schedule.Length==15,"重试次数配置错误");
   Check(schedule[0]==5000&&schedule[7]==120000&&schedule[14]==120000,"重试等待上限错误");
   for(int index=1;index<schedule.Length;index++)
    Check(schedule[index]>=schedule[index-1]&&schedule[index]<=120000,"重试等待应逐渐增加且不超过两分钟");
   Done("15次重试间隔递增且最大两分钟");
   using(var server=new Server((stream,n)=>{
    if(n==1)Reply(stream,503,new byte[0]);
    else if(n==2)Reply(stream,200,new byte[]{1,2,3},Payload.Length);
    else Reply(stream,200,Payload);
   })){
    var status=new Reporter<string>();var progress=new Reporter<int>();string file=FileFor("success");
    Download(server,file,CancellationToken.None,status,null,progress);
    Check(server.Requests==3,"未按实际网络故障重试");
    Check(UpdateCore.Hash(file)==Digest(Payload),"重试后文件没有重新完整下载");
    Check(progress.Values.FindAll(value=>value==0).Count>=3,"进度未重置");
    Check(status.Values.Exists(value=>value.Contains("2/15")),"未报告重试次数");
    Done("503与下载截断后自动重试，重新下载、进度和哈希正确");
   }
   using(var server=new Server((stream,n)=>Reply(stream,502,new byte[0]))){
    string file=FileFor("exhausted");
    var error=Failure<IOException>(()=>Download(server,file,CancellationToken.None));
    Check(server.Requests==16&&error.Message.Contains("已自动重试 15 次"),"重试上限不正确");
    Check(!File.Exists(file),"失败残留不完整包");
    Done("最多重试15次，达到上限显示错误并清理未完成文件");
   }
   using(var server=new Server((stream,n)=>Reply(stream,404,new byte[0]))){
    Failure<WebException>(()=>Download(server,FileFor("missing"),CancellationToken.None));
    Check(server.Requests==1,"404不应自动重试");Done("文件不存在时直接报告失败");
   }
   using(var server=new Server((stream,n)=>Reply(stream,429,new byte[0]))){
    using(var cancel=new CancellationTokenSource()){
     var status=new Reporter<string>();status.OnReport=value=>{if(value.Contains("秒后"))cancel.Cancel();};
     string file=FileFor("cancel-wait");
     Failure<OperationCanceledException>(()=>UpdateCore.DownloadPackage(Release(),file,null,cancel.Token,status,()=>Open(server)));
     Check(server.Requests==1&&!File.Exists(file),"等待时取消后继续请求");
     Done("429触发等待，取消立即终止后续重试");
    }
   }
   using(var server=new Server((stream,n)=>Reply(stream,200,Payload))){
    var wrong=Release();wrong.Sha256=new string('0',64);
    string file=FileFor("checksum");
    Failure<InvalidDataException>(()=>Download(server,file,CancellationToken.None,null,null,null,wrong));
    Check(server.Requests==1&&!File.Exists(file),"校验失败不应重复安装或保留损坏包");
    Done("哈希校验失败即停止并删除损坏包");
   }
   using(var server=new Server((stream,n)=>Reply(stream,200,Payload))){
    var release=Release();release.Size--;
    Failure<InvalidDataException>(()=>Download(server,FileFor("oversize"),CancellationToken.None,null,null,null,release));
    Check(server.Requests==1,"大小异常不应重试");Done("大小异常即停止");
   }
   using(var server=new Server((stream,n)=>Reply(stream,200,Payload))){
    Failure<DirectoryNotFoundException>(()=>Download(server,Path.Combine(testRoot,"absent","file.zip"),CancellationToken.None));
    Check(server.Requests==1,"磁盘错误不应网络重试");Done("磁盘写入错误不作网络重试");
   }
   using(var server=new Server((stream,n)=>Reply(stream,200,Payload))){
    using(var cancel=new CancellationTokenSource()){
     cancel.Cancel();Failure<OperationCanceledException>(()=>Download(server,FileFor("already-cancelled"),cancel.Token));
     Check(server.Requests==0,"取消后仍发出请求");Done("取消后不再发起下载");
    }
   }
   using(var gate=new ManualResetEvent(false))
   using(var received=new ManualResetEvent(false))
   using(var server=new Server((stream,n)=>{
    byte[] header=Encoding.ASCII.GetBytes("HTTP/1.1 200 OK\r\nContent-Length: "+Payload.Length+"\r\nConnection: close\r\n\r\n");
    stream.Write(header,0,header.Length);stream.Flush();received.Set();gate.WaitOne(3000);
   })){
    using(var cancel=new CancellationTokenSource()){
     Exception failure=null;string file=FileFor("cancel-read");
     var worker=new Thread(()=>{try{Download(server,file,cancel.Token);}catch(Exception error){failure=error;}});
     worker.Start();Check(received.WaitOne(2000),"未发送响应头");cancel.Cancel();
     bool ended=worker.Join(1500);gate.Set();worker.Join(3000);
     Check(ended&&failure is OperationCanceledException,"读取下载流时无法及时取消");
     Check(server.Requests==1&&!File.Exists(file),"取消后继续下载或残留文件");
     Done("下载流等待中取消能及时停止并清理文件");
    }
   }
   using(var gate=new ManualResetEvent(false))
   using(var received=new ManualResetEvent(false))
   using(var server=new Server((stream,n)=>{received.Set();gate.WaitOne(3000);})){
    using(var cancel=new CancellationTokenSource()){
     var request=(HttpWebRequest)WebRequest.Create(server.Url);request.Proxy=null;
     var method=typeof(UpdateCore).GetMethod("OpenResponse",BindingFlags.Static|BindingFlags.NonPublic);
     Exception failure=null;
     var worker=new Thread(()=>{try{method.Invoke(null,new object[]{request,cancel.Token});}catch(TargetInvocationException error){failure=error.InnerException;}});
     worker.Start();Check(received.WaitOne(2000),"请求未发出");cancel.Cancel();
     Check(worker.Join(1500),"连接等待中取消未生效");gate.Set();
     Check(failure is OperationCanceledException,"连接取消未识别");Done("正在等待HTTP响应时也可立即取消");
    }
   }
   var hosted=UpdateCore.ParseHostedRelease(Json(Manifest()));Check(hosted.DownloadUrl.StartsWith(UpdateCore.HostedRoot)&&hosted.FallbackUrl.Contains("github.com/"+UpdateCore.Repository+"/releases/download/"),"主备用下载源不正确");Check(hosted.Sha256==Digest(Payload)&&hosted.Size==Payload.Length,"发布校验资料不正确");Done("Cloudflare清单只生成固定可信主源与官方备用源");
   foreach(var change in new[]{new KeyValuePair<string,object>("repository","other/repo"),new KeyValuePair<string,object>("file","https://evil.example/update.zip"),new KeyValuePair<string,object>("file","packages/../update.zip"),new KeyValuePair<string,object>("releaseUrl","https://evil.example/"),new KeyValuePair<string,object>("sha256",""),new KeyValuePair<string,object>("size",65L*1024*1024),new KeyValuePair<string,object>("schema",2)}){var m=Manifest();m[change.Key]=change.Value;Failure<InvalidDataException>(()=>UpdateCore.ParseHostedRelease(Json(m)));}Done("无效来源、路径、哈希、大小和清单版本均拒绝");
   UpdateCore.TrustedAddress(new Uri(UpdateCore.HostedRoot+"latest.json"),true);foreach(string bad in new[]{"http://"+UpdateCore.UpdateHost+"/","https://"+UpdateCore.UpdateHost+".evil.example/","https://sub."+UpdateCore.UpdateHost+"/","https://user@"+UpdateCore.UpdateHost+"/","https://"+UpdateCore.UpdateHost+":444/","https://other.workers.dev/"})Failure<InvalidDataException>(()=>UpdateCore.TrustedAddress(new Uri(bad),false));Done("仅信任指定HTTPS域名，不放开其他workers.dev地址或重定向");
   int reads=0;var primary=UpdateCore.Check(url=>{reads++;Check(url==UpdateCore.HostedRoot+"latest.json","检查主源错误");return Json(Manifest());});Check(reads==1&&primary.DownloadUrl.StartsWith(UpdateCore.HostedRoot),"主源正常时不应依赖GitHub");reads=0;var fallback=UpdateCore.Check(url=>{reads++;if(reads==1)throw new WebException("虚构网络中断");return GitHubRelease();});Check(reads==2&&fallback.DownloadUrl.StartsWith("https://github.com/"),"主源失败未检查备用");Done("检查更新优先Cloudflare，主源失败自动切换GitHub");
   using(var main=new Server((stream,n)=>Reply(stream,404,new byte[0])))
   using(var backup=new Server((stream,n)=>Reply(stream,200,Payload))){
    var release=Release();release.DownloadUrl=main.Url;release.FallbackUrl=backup.Url;string file=FileFor("fallback-404");UpdateCore.DownloadFromSources(release,file,null,CancellationToken.None,null,url=>Open(url==main.Url?main:backup),new int[15]);Check(main.Requests==1&&backup.Requests==1&&UpdateCore.Hash(file)==Digest(Payload),"主源404未切换备用源");Done("主源404即时切换备用，无需等待15次");
   }
   using(var main=new Server((stream,n)=>Reply(stream,200,new byte[]{1,2,3},Payload.Length)))
   using(var backup=new Server((stream,n)=>Reply(stream,200,Payload))){
    var release=Release();release.DownloadUrl=main.Url;release.FallbackUrl=backup.Url;var status=new Reporter<string>();UpdateCore.DownloadFromSources(release,FileFor("fallback-truncated"),null,CancellationToken.None,status,url=>Open(url==main.Url?main:backup),new int[15]);Check(main.Requests==1&&backup.Requests==1&&status.Values.Exists(value=>value.Contains("1/15")),"下载中断后未切换备用");Done("下载中断重试时切换备用源并保持重试次数显示");
   }
   using(var main=new Server((stream,n)=>Reply(stream,503,new byte[0])))
   using(var backup=new Server((stream,n)=>Reply(stream,502,new byte[0]))){
    var release=Release();release.DownloadUrl=main.Url;release.FallbackUrl=backup.Url;var error=Failure<IOException>(()=>UpdateCore.DownloadFromSources(release,FileFor("both-exhausted"),null,CancellationToken.None,null,url=>Open(url==main.Url?main:backup),new int[15]));Check(main.Requests==16&&backup.Requests==16&&error.Message.Contains("15 次"),"双源重试预算不正确");Done("双源故障仍严格限制15次重试");
   }
   using(var main=new Server((stream,n)=>Reply(stream,200,new byte[Payload.Length])))
   using(var backup=new Server((stream,n)=>Reply(stream,200,Payload))){
    var release=Release();release.DownloadUrl=main.Url;release.FallbackUrl=backup.Url;Failure<InvalidDataException>(()=>UpdateCore.DownloadFromSources(release,FileFor("fallback-checksum"),null,CancellationToken.None,null,url=>Open(url==main.Url?main:backup),new int[15]));Check(main.Requests==1&&backup.Requests==0&&!File.Exists(FileFor("fallback-checksum")),"损坏包不应绕过校验或使用备用掩盖");Done("主源哈希异常立即停止并清理，不降低校验要求");
   }
   Console.WriteLine("更新下载回归 "+passed+" 项通过");return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 1;}
 }
}
