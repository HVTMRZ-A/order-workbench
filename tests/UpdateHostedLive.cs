using System;
using System.IO;
using System.Net;
using System.Reflection;
using System.Threading;
static class UpdateHostedLive {
 static HttpWebResponse Open(string url){try{return (HttpWebResponse)typeof(UpdateCore).GetMethod("Response",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,new object[]{url,false,CancellationToken.None});}catch(TargetInvocationException error){throw error.InnerException;}}
 static int Main(string[] args){try{
  string root=args[0];Directory.CreateDirectory(root);var release=UpdateCore.Check();UpdateCore.Require(release.DownloadUrl.StartsWith(UpdateCore.HostedRoot),"未能从Cloudflare读取清单");UpdateCore.Require(release.Version=="1.20.0","当前测试清单已改变");string zip=Path.Combine(root,"dotnet-downloaded.zip");UpdateCore.DownloadFromSources(release,zip,null,CancellationToken.None,null,Open,new int[0]);UpdateCore.Require(UpdateCore.Hash(zip)==release.Sha256,"下载哈希不一致");
  string target=Path.Combine(root,"fictional-target"),data=Path.Combine(root,"fictional-data"),stage=Path.Combine(root,"stage");Directory.CreateDirectory(target);Directory.CreateDirectory(data);Directory.CreateDirectory(stage);File.Copy(Path.Combine(args[1],"订单工作台.exe"),Path.Combine(target,"订单工作台.exe"));File.WriteAllText(Path.Combine(data,"orders.json"),"{\"fictional\":true}");string before=UpdateCore.Hash(Path.Combine(data,"orders.json"));string plan=UpdateCore.StagePackage(zip,release.Sha256,release.Version,target,data,stage);UpdateCore.Require(File.Exists(plan)&&UpdateCore.Hash(Path.Combine(data,"orders.json"))==before,"暂存校验影响虚构数据");
  Console.WriteLine("通过：.NET更新器从Cloudflare读取v"+release.Version+"，下载并通过SHA256、清单和内部程序版本校验，未修改数据。");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
