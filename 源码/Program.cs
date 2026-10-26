using System;
using System.IO;
using System.Text;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Collections;
using System.Collections.Generic;
using System.Web.Script.Serialization;
using System.Windows.Forms;
using System.Drawing;
using System.Diagnostics;
using System.Reflection;

internal static class Program {
    static string dir, file, token, url; static int port; static TcpListener listener; static FileStream dataLock;
    static WorkbenchWindow mainWindow; static NotifyIcon tray; static readonly object gate = new object(); static bool stopping;
    static JavaScriptSerializer Json() { return new JavaScriptSerializer { MaxJsonLength=16*1024*1024, RecursionLimit=100 }; }
    static readonly UTF8Encoding Utf8 = new UTF8Encoding(false,true);
    [STAThread] static void Main(string[] args) {
        Application.EnableVisualStyles();
        try {
            dir=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"data"); bool browse=true; bool browserMode=false;
            for(int i=0;i<args.Length;i++) { if(args[i]=="--data-dir" && i+1<args.Length)dir=Path.GetFullPath(args[++i]);if(args[i]=="--no-browser")browse=false;if(args[i]=="--browser")browserMode=true; }
            Directory.CreateDirectory(dir); file=Path.Combine(dir,"orders.json");
            try {dataLock=new FileStream(Path.Combine(dir,"running.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None);}
            catch(IOException) { string instance=Path.Combine(dir,"instance.json"); if(File.Exists(instance)){var old=Dict(Json().DeserializeObject(File.ReadAllText(instance,Utf8))); RevealInstance(S(old,"url"));} else MessageBox.Show("当前数据文件夹已被另一个工作台占用。","订单工作台"); return; }
            if(!File.Exists(file)) AtomicSave(new Dictionary<string,object>{{"schema",1},{"revision",0},{"orders",new object[0]}});
            ReadData(); // 损坏时停止，不以空白数据覆盖。
            token=Guid.NewGuid().ToString("N"); listener=new TcpListener(IPAddress.Loopback,0);listener.Start();port=((IPEndPoint)listener.LocalEndpoint).Port;url="http://127.0.0.1:"+port+"/"+token+"/";
            File.WriteAllText(Path.Combine(dir,"instance.json"),Json().Serialize(new {url=url,pid=Process.GetCurrentProcess().Id}),Utf8);
            ThreadPool.QueueUserWorkItem(delegate {AcceptLoop();});
            tray=new NotifyIcon(); tray.Text="订单工作台 · 正在本机运行"; using(Stream s=Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico")){tray.Icon=s==null?SystemIcons.Application:new Icon(s);} tray.Visible=true;
            ContextMenuStrip menu=new ContextMenuStrip();menu.Items.Add("打开订单工作台",null,delegate{if(mainWindow!=null)mainWindow.Reveal();else Open(url);});menu.Items.Add("打开数据文件夹",null,delegate{Process.Start("explorer.exe",dir);});menu.Items.Add("退出工作台",null,delegate{RequestShutdown();});tray.ContextMenuStrip=menu;tray.DoubleClick+=delegate{if(mainWindow!=null)mainWindow.Reveal();else Open(url);};
            if(browse&&!browserMode){mainWindow=new WorkbenchWindow(url,dir);Application.Run(mainWindow);}else{if(browse)Open(url);Application.Run();}
        } catch(Exception ex) { MessageBox.Show("工作台未能启动：\n"+ex.Message+"\n\n如果订单文件损坏，请保留 data 文件夹，并从 data\\backups 或导出的备份中恢复。","订单工作台",MessageBoxButtons.OK,MessageBoxIcon.Error); }
        finally {if(mainWindow!=null&&!mainWindow.UpdateInstalling)NativeIcons.Schedule(Application.ExecutablePath,dir);stopping=true;if(tray!=null){tray.Visible=false;tray.Dispose();}if(listener!=null)listener.Stop();if(dataLock!=null)dataLock.Dispose();}
    }
    internal static void SetTrayIcon(Icon icon){if(tray==null)return;var old=tray.Icon;tray.Icon=(Icon)icon.Clone();if(old!=null)old.Dispose();}
    internal static void ApplySavedIcon(Dictionary<string,object> settings){if(mainWindow==null||mainWindow.IsDisposed)return;mainWindow.BeginInvoke(new Action(delegate{try{NativeIcons.SaveIcon(dir,settings);mainWindow.ApplyProgramIcon(settings);NativeIcons.RefreshShortcuts(Application.ExecutablePath,dir);}catch{}}));}
    static void RevealInstance(string address){try {var uri=new Uri(address);string secret=uri.AbsolutePath.Trim('/');var request=(HttpWebRequest)WebRequest.Create(address+"api/show");request.Method="POST";request.Headers["X-Workbench-Token"]=secret;request.ContentLength=0;request.Timeout=3000;using(var response=request.GetResponse()){} }catch{MessageBox.Show("工作台已运行，请切回现有窗口。","订单工作台");}}
    static void RequestShutdown(){if(mainWindow!=null&&!mainWindow.IsDisposed){mainWindow.BeginInvoke(new Action(mainWindow.RequestClose));return;}Shutdown();}
    static void Open(string address) {
        if(String.IsNullOrEmpty(address)||!address.StartsWith("http://127.0.0.1:"))return;
        string[] candidates={Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),@"Microsoft\Edge\Application\msedge.exe"),Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),@"Microsoft\Edge\Application\msedge.exe")};
        foreach(string p in candidates)if(File.Exists(p)){Process.Start(new ProcessStartInfo(p,"--app=\""+address+"\""){UseShellExecute=true});return;}
        Process.Start(new ProcessStartInfo(address){UseShellExecute=true});
    }
    static void Shutdown(){if(stopping)return;stopping=true; if(listener!=null)listener.Stop();Application.Exit();}
    static void AcceptLoop(){while(!stopping){try{var client=listener.AcceptTcpClient();ThreadPool.QueueUserWorkItem(delegate{Handle(client);});}catch{if(stopping)return;}}}
    static void Handle(TcpClient client){using(client){client.ReceiveTimeout=15000;client.SendTimeout=15000;try{
        NetworkStream stream=client.GetStream();var bytes=new List<byte>(); int b;
        while((b=stream.ReadByte())>=0){bytes.Add((byte)b);int n=bytes.Count;if(n>16384){Reply(stream,431,"text/plain","请求头过大");return;}if(n>=4&&bytes[n-4]==13&&bytes[n-3]==10&&bytes[n-2]==13&&bytes[n-1]==10)break;}
        string[] lines=Encoding.ASCII.GetString(bytes.ToArray()).Split(new[]{"\r\n"},StringSplitOptions.None);if(lines.Length==0)return;string[] first=lines[0].Split(' ');if(first.Length!=3){Reply(stream,400,"text/plain","无效请求");return;}
        string method=first[0], path=first[1].Split('?')[0];var headers=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase);
        for(int i=1;i<lines.Length;i++){int c=lines[i].IndexOf(':');if(c>0)headers[lines[i].Substring(0,c).Trim()]=lines[i].Substring(c+1).Trim();}
        if(!headers.ContainsKey("Host")||headers["Host"]!="127.0.0.1:"+port||!path.StartsWith("/"+token+"/",StringComparison.Ordinal)){Reply(stream,404,"text/plain","未找到");return;}
        if(headers.ContainsKey("Origin")&&headers["Origin"]!="http://127.0.0.1:"+port){Reply(stream,403,"text/plain","仅允许本机工作台请求");return;}
        string route=path.Substring(token.Length+2);long length=0;if(headers.ContainsKey("Content-Length")&&!Int64.TryParse(headers["Content-Length"],out length)){Reply(stream,400,"text/plain","无效长度");return;}
        if(length<0||length>12*1024*1024||headers.ContainsKey("Transfer-Encoding")){Reply(stream,413,"text/plain","数据过大，最多支持 12 MB");return;}
        byte[] body=new byte[(int)length];int offset=0;while(offset<body.Length){int read=stream.Read(body,offset,body.Length-offset);if(read<=0)throw new IOException("请求不完整");offset+=read;}
        if(method=="POST"&&route=="api/show"&&headers.ContainsKey("X-Workbench-Token")&&headers["X-Workbench-Token"]==token){if(mainWindow!=null)mainWindow.BeginInvoke(new Action(mainWindow.Reveal));else Open(url);ReplyJson(stream,200,new{ok=true});return;}
        string cookie=headers.ContainsKey("Cookie")?headers["Cookie"]:"";var account=Accounts.Current(cookie);
        if(route.StartsWith("api/auth/",StringComparison.Ordinal)){
            try{
                if(method=="GET"&&route=="api/auth/me"){ReplyJson(stream,200,new{user=account,hasUsers=Accounts.HasUsers(dir)});return;}
                if(method!="POST"||!headers.ContainsKey("X-Workbench-Token")||headers["X-Workbench-Token"]!=token){ReplyJson(stream,403,new{error="无效会话，请重新打开工作台"});return;}
                if(body.Length>16384){ReplyJson(stream,400,new{error="账户信息过长"});return;}var input=Dict(Json().DeserializeObject(Utf8.GetString(body)));
                if(route=="api/auth/create"){ReplyJson(stream,200,new{user=Accounts.Create(dir,input,account)});return;}
                if(route=="api/auth/login"){string session;var user=Accounts.Login(dir,input,out session);Accounts.Logout(cookie);ReplyAuth(stream,new{user=user},"ow_session="+session+"; HttpOnly; SameSite=Strict; Path=/"+token+"/");return;}
                if(route=="api/auth/logout"){Accounts.Logout(cookie);ReplyAuth(stream,new{ok=true},"ow_session=; Max-Age=0; HttpOnly; SameSite=Strict; Path=/"+token+"/");return;}
                ReplyJson(stream,404,new{error="账户操作不存在"});
            }catch(Exception ex){ReplyJson(stream,400,new{error=ex.Message});}return;
        }
        if(route.StartsWith("api/",StringComparison.Ordinal)&&route!="api/quit"&&account==null){ReplyJson(stream,401,new{error="请先登录账户。",code="AUTH_REQUIRED"});return;}
        if(method=="GET"&&route=="api/data"){lock(gate)ReplyJson(stream,200,ReadData());return;}
        if(method=="POST"&&(route=="api/files/status"||route=="api/files/action")){
            if(!headers.ContainsKey("X-Workbench-Token")||headers["X-Workbench-Token"]!=token){ReplyJson(stream,403,new{error="无效会话，请重新打开工作台"});return;}
            try{var input=Dict(Json().DeserializeObject(Utf8.GetString(body)));if(route=="api/files/status"){object raw;Require(input.TryGetValue("paths",out raw)&&raw is object[]&&((object[])raw).Length<=200,"路径列表格式错误或超过 200 个");var results=new List<object>();foreach(object item in (object[])raw){Require(item is string,"路径必须是文字");results.Add(EvidenceFiles.Status((string)item));}ReplyJson(stream,200,new{files=results});}else ReplyJson(stream,200,EvidenceFiles.Action(S(input,"path"),S(input,"action")));}
            catch(Exception ex){ReplyJson(stream,400,new{error="文件操作未完成："+ex.Message});}return;
        }
        if(method=="POST"&&route=="api/production-preview"){
            if(!headers.ContainsKey("X-Workbench-Token")||headers["X-Workbench-Token"]!=token){ReplyJson(stream,403,new{error="无效会话，请重新打开工作台"});return;}
            try {var input=Dict(Json().DeserializeObject(Utf8.GetString(body)));ReplyJson(stream,200,ProductionPreview.Read(S(input,"content"),S(input,"filename")));}
            catch(Exception ex){ReplyJson(stream,400,new{error="无法读取生产单："+ex.Message});}return;
        }
        if(route=="api/settings"&&(method=="GET"||method=="POST")){
            if(method=="POST"&&(!headers.ContainsKey("X-Workbench-Token")||headers["X-Workbench-Token"]!=token)){ReplyJson(stream,403,new{error="无效会话，请重新打开工作台"});return;}
            try{lock(SettingsStore.Gate){if(method=="GET")ReplyJson(stream,200,SettingsStore.Read(dir));else{var saved=SettingsStore.Save(dir,Dict(Json().DeserializeObject(Utf8.GetString(body))));ApplySavedIcon(saved);ReplyJson(stream,200,saved);}}}
            catch(SettingsConflictException ex){ReplyJson(stream,409,new{error=ex.Message});}catch(Exception ex){ReplyJson(stream,400,new{error="设置未保存："+ex.Message});}return;
        }
        if(method=="POST"&&route=="api/data"){
            if(!headers.ContainsKey("X-Workbench-Token")||headers["X-Workbench-Token"]!=token){ReplyJson(stream,403,new{error="无效会话，请重新打开工作台"});return;}
            try{var incoming=Dict(Json().DeserializeObject(Utf8.GetString(body)));lock(gate){var current=ReadData();if(Convert.ToInt64(incoming["revision"])!=Convert.ToInt64(current["revision"])){ReplyJson(stream,409,new{error="另一个窗口已修改订单。当前编辑已保留，请导出草稿后重新载入。"});return;}ValidateDataShape(incoming);AuditTrail.Prepare(current,incoming,account);ValidateData(incoming);incoming["revision"]=Convert.ToInt64(current["revision"])+1;incoming["savedAt"]=DateTime.UtcNow.ToString("o");AtomicSave(incoming);ReplyJson(stream,200,incoming);} }
            catch(InvalidDataException ex){ReplyJson(stream,400,new{error=ex.Message});}catch(ArgumentException){ReplyJson(stream,400,new{error="数据格式不正确"});}return;
        }
        if(method=="POST"&&route=="api/quit"&&headers.ContainsKey("X-Workbench-Token")&&headers["X-Workbench-Token"]==token){ReplyJson(stream,200,new{ok=true});ThreadPool.QueueUserWorkItem(delegate{Thread.Sleep(400);RequestShutdown();});return;}
        if(method!="GET"){Reply(stream,405,"text/plain","请求方式不支持");return;}
        string resource=route==""?"index.html":route;string mime=resource.EndsWith(".js")?"application/javascript":resource.EndsWith(".css")?"text/css":resource=="app.ico"?"image/x-icon":"text/html";
        if(resource!="code-match.js"&&resource!="batch-codes-ui.js"&&resource!="auth-ui.js"&&resource!="audit-ui.js"&&resource!="theme.css"&&resource!="settings-ui.js"&&resource!="index.html"&&resource!="model.js"&&resource!="production.js"&&resource!="production-ui.js"&&resource!="app.js"&&resource!="evidence.js"&&resource!="evidence-ui.js"&&resource!="batch.js"&&resource!="batch-ui.js"&&resource!="save-state.js"&&resource!="style.css"&&resource!="app.ico"){Reply(stream,404,"text/plain","未找到");return;}
        using(Stream embedded=Assembly.GetExecutingAssembly().GetManifestResourceStream(resource)){if(embedded==null){Reply(stream,404,"text/plain","资源缺失");return;}using(var ms=new MemoryStream()){embedded.CopyTo(ms);byte[] content=ms.ToArray();if(resource=="index.html"){try{var settings=SettingsStore.Read(dir);string theme=(string)settings["theme"];if(theme=="dark")content=Utf8.GetBytes(Utf8.GetString(content).Replace("<html lang=\"zh-CN\">","<html lang=\"zh-CN\" data-theme=\"dark\">").Replace("content=\"light\"","content=\"dark\""));}catch{}}ReplyBytes(stream,200,mime,content);}}
    }catch(Exception ex){try{ReplyJson(client.GetStream(),500,new{error="操作失败，数据未确认保存："+ex.Message});}catch{}}}}
    static Dictionary<string,object> Dict(object o){var d=o as Dictionary<string,object>;if(d==null)throw new InvalidDataException("数据格式错误：应为对象");return d;}
    static string S(Dictionary<string,object> d,string key){object v;return d.TryGetValue(key,out v)&&v is string?(string)v:"";}
    static bool B(Dictionary<string,object>d,string key){object v;return d.TryGetValue(key,out v)&&v is bool&&(bool)v;}
    static void Require(bool ok,string message){if(!ok)throw new InvalidDataException(message);}
    static bool In(string value,params string[] values){return Array.IndexOf(values,value)>=0;}
    static string[] materials={"silk","nameplate","manual","carton"};
    static Dictionary<string,object> ReadData(){var data=Dict(Json().DeserializeObject(File.ReadAllText(file,Utf8)));ValidateData(data);return data;}
    static void ValidateDataShape(Dictionary<string,object> data){object value;if(!data.TryGetValue("orders",out value)||!(value is object[])||((object[])value).Length>10000)throw new InvalidDataException("订单列表无效");foreach(object item in (object[])value){var o=Dict(item);if(S(o,"id")==""||!o.ContainsKey("materials")||!(o["materials"] is Dictionary<string,object>))throw new InvalidDataException("订单基本结构不完整");}}
    static void ValidateData(Dictionary<string,object> data){
        Require(data.ContainsKey("schema")&&Convert.ToInt32(data["schema"])==1,"不支持此备份版本");Require(data.ContainsKey("revision")&&Convert.ToInt64(data["revision"])>=0,"缺少有效版本号");
        object val;Require(data.TryGetValue("orders",out val)&&val is object[],"缺少订单列表");object[] orders=(object[])val;Require(orders.Length<=10000,"最多支持 10000 个订单");var ids=new HashSet<string>();var numbers=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(object obj in orders){var o=Dict(obj);string id=S(o,"id"),num=S(o,"number").Trim();Require(id.Length>0&&ids.Add(id),"订单标识缺失或重复");Require(num.Length>0&&numbers.Add(num),"订单号缺失或重复："+num);Require(S(o,"customer").Trim().Length>0&&S(o,"model").Trim().Length>0,"客户和型号不能为空");double quantity=0;Require(o.ContainsKey("quantity")&&Double.TryParse(Convert.ToString(o["quantity"]),out quantity)&&quantity>0&&quantity<=100000000&&Math.Floor(quantity)==quantity,"数量必须是有效正整数");
            Require(In(S(o,"priority"),"normal","urgent"),"无效优先级");string due=S(o,"due");DateTime dueDate;Require(due==""||DateTime.TryParseExact(due,"yyyy-MM-dd",System.Globalization.CultureInfo.InvariantCulture,System.Globalization.DateTimeStyles.None,out dueDate),"日期格式无效");
            Require(!B(o,"machineConfirmed")||B(o,"appearance"),"确认机器前需完成外形确认");Require(!B(o,"procurementSent")||(B(o,"appearance")&&B(o,"machineConfirmed")&&(S(o,"machineCode").Trim().Length>0||AuditTrail.Accepted(o,"purchase"))),"采购交接缺少机器确认或机器料号");
            Require(o.ContainsKey("materials"),"缺少包材信息");var mats=Dict(o["materials"]);bool all=true;foreach(string key in materials){Require(mats.ContainsKey(key),"缺少包材项目："+key);var m=Dict(mats[key]);string status=S(m,"status");Require(In(status,"pending","requested","received","confirmed","na"),"无效包材状态");Require(In(S(m,"source"),"existing","new"),"无效料号来源");if(status!="na"&&(status!="confirmed"||(S(m,"code").Trim().Length==0&&!AuditTrail.Accepted(o,"plan"))))all=false;}
            string plate=S(o,"plate");Require(In(plate,"unknown","existing","requested","ready","na"),"无效网版状态");string silk=S(Dict(mats["silk"]),"status");Require(silk=="na"||!In(plate,"existing","requested","ready")||silk=="confirmed","确认丝印要求后再提交或确认网版");bool ready=silk=="na"||In(plate,"existing","ready","na");Require(!B(o,"planSent")||(B(o,"procurementSent")&&all&&ready),"计划交接前须完成所有要求确认、料号与网版检查");
            Require(In(S(o,"machineSource"),"existing","new"),"无效机器料号来源");foreach(var kv in o)if(kv.Value is string)Require(((string)kv.Value).Length<=20000,"文本超长");
            foreach(string key in new[]{"number","customer","model","sales","due","priority","notes","machineReference","machineSource","machineCode","procurementDate","procurementNote","plate","plateNote","planDate","planNote","createdAt","updatedAt"})Require(o.ContainsKey(key)&&o[key] is string,"订单缺少文本字段："+key);
            foreach(string key in new[]{"appearance","machineConfirmed","procurementSent","planSent","archived"})Require(o.ContainsKey(key)&&o[key] is bool,"订单状态格式错误："+key);
            foreach(string key in materials){var m=Dict(mats[key]);foreach(string f in new[]{"source","code","reference","note"})Require(m.ContainsKey(f)&&m[f] is string&&((string)m[f]).Length<=20000,"包材字段错误："+key+"."+f);}
            foreach(string key in new[]{"alias","contractNumber","customerModel","customerCode","sourceSerial","productRequirements","packagingRequirements","category","unit","sourceOperator","orderDate","orderProperty","transport"})if(o.ContainsKey(key))Require(o[key] is string,"生产单字段格式错误："+key);
            if(o.ContainsKey("productionSource")&&o["productionSource"]!=null){var source=Dict(o["productionSource"]);Require(source.ContainsKey("warnings")&&source["warnings"] is object[],"生产单来源提示格式错误");foreach(object warning in (object[])source["warnings"])Require(warning is string,"生产单来源提示必须是文字");foreach(string key in new[]{"filename","sha256","sheet","headerQuantity"})Require(source.ContainsKey(key)&&source[key] is string,"生产单来源字段错误："+key);Require(source.ContainsKey("originalItem")&&source["originalItem"] is Dictionary<string,object>,"生产单原始行格式错误");}
            EvidenceFiles.ValidateLinks(o);
            Require(o.ContainsKey("history")&&o["history"] is object[],"订单记录格式错误");foreach(object item in (object[])o["history"]){var h=Dict(item);Require(h.ContainsKey("text")&&h["text"] is string&&h.ContainsKey("at")&&h["at"] is string,"跟进记录格式错误");}
        }
    }
    static void AtomicSave(Dictionary<string,object> data){
        string json=Json().Serialize(data);string temp=file+".tmp";byte[] raw=Utf8.GetBytes(json);
        using(var output=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){output.Write(raw,0,raw.Length);output.Flush(true);}
        if(File.Exists(file)){string backups=Path.Combine(dir,"backups");Directory.CreateDirectory(backups);string backup=Path.Combine(backups,"orders_"+DateTime.Now.ToString("yyyyMMdd_HHmmss_fff")+"_"+Guid.NewGuid().ToString("N").Substring(0,6)+".json");File.Replace(temp,file,backup,true);
            try{var files=new DirectoryInfo(backups).GetFiles("orders_*.json");Array.Sort(files,delegate(FileInfo a,FileInfo b){return b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc);});for(int i=50;i<files.Length;i++)files[i].Delete();}catch{}
        }else File.Move(temp,file);
    }
    static void ReplyAuth(NetworkStream s,object obj,string cookie){ReplyBytes(s,200,"application/json",Encoding.UTF8.GetBytes(Json().Serialize(obj)),"Set-Cookie: "+cookie+"\r\n");}
    static void ReplyJson(NetworkStream s,int code,object obj){Reply(s,code,"application/json",Json().Serialize(obj));}
    static void Reply(NetworkStream s,int code,string mime,string text){ReplyBytes(s,code,mime,Encoding.UTF8.GetBytes(text));}
    static void ReplyBytes(NetworkStream s,int code,string mime,byte[] body,string extra=""){string status=code==200?"OK":code==409?"Conflict":code==404?"Not Found":"Error";string header="HTTP/1.1 "+code+" "+status+"\r\n"+extra+"Content-Type: "+mime+"; charset=utf-8\r\nContent-Length: "+body.Length+"\r\nCache-Control: no-store\r\nX-Content-Type-Options: nosniff\r\nReferrer-Policy: no-referrer\r\nContent-Security-Policy: default-src 'self'; script-src 'self'; style-src 'self' 'unsafe-inline'; img-src 'self' data:; connect-src 'self'; frame-ancestors 'none'; base-uri 'self'; form-action 'self'\r\nConnection: close\r\n\r\n";byte[] raw=Encoding.ASCII.GetBytes(header);s.Write(raw,0,raw.Length);s.Write(body,0,body.Length);s.Flush();}
}
