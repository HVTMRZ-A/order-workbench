using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Diagnostics;
using System.Threading;
using System.Web.Script.Serialization;
using System.Reflection;

[assembly:AssemblyVersion("1.10.0.0")]
[assembly:AssemblyFileVersion("1.10.0.0")]

internal sealed class ReleaseInfo {
 public string Version,Notes,Url,DownloadUrl,Sha256; public long Size;
}
internal sealed class UpdateFile { public string Path,Sha256; public long Size; }
internal sealed class UpdatePlan {
 public string Version,Target,Data,Stage,OldExeHash;public int ParentId;public long ParentStarted;
 public List<UpdateFile> Files=new List<UpdateFile>();
}
internal static class UpdateCore {
 internal const string VersionText="1.10.0",Repository="HVTMRZ-A/order-workbench";
 internal static readonly Version CurrentVersion=new Version(VersionText);
 internal static readonly string UpdateRoot=System.IO.Path.Combine(System.IO.Path.GetTempPath(),"OrderWorkbench-Updates");
 static readonly string[] RuntimeFiles={"订单工作台.exe","订单更新助手.exe","Microsoft.Web.WebView2.Core.dll","Microsoft.Web.WebView2.WinForms.dll","runtimes/win-x64/native/WebView2Loader.dll","runtimes/win-x86/native/WebView2Loader.dll","先看这里.txt","验证说明.txt","WebView2组件许可.txt"};
 static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=2*1024*1024};}
 internal static void Require(bool ok,string text){if(!ok)throw new InvalidDataException(text);}
 internal static Dictionary<string,object> Dict(object x){var d=x as Dictionary<string,object>;Require(d!=null,"更新资料格式无效");return d;}
 internal static string S(Dictionary<string,object>d,string k){object v;return d.TryGetValue(k,out v)&&v is string?(string)v:"";}
 static bool B(Dictionary<string,object>d,string k){object v;return d.TryGetValue(k,out v)&&v is bool&&(bool)v;}
 internal static Version ParseVersion(string value){Require(value!=null&&Regex.IsMatch(value,@"^v?\d{1,5}\.\d{1,5}\.\d{1,5}$"),"更新版本号无效");return new Version(value.TrimStart('v'));}
 internal static string Hash(string file){using(var s=File.OpenRead(file))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(s)).Replace("-","").ToLowerInvariant();}
 internal static ReleaseInfo ParseRelease(string text){
  var d=Dict(Json().DeserializeObject(text));Require(!B(d,"draft")&&!B(d,"prerelease"),"没有可用的正式更新");string tag=S(d,"tag_name"),v=ParseVersion(tag).ToString();
  string prefix="https://github.com/"+Repository+"/releases/";Require(S(d,"html_url")==prefix+"tag/"+tag,"更新来源不匹配");
  object value;Require(d.TryGetValue("assets",out value)&&value is object[],"更新包列表缺失");string name="order-workbench-v"+v+"-portable.zip";
  Dictionary<string,object> asset=null;foreach(var item in (object[])value){var a=Dict(item);if(S(a,"name")==name){Require(asset==null,"更新包重复");asset=a;}}
  // 较旧版本没有自更新清单，但仍可准确显示“当前已是最新”。
  if(ParseVersion(v)<=CurrentVersion)return new ReleaseInfo{Version=v,Notes=S(d,"body"),Url=S(d,"html_url")};
  Require(asset!=null&&S(asset,"state")=="uploaded","新版本程序包尚未上传完成");string digest=S(asset,"digest");Require(Regex.IsMatch(digest,@"^sha256:[0-9a-fA-F]{64}$"),"更新包缺少 SHA256 校验，已停止更新");
  string url=prefix+"download/"+tag+"/"+name;Require(S(asset,"browser_download_url")==url,"更新包下载地址无效");long size=Convert.ToInt64(asset["size"]);Require(size>0&&size<=64*1024*1024,"更新包大小无效或超过 64 MB");
  return new ReleaseInfo{Version=v,Notes=S(d,"body"),Url=S(d,"html_url"),DownloadUrl=url,Sha256=digest.Substring(7).ToLowerInvariant(),Size=size};
 }
 internal static ReleaseInfo Check(){return ParseRelease(ReadText("https://api.github.com/repos/"+Repository+"/releases/latest"));}
 static HttpWebResponse Response(string address,bool json){
  Uri uri=new Uri(address);for(int redirects=0;redirects<6;redirects++){
   Require(uri.Scheme=="https"&&uri.IsDefaultPort&&String.IsNullOrEmpty(uri.UserInfo),"只允许从 GitHub 的 HTTPS 地址获取更新");
   bool trusted=json?uri.Host=="api.github.com":uri.Host=="github.com"||uri.Host=="release-assets.githubusercontent.com"||uri.Host=="objects.githubusercontent.com"||uri.Host=="github-releases.githubusercontent.com";
   Require(trusted,"更新下载被重定向到不支持的地址");ServicePointManager.SecurityProtocol|=SecurityProtocolType.Tls12;
   var req=(HttpWebRequest)WebRequest.Create(uri);req.Method="GET";req.UserAgent="OrderWorkbench/"+VersionText;req.Accept=json?"application/vnd.github+json":"application/octet-stream";req.Timeout=20000;req.ReadWriteTimeout=20000;req.AllowAutoRedirect=false;
   var response=(HttpWebResponse)req.GetResponse();int status=(int)response.StatusCode;
   if(status>=300&&status<400){string location=response.Headers["Location"];response.Dispose();Require(!String.IsNullOrEmpty(location),"下载跳转地址缺失");uri=new Uri(uri,location);continue;}Require(status==200,"GitHub 未返回更新文件");return response;
  }throw new InvalidDataException("更新下载跳转次数过多");
 }
 static string ReadText(string url){using(var response=Response(url,true))using(var s=response.GetResponseStream())using(var ms=new MemoryStream()){var buffer=new byte[8192];int n;while((n=s.Read(buffer,0,buffer.Length))>0){Require(ms.Length+n<=2*1024*1024,"更新资料过大");ms.Write(buffer,0,n);}return Encoding.UTF8.GetString(ms.ToArray());}}
 internal static string Friendly(Exception ex){var web=ex as WebException;if(web!=null){var response=web.Response as HttpWebResponse;if(response!=null&&(int)response.StatusCode==404)return "暂未找到公开的正式更新，请稍后重试。";if(response!=null&&((int)response.StatusCode==403||(int)response.StatusCode==429))return "GitHub 暂时限制了检查次数，请稍后再试。";return "无法连接 GitHub，请检查网络后重试。当前程序与订单数据保持不变。";}return ex.Message;}
 internal static string Prepare(ReleaseInfo release,string target,string data,IProgress<int> progress,CancellationToken cancel){
  Require(ParseVersion(release.Version)>CurrentVersion,"无须安装相同或更早的版本");Directory.CreateDirectory(UpdateRoot);string stage=System.IO.Path.Combine(UpdateRoot,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);string zip=System.IO.Path.Combine(stage,"update.zip");
  using(var response=Response(release.DownloadUrl,false))using(var input=response.GetResponseStream())using(var output=new FileStream(zip,FileMode.CreateNew,FileAccess.Write,FileShare.None)){
   byte[] buffer=new byte[65536];long length=0;int n;while((n=input.Read(buffer,0,buffer.Length))>0){cancel.ThrowIfCancellationRequested();length+=n;Require(length<=release.Size&&length<=64*1024*1024,"下载文件大小与发布信息不符");output.Write(buffer,0,n);if(progress!=null)progress.Report((int)(length*100/release.Size));}Require(length==release.Size,"更新下载不完整，请重试");output.Flush(true);
  }cancel.ThrowIfCancellationRequested();return StagePackage(zip,release.Sha256,release.Version,target,data,stage);
 }
 internal static string SafeRelative(string path){Require(!String.IsNullOrWhiteSpace(path)&&path.Length<240&&!path.Contains("\\")&&!path.Contains(":")&&!path.StartsWith("/"),"更新包路径无效");foreach(string part in path.Split('/'))Require(part!=""&&part!="."&&part!=".."&&!part.EndsWith(".")&&!part.EndsWith(" "),"更新包包含不安全路径");return path;}
 internal static string Inside(string root,string path){string full=System.IO.Path.GetFullPath(System.IO.Path.Combine(root,SafeRelative(path).Replace('/',System.IO.Path.DirectorySeparatorChar)));Require(full.StartsWith(System.IO.Path.GetFullPath(root).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase),"更新路径超出目标目录");return full;}
 internal static void NoReparse(string root,string file){string current=System.IO.Path.GetFullPath(file),end=System.IO.Path.GetFullPath(root).TrimEnd('\\');while(current.Length>=end.Length){if(File.Exists(current)||Directory.Exists(current))Require((File.GetAttributes(current)&FileAttributes.ReparsePoint)==0,"更新目录含有链接，已停止更新");if(current.Equals(end,StringComparison.OrdinalIgnoreCase))return;current=System.IO.Path.GetDirectoryName(current);if(current==null)break;}throw new InvalidDataException("更新路径不在程序目录内");}
 static bool Allowed(string path){return Array.Exists(RuntimeFiles,x=>String.Equals(x,path,StringComparison.OrdinalIgnoreCase));}
 internal static string StagePackage(string zip,string expectedHash,string version,string target,string data,string stage){
  Require(Regex.IsMatch(expectedHash??"",@"^[0-9a-fA-F]{64}$")&&Hash(zip)==expectedHash.ToLowerInvariant(),"更新包校验失败，未更改原程序");ParseVersion(version);
  target=System.IO.Path.GetFullPath(target).TrimEnd('\\');Require(Directory.Exists(target)&&File.Exists(System.IO.Path.Combine(target,"订单工作台.exe")),"没有找到待更新的工作台程序");Require(target.Length>System.IO.Path.GetPathRoot(target).Length,"不能在磁盘根目录执行更新");NoReparse(target,target);
  string payload=System.IO.Path.Combine(stage,"payload");Directory.CreateDirectory(payload);var plan=new UpdatePlan{Version=version,Target=target,Data=System.IO.Path.GetFullPath(data),Stage=System.IO.Path.GetFullPath(stage),ParentId=Process.GetCurrentProcess().Id,ParentStarted=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks,OldExeHash=Hash(System.IO.Path.Combine(target,"订单工作台.exe"))};
  using(var archive=ZipFile.OpenRead(zip)){
   var entries=new Dictionary<string,ZipArchiveEntry>(StringComparer.OrdinalIgnoreCase);long total=0;Require(archive.Entries.Count<=200,"更新包文件数量异常");foreach(var entry in archive.Entries){string p=entry.FullName;if(p.EndsWith("/")){SafeRelative(p.TrimEnd('/'));continue;}SafeRelative(p);Require(!entries.ContainsKey(p),"更新包包含重复文件");Require((entry.ExternalAttributes>>16&0xf000)!=0xa000,"更新包不支持符号链接");total+=entry.Length;Require(entry.Length<=64*1024*1024&&total<=128*1024*1024,"更新包解压大小异常");entries.Add(p,entry);}
   ZipArchiveEntry manifest;Require(entries.TryGetValue("update-manifest.json",out manifest)&&manifest.Length<=20000,"更新包不支持自动更新，请手动安装完整版本");Dictionary<string,object> m;using(var reader=new StreamReader(manifest.Open(),Encoding.UTF8))m=Dict(Json().DeserializeObject(reader.ReadToEnd()));Require(S(m,"version")==version&&Convert.ToInt32(m["schema"])==1,"更新包版本与发布信息不符");object raw;Require(m.TryGetValue("files",out raw)&&raw is object[],"更新文件清单缺失");var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
   foreach(var item in (object[])raw){var f=Dict(item);string path=SafeRelative(S(f,"path")),sha=S(f,"sha256");Require(Allowed(path)&&seen.Add(path),"更新清单包含不支持或重复的文件");Require(Regex.IsMatch(sha,@"^[0-9a-f]{64}$"),"更新文件校验值无效");ZipArchiveEntry entry;Require(entries.TryGetValue(path,out entry),"更新包缺少文件："+path);long size=Convert.ToInt64(f["size"]);Require(size==entry.Length,"更新文件大小不符："+path);string file=Inside(payload,path);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(file));using(var input=entry.Open())using(var output=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.None))input.CopyTo(output);Require(Hash(file)==sha,"更新文件校验失败："+path);plan.Files.Add(new UpdateFile{Path=path,Sha256=sha,Size=size});}
   foreach(string required in RuntimeFiles)Require(seen.Contains(required),"更新包缺少必要组件："+required);
  }
  string v=FileVersionInfo.GetVersionInfo(Inside(payload,"订单工作台.exe")).FileVersion;Require(new Version(v)==new Version(version+".0"),"新程序内部版本与更新信息不一致");
  string planFile=System.IO.Path.Combine(stage,"plan.json");File.WriteAllText(planFile,Json().Serialize(plan),new UTF8Encoding(false));return planFile;
 }
 internal static UpdatePlan ReadPlan(string file){var p=Json().Deserialize<UpdatePlan>(File.ReadAllText(file,Encoding.UTF8));Require(p!=null&&p.Files!=null&&p.Files.Count==RuntimeFiles.Length,"更新计划无效");Require(ParseVersion(p.Version)>CurrentVersion,"不能安装相同或更早的版本");Require(System.IO.Path.GetFullPath(System.IO.Path.GetDirectoryName(file))==System.IO.Path.GetFullPath(p.Stage),"更新计划目录不匹配");Require(Directory.Exists(p.Target)&&System.IO.Path.GetFullPath(p.Target).TrimEnd('\\').Length>System.IO.Path.GetPathRoot(p.Target).Length,"目标目录无效");Require(System.IO.Path.GetFullPath(p.Stage)!=System.IO.Path.GetFullPath(p.Target),"暂存目录不能是程序目录");NoReparse(p.Target,p.Target);var seen=new HashSet<string>(StringComparer.OrdinalIgnoreCase);foreach(var f in p.Files){Require(Allowed(f.Path)&&seen.Add(f.Path),"更新计划包含不支持的文件");string payload=Inside(System.IO.Path.Combine(p.Stage,"payload"),f.Path);NoReparse(p.Stage,payload);Require(new FileInfo(payload).Length==f.Size&&Hash(payload)==f.Sha256,"暂存更新文件已改变");NoReparse(p.Target,Inside(p.Target,f.Path));}Require(Hash(System.IO.Path.Combine(p.Target,"订单工作台.exe"))==p.OldExeHash,"原程序已经改变，请重新检查更新");return p;}
 internal static void ReplaceFrom(string from,string to){Directory.CreateDirectory(System.IO.Path.GetDirectoryName(to));string temp=to+".update-"+Guid.NewGuid().ToString("N")+".tmp";try{File.Copy(from,temp,false);if(File.Exists(to))File.Replace(temp,to,null,true);else File.Move(temp,to);}finally{if(File.Exists(temp))File.Delete(temp);}}
 internal static void ApplyFiles(UpdatePlan plan,Action<int> afterReplace){
  string backup=System.IO.Path.Combine(plan.Stage,"rollback");Directory.CreateDirectory(backup);var existed=new HashSet<string>(StringComparer.OrdinalIgnoreCase);var replaced=new List<UpdateFile>();
  foreach(var f in plan.Files){string target=Inside(plan.Target,f.Path);NoReparse(plan.Target,target);if(File.Exists(target)){using(var stream=new FileStream(target,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){}string old=Inside(backup,f.Path);Directory.CreateDirectory(System.IO.Path.GetDirectoryName(old));File.Copy(target,old,false);existed.Add(f.Path);}}
  try{int i=0;foreach(var f in plan.Files){string to=Inside(plan.Target,f.Path);NoReparse(plan.Target,to);ReplaceFrom(Inside(System.IO.Path.Combine(plan.Stage,"payload"),f.Path),to);replaced.Add(f);Require(Hash(to)==f.Sha256,"安装后的文件校验失败");if(afterReplace!=null)afterReplace(++i);}}
  catch{for(int i=replaced.Count-1;i>=0;i--){var f=replaced[i];string to=Inside(plan.Target,f.Path);if(existed.Contains(f.Path))ReplaceFrom(Inside(backup,f.Path),to);else if(File.Exists(to))File.Delete(to);}throw;}
 }
 internal static string Quote(string text){Require(text!=null&&!text.Contains("\""),"启动参数无效");int trailing=0;for(int i=text.Length-1;i>=0&&text[i]=='\\';i--)trailing++;return "\""+text+new string('\\',trailing)+"\"";}
 internal static void StartHelper(string planFile){var plan=ReadPlan(planFile);string original=System.IO.Path.Combine(plan.Target,"订单更新助手.exe");Require(File.Exists(original),"缺少更新助手，请安装完整程序包");string helper=System.IO.Path.Combine(plan.Stage,"apply.exe");File.Copy(original,helper,false);Process.Start(new ProcessStartInfo(helper,"--apply "+Quote(planFile)){UseShellExecute=false,WorkingDirectory=plan.Stage,CreateNoWindow=true});}
 internal static void WaitForParent(UpdatePlan plan){try{var parent=Process.GetProcessById(plan.ParentId);if(parent.StartTime.ToUniversalTime().Ticks==plan.ParentStarted)Require(parent.WaitForExit(90000),"工作台仍在运行，更新已取消");}catch(ArgumentException){}string path=System.IO.Path.Combine(plan.Data,"running.lock");if(File.Exists(path)){using(var s=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.None)){} }}
}
