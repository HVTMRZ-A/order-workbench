using System;
using System.IO;
using System.Diagnostics;
using System.Windows.Forms;
using System.Drawing;
using System.Threading.Tasks;

internal static class UpdaterProgram {
 [STAThread] static void Main(string[] args){Application.EnableVisualStyles();if(args.Length==2&&args[0]=="--icon"){NativeIcons.ApplyPlan(args[1]);return;}if(args.Length!=2||args[0]!="--apply")return;using(var form=new Form{Text="订单工作台 · 正在更新",ClientSize=new Size(460,120),StartPosition=FormStartPosition.CenterScreen,FormBorderStyle=FormBorderStyle.FixedDialog,MaximizeBox=false,MinimizeBox=false,ControlBox=false,Font=new Font("Microsoft YaHei UI",10)}){var text=new Label{Text="正在等待工作台退出，请勿拔出存储设备…",AutoSize=false,Location=new Point(22,24),Size=new Size(420,55)};form.Controls.Add(text);form.Shown+=async delegate{UpdatePlan plan=null;try{string path=Path.GetFullPath(args[1]);UpdateCore.Require(Path.GetDirectoryName(Application.ExecutablePath)==Path.GetDirectoryName(path),"更新助手与计划目录不一致");plan=UpdateCore.ReadPlan(path);await Task.Run(()=>UpdateCore.WaitForParent(plan));text.Text="正在替换程序文件，订单与依据路径保持不变…";await Task.Run(()=>{plan=UpdateCore.ReadPlan(path);UpdateCore.ApplyFiles(plan,null);});File.WriteAllText(Path.Combine(plan.Stage,"result.txt"),"成功："+plan.Version);Process.Start(new ProcessStartInfo(Path.Combine(plan.Target,"订单工作台.exe"),"--data-dir "+UpdateCore.Quote(plan.Data)){UseShellExecute=true,WorkingDirectory=plan.Target});form.Close();}catch(Exception ex){if(plan!=null)try{File.WriteAllText(Path.Combine(plan.Stage,"result.txt"),"失败："+ex.Message);}catch{}MessageBox.Show(form,"更新未完成，订单数据保持不变。\n\n"+ex.Message+"\n\n请重新打开原程序，或下载完整包手动覆盖；保留 data 文件夹。","更新未完成",MessageBoxButtons.OK,MessageBoxIcon.Warning);form.Close();}};Application.Run(form);}}
}
