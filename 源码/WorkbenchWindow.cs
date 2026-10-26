using System;
using System.IO;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Runtime.InteropServices;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

internal sealed class WorkbenchWindow : Form {
    readonly WebView2 browser=new WebView2(); readonly string address,storage;
    readonly UpdateMonitor updateMonitor=new UpdateMonitor();readonly Timer autoUpdateTimer=new Timer{Interval=3000};bool autoUpdatesStarted;
    bool allowClose,closeInProgress,pageReady,updateOpen; string latestState="saved";
    readonly Dictionary<string,TaskCompletionSource<Dictionary<string,object>>> requests=new Dictionary<string,TaskCompletionSource<Dictionary<string,object>>>();
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool SetDllDirectory(string path);
    [DllImport("dwmapi.dll")] static extern int DwmSetWindowAttribute(IntPtr window,int attribute,ref int value,int size);
    static JavaScriptSerializer Json(){return new JavaScriptSerializer();}
    static bool Flag(Dictionary<string,object>d,string key){object value;return d.TryGetValue(key,out value)&&value is bool&&(bool)value;}
    static string TextValue(Dictionary<string,object>d,string key){object value;return d.TryGetValue(key,out value)&&value is string?(string)value:"";}
    internal WorkbenchWindow(string url,string dataDir){
        address=url;storage=dataDir;Text="订单工作台 v1.14";Font=new Font("Microsoft YaHei UI",9);StartPosition=FormStartPosition.CenterScreen;Size=new Size(1400,940);MinimumSize=new Size(1050,730);AutoScaleMode=AutoScaleMode.Dpi;
        using(var s=System.Reflection.Assembly.GetExecutingAssembly().GetManifestResourceStream("app.ico")){if(s!=null)Icon=new Icon(s);}
        browser.AllowExternalDrop=true;browser.Dock=DockStyle.Fill;Controls.Add(browser);Shown+=Initialize;FormClosing+=HandleFormClosing;
        updateMonitor.Changed+=PublishUpdateState;
        autoUpdateTimer.Tick+=async delegate{if(IsDisposed||!pageReady||updateOpen||closeInProgress||!updateMonitor.ShouldCheckAutomatically())return;try{await updateMonitor.CheckAsync(true);}catch{}};
        FormClosed+=delegate{autoUpdateTimer.Stop();autoUpdateTimer.Dispose();updateMonitor.Dispose();};
    }
    async void Initialize(object sender,EventArgs args){
        try {
            string arch=IntPtr.Size==8?"win-x64":"win-x86";SetDllDirectory(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"runtimes",arch,"native"));
            var env=await CoreWebView2Environment.CreateAsync(null,Path.Combine(storage,"window-cache"));await browser.EnsureCoreWebView2Async(env);
            browser.CoreWebView2.Settings.IsStatusBarEnabled=false;
            browser.CoreWebView2.NavigationStarting+=delegate(object s,CoreWebView2NavigationStartingEventArgs e){if(!e.Uri.StartsWith(address,StringComparison.Ordinal)&&e.Uri!="about:blank")e.Cancel=true;};
            browser.CoreWebView2.WebMessageReceived+=Message;
            browser.Source=new Uri(address);
        }catch(Exception ex){MessageBox.Show(this,"无法打开独立工作台窗口：\n"+ex.Message+"\n\n请确认程序旁的组件文件完整，并且这台电脑具备 Microsoft Edge WebView2 Runtime。原订单数据保持在 data 文件夹中。","工作台启动提示",MessageBoxButtons.OK,MessageBoxIcon.Error);allowClose=true;Close();}
    }
    void Message(object sender,CoreWebView2WebMessageReceivedEventArgs e){
        if(!e.Source.StartsWith(address,StringComparison.Ordinal))return;
        try {var data=Json().Deserialize<Dictionary<string,object>>(e.WebMessageAsJson);string type=TextValue(data,"type");
            if(type=="evidence-paths"){
                string requestId=TextValue(data,"requestId");try{var paths=new List<string>();if(e.AdditionalObjects==null||e.AdditionalObjects.Count==0||e.AdditionalObjects.Count>50)throw new InvalidDataException("请一次关联 1 至 50 个文件");foreach(object item in e.AdditionalObjects){var localFile=item as CoreWebView2File;if(localFile==null)throw new InvalidDataException("未能识别原文件路径，请重新选择文件");paths.Add(EvidenceFiles.NormalizePath(localFile.Path));}browser.CoreWebView2.PostWebMessageAsJson(Json().Serialize(new{type="evidence-paths-result",requestId=requestId,paths=paths}));}
                catch(Exception ex){browser.CoreWebView2.PostWebMessageAsJson(Json().Serialize(new{type="evidence-paths-result",requestId=requestId,error="未能取得原文件路径："+ex.Message}));}return;
            }
            if(type=="update-status-request"){PublishUpdateState();return;}
            if(type=="appearance"){string theme=TextValue(data,"theme");if(theme=="dark"||theme=="light"){int dark=theme=="dark"?1:0;BackColor=dark==1?Color.FromArgb(16,24,32):Color.White;try{if(DwmSetWindowAttribute(Handle,20,ref dark,4)!=0)DwmSetWindowAttribute(Handle,19,ref dark,4);}catch{}}return;}
            if(type=="open-project"){try{System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("https://github.com/"+UpdateCore.Repository){UseShellExecute=true});}catch(Exception ex){MessageBox.Show(this,"无法打开浏览器。项目地址：\nhttps://github.com/"+UpdateCore.Repository+"\n\n"+ex.Message,"项目地址",MessageBoxButtons.OK,MessageBoxIcon.Information);}return;}
            if(type=="software-update"){BeginInvoke(new Action(ShowUpdates));return;}
            if(type=="save-state"){pageReady=true;latestState=TextValue(data,"state");string label=latestState=="dirty"?"未保存":latestState=="saving"?"正在保存":latestState=="error"?"保存失败":latestState=="demo"?"示例模式":"已保存";Text="订单工作台 v1.14 · "+label;if(!autoUpdatesStarted&&latestState!="loading"){autoUpdatesStarted=true;updateMonitor.StartAutomatic();autoUpdateTimer.Start();PublishUpdateState();}}
            if(type=="request-close")BeginInvoke(new Action(RequestClose));
            if(type=="close-save-result"){string id=TextValue(data,"requestId");TaskCompletionSource<Dictionary<string,object>> request;if(requests.TryGetValue(id,out request))request.TrySetResult(data);}
        }catch{}
    }
    void PublishUpdateState(){
        if(IsDisposed||!IsHandleCreated)return;if(InvokeRequired){try{BeginInvoke(new Action(PublishUpdateState));}catch{}return;}
        if(!pageReady||browser.CoreWebView2==null)return;var update=updateMonitor.Status();
        try{browser.CoreWebView2.PostWebMessageAsJson(Json().Serialize(new{type="update-status",state=update.State,available=update.Available,version=update.Version,error=update.Error,checkedAt=update.CheckedAt==DateTime.MinValue?"":update.CheckedAt.ToString("o")}));}catch{}
    }
    void ShowUpdates(){if(updateOpen||IsDisposed)return;if(!pageReady){MessageBox.Show(this,"请等订单数据读取完成后再检查更新。","软件更新");return;}updateOpen=true;try{using(var dialog=new UpdateDialog(AppDomain.CurrentDomain.BaseDirectory,storage,BeginUpdate,updateMonitor))dialog.ShowDialog(this);}finally{updateOpen=false;}}
    async Task<bool> BeginUpdate(string plan){
        var state=await CloseState();if(!Flag(state,"ready")||Flag(state,"saving")||Flag(state,"busy")){MessageBox.Show(this,"请先等读取、保存或文件关联完成后，再安装更新。","暂不能更新");return false;}
        if(Flag(state,"dirty")){if(MessageBox.Show(this,"当前有未保存更改。保存成功后将关闭并更新；若暂未完成核对，请取消。\n\n是否保存并更新？","保存并更新",MessageBoxButtons.YesNo,MessageBoxIcon.Question)!=DialogResult.Yes)return false;var saved=await SaveForClose();if(!Flag(saved,"ok")){MessageBox.Show(this,"未安装更新，编辑仍保留。\n\n"+TextValue(saved,"error"),"保存未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);return false;}}
        state=await CloseState();if(Flag(state,"dirty")||Flag(state,"saving")||Flag(state,"busy")){MessageBox.Show(this,"仍有未保存内容，更新已取消。","请先保存");return false;}
        UpdateCore.StartHelper(plan);await browser.ExecuteScriptAsync("window.workbench && window.workbench.allowClose();");allowClose=true;BeginInvoke(new Action(Close));return true;
    }
    internal void Reveal(){if(IsDisposed)return;if(WindowState==FormWindowState.Minimized)WindowState=FormWindowState.Normal;Show();Activate();}
    internal void RequestClose(){if(!IsDisposed)Close();}
    async Task<Dictionary<string,object>> CloseState(){string json=await browser.ExecuteScriptAsync("window.workbench ? window.workbench.getCloseState() : null");if(json=="null")throw new InvalidOperationException("页面尚未就绪，不能确认保存状态。");return Json().Deserialize<Dictionary<string,object>>(json);}
    async Task<Dictionary<string,object>> SaveForClose(){
        string id=Guid.NewGuid().ToString("N");var done=new TaskCompletionSource<Dictionary<string,object>>(TaskCreationOptions.RunContinuationsAsynchronously);requests[id]=done;
        try {await browser.ExecuteScriptAsync("window.workbench.saveForClose("+Json().Serialize(id)+");");while(!done.Task.IsCompleted){var result=await Task.WhenAny(done.Task,Task.Delay(30000));if(result==done.Task)break;var waiting=await CloseState();if(!Flag(waiting,"busy"))throw new TimeoutException("保存未在30秒内确认完成，请保留窗口并检查保存状态。");}return await done.Task;}finally{requests.Remove(id);}
    }
    async void HandleFormClosing(object sender,FormClosingEventArgs e){
        if(allowClose)return;e.Cancel=true;if(closeInProgress)return;closeInProgress=true;
        try {
            if(!pageReady||browser.CoreWebView2==null){allowClose=true;BeginInvoke(new Action(Close));return;}
            var state=await CloseState();
            if(Flag(state,"busy")){MessageBox.Show(this,"正在处理文件、登录或确认风险，请完成或取消当前操作后再关闭。","请完成当前操作",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
            if(Flag(state,"saving")){MessageBox.Show(this,"正在保存，请等保存完成后再关闭。","正在保存",MessageBoxButtons.OK,MessageBoxIcon.Information);return;}
            if(Flag(state,"dirty")){
                DialogResult answer;using(var prompt=new SaveBeforeExitDialog(TextValue(state,"reason"),TextValue(state,"lastSavedText")))answer=prompt.ShowDialog(this);
                if(answer==DialogResult.Cancel)return;
                if(answer==DialogResult.Yes){Text="订单工作台 v1.14 · 正在保存后退出";var saved=await SaveForClose();if(!Flag(saved,"ok")){MessageBox.Show(this,"没有退出，当前编辑仍保留。\n\n"+TextValue(saved,"error"),"保存未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}state=await CloseState();if(Flag(state,"dirty")||Flag(state,"saving")){MessageBox.Show(this,"仍有未保存内容，已取消退出。","请先保存",MessageBoxButtons.OK,MessageBoxIcon.Warning);return;}}
            }
            await browser.ExecuteScriptAsync("window.workbench && window.workbench.allowClose();");allowClose=true;BeginInvoke(new Action(Close));
        }catch(Exception ex){MessageBox.Show(this,"已取消退出，不能确认数据已保存。\n\n"+ex.Message,"请保留当前窗口",MessageBoxButtons.OK,MessageBoxIcon.Warning);}
        finally {if(!IsDisposed&&!allowClose){browser.Enabled=true;closeInProgress=false;Text="订单工作台 v1.14 · "+(latestState=="error"?"保存失败":latestState=="dirty"?"未保存":"继续编辑");}}
    }
}

internal sealed class SaveBeforeExitDialog : Form {
    internal SaveBeforeExitDialog(string reason,string lastSaved){
        Text="有未保存的更改";Font=new Font("Microsoft YaHei UI",9.5f);StartPosition=FormStartPosition.CenterParent;FormBorderStyle=FormBorderStyle.FixedDialog;MaximizeBox=false;MinimizeBox=false;ShowInTaskbar=false;ClientSize=new Size(600,215);BackColor=Color.White;AutoScaleMode=AutoScaleMode.Dpi;
        var heading=new Label{Text="退出前是否保存当前更改？",Font=new Font("Microsoft YaHei UI",12,FontStyle.Bold),AutoSize=true,Location=new Point(24,22)};
        var details=new Label{Text=reason,Location=new Point(25,60),Size=new Size(550,43),ForeColor=Color.FromArgb(89,107,98)};
        var time=new Label{Text="最近保存："+(String.IsNullOrEmpty(lastSaved)?"暂无保存记录":lastSaved),AutoSize=true,Location=new Point(25,108),ForeColor=Color.Gray};
        var save=new Button{Text="保存并退出",DialogResult=DialogResult.Yes,Location=new Point(195,158),Size=new Size(120,34),BackColor=Color.FromArgb(8,124,112),ForeColor=Color.White,FlatStyle=FlatStyle.Flat};
        var discard=new Button{Text="不保存退出",DialogResult=DialogResult.No,Location=new Point(325,158),Size=new Size(120,34)};
        var cancel=new Button{Text="取消",DialogResult=DialogResult.Cancel,Location=new Point(455,158),Size=new Size(120,34)};
        Controls.AddRange(new Control[]{heading,details,time,save,discard,cancel});AcceptButton=save;CancelButton=cancel;
    }
}
