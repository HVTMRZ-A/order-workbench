using System;
using System.IO;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Diagnostics;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;

internal sealed class IconPlan {public string Target,Data,Icon,Stage,ExeHash,IconHash,Version;public int ParentId;public long ParentStarted;}
internal static class NativeIcons {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr BeginUpdateResource(string file,bool deleteExisting);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool UpdateResource(IntPtr update,IntPtr type,IntPtr name,ushort language,byte[] data,uint size);
    [DllImport("kernel32.dll",SetLastError=true)]static extern bool EndUpdateResource(IntPtr update,bool discard);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr LoadLibraryEx(string file,IntPtr h,uint flags);
    [DllImport("kernel32.dll")]static extern bool FreeLibrary(IntPtr h);
    delegate bool ResourceName(IntPtr module,IntPtr type,IntPtr name,IntPtr parameter);
    delegate bool ResourceLanguage(IntPtr module,IntPtr type,IntPtr name,ushort language,IntPtr parameter);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool EnumResourceNames(IntPtr module,IntPtr type,ResourceName callback,IntPtr parameter);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern bool EnumResourceLanguages(IntPtr module,IntPtr type,IntPtr name,ResourceLanguage callback,IntPtr parameter);
    [DllImport("shell32.dll",CharSet=CharSet.Unicode)]static extern void SHChangeNotify(uint events,uint flags,string item,IntPtr other);
    static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=2*1024*1024};}
    static void Require(bool ok,string message){if(!ok)throw new InvalidDataException(message);}
    static void Lines(Graphics g,Pen pen,params float[] values){for(int i=0;i+3<values.Length;i+=2)g.DrawLine(pen,values[i],values[i+1],values[i+2],values[i+3]);}
    internal static byte[] Render(Dictionary<string,object> settings){
        SettingsStore.Validate(settings);string key=(string)settings["icon"];
        using(var image=new Bitmap(256,256,PixelFormat.Format32bppArgb))using(var g=Graphics.FromImage(image)){
            g.Clear(Color.Transparent);g.SmoothingMode=SmoothingMode.AntiAlias;g.InterpolationMode=InterpolationMode.HighQualityBicubic;
            if(key=="custom"){var raw=Convert.FromBase64String(((string)settings["image"]).Substring("data:image/png;base64,".Length));using(var stream=new MemoryStream(raw))using(var original=Image.FromStream(stream))g.DrawImage(original,0,0,256,256);}
            else{
                using(var path=new GraphicsPath()){path.AddArc(8,8,72,72,180,90);path.AddArc(176,8,72,72,270,90);path.AddArc(176,176,72,72,0,90);path.AddArc(8,176,72,72,90,90);path.CloseFigure();using(var fill=new SolidBrush(Color.FromArgb(13,124,112)))g.FillPath(fill,path);}
                if(key=="sequence"){using(var font=new Font("SimSun",132,FontStyle.Regular,GraphicsUnit.Pixel))using(var brush=new SolidBrush(Color.White)){var format=new StringFormat{Alignment=StringAlignment.Center,LineAlignment=StringAlignment.Center};g.DrawString("序",font,brush,new RectangleF(10,4,236,244),format);}}
                else{g.TranslateTransform(40,30);g.ScaleTransform(7.3f,7.3f);using(var pen=new Pen(Color.White,1.55f)){pen.StartCap=pen.EndCap=LineCap.Round;pen.LineJoin=LineJoin.Round;
                    if(key=="grid"){g.DrawRectangle(pen,3,3,7,7);g.DrawRectangle(pen,14,3,7,7);g.DrawRectangle(pen,3,14,7,7);g.DrawRectangle(pen,14,14,7,7);}
                    if(key=="flow"){g.DrawRectangle(pen,6,5,12,5);g.DrawRectangle(pen,6,15,12,5);Lines(g,pen,12,10,12,15);Lines(g,pen,9,17.5f,15,17.5f);}
                    if(key=="box"){Lines(g,pen,12,3,21,8,12,13,3,8,12,3);Lines(g,pen,3,8,3,18,12,23,21,18,21,8);Lines(g,pen,12,13,12,23);Lines(g,pen,7.5f,5.5f,16.5f,10.5f);}
                    if(key=="check"){Lines(g,pen,9,3,15,3,16,6,20,6,20,21,4,21,4,6,8,6,9,3);Lines(g,pen,9,3,9,8,15,8,15,3);Lines(g,pen,8,14,11,17,16,11);}
                }}
            }
            var frames=new List<byte[]>();int[] sizes={16,24,32,48,64,128,256};foreach(int size in sizes)using(var scaled=new Bitmap(size,size,PixelFormat.Format32bppArgb))using(var draw=Graphics.FromImage(scaled))using(var png=new MemoryStream()){draw.InterpolationMode=InterpolationMode.HighQualityBicubic;draw.DrawImage(image,0,0,size,size);scaled.Save(png,ImageFormat.Png);frames.Add(png.ToArray());}
            using(var output=new MemoryStream())using(var writer=new BinaryWriter(output)){writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)sizes.Length);int offset=6+16*sizes.Length;for(int i=0;i<sizes.Length;i++){writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)(sizes[i]==256?0:sizes[i]));writer.Write((byte)0);writer.Write((byte)0);writer.Write((ushort)1);writer.Write((ushort)32);writer.Write(frames[i].Length);writer.Write(offset);offset+=frames[i].Length;}foreach(var frame in frames)writer.Write(frame);return output.ToArray();}
        }
    }
    internal static Icon FromSettings(Dictionary<string,object> settings){using(var stream=new MemoryStream(Render(settings)))using(var icon=new Icon(stream))return (Icon)icon.Clone();}
    internal static void SaveIcon(string data,Dictionary<string,object> settings){
        string file=Path.Combine(data,"program-icon.ico"),temp=file+".tmp";File.WriteAllBytes(temp,Render(settings));if(File.Exists(file))File.Replace(temp,file,null);else File.Move(temp,file);
    }
    internal static void RefreshShortcuts(string target,string data){
        object shell=null;try{
            var type=Type.GetTypeFromProgID("WScript.Shell");if(type==null)return;shell=Activator.CreateInstance(type);string icon=Path.Combine(data,"program-icon.ico");
            var paths=new List<string>{Path.Combine(Path.GetDirectoryName(target),"启动订单工作台.lnk")};foreach(string root in new[]{Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),Environment.GetFolderPath(Environment.SpecialFolder.Programs)})foreach(string name in new[]{"订单工作台.lnk","启动订单工作台.lnk","订单工作台.exe.lnk"}){string p=Path.Combine(root,name);if(File.Exists(p))paths.Add(p);}
            foreach(string path in paths){object link=null;try{link=type.InvokeMember("CreateShortcut",BindingFlags.InvokeMethod,null,shell,new object[]{path});var lt=link.GetType();bool exists=File.Exists(path);if(exists){string current=Convert.ToString(lt.InvokeMember("TargetPath",BindingFlags.GetProperty,null,link,null));if(!String.Equals(Path.GetFullPath(current),Path.GetFullPath(target),StringComparison.OrdinalIgnoreCase))continue;}else{lt.InvokeMember("TargetPath",BindingFlags.SetProperty,null,link,new object[]{target});lt.InvokeMember("WorkingDirectory",BindingFlags.SetProperty,null,link,new object[]{Path.GetDirectoryName(target)});lt.InvokeMember("Arguments",BindingFlags.SetProperty,null,link,new object[]{"--data-dir "+UpdateCore.Quote(data)});}lt.InvokeMember("IconLocation",BindingFlags.SetProperty,null,link,new object[]{icon+",0"});lt.InvokeMember("Save",BindingFlags.InvokeMethod,null,link,null);}finally{if(link!=null&&Marshal.IsComObject(link))Marshal.FinalReleaseComObject(link);}}
            SHChangeNotify(0x08000000,0, null,IntPtr.Zero);
        }catch{}finally{if(shell!=null&&Marshal.IsComObject(shell))Marshal.FinalReleaseComObject(shell);}
    }
    internal static void Schedule(string target,string data){
        try{
            var settings=SettingsStore.Read(data);SaveIcon(data,settings);RefreshShortcuts(target,data);string icon=Path.Combine(data,"program-icon.ico"),exeHash=UpdateCore.Hash(target),iconHash=UpdateCore.Hash(icon),marker=Path.Combine(data,"program-icon-state.json");
            if(File.Exists(marker))try{var state=Json().Deserialize<Dictionary<string,string>>(File.ReadAllText(marker));if(state["executableHash"]==exeHash&&state["iconHash"]==iconHash)return;}catch{}
            string stage=Path.Combine(Path.GetTempPath(),"OrderWorkbench-Icons",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(stage);File.Copy(icon,Path.Combine(stage,"icon.ico"));var plan=new IconPlan{Target=Path.GetFullPath(target),Data=Path.GetFullPath(data),Icon=Path.Combine(stage,"icon.ico"),Stage=stage,ExeHash=exeHash,IconHash=iconHash,Version=FileVersionInfo.GetVersionInfo(target).FileVersion,ParentId=Process.GetCurrentProcess().Id,ParentStarted=Process.GetCurrentProcess().StartTime.ToUniversalTime().Ticks};
            string planFile=Path.Combine(stage,"icon-plan.json"),helper=Path.Combine(stage,"icon-helper.exe");File.WriteAllText(planFile,Json().Serialize(plan));File.Copy(Path.Combine(Path.GetDirectoryName(target),"订单更新助手.exe"),helper);Process.Start(new ProcessStartInfo(helper,"--icon "+UpdateCore.Quote(planFile)){UseShellExecute=false,CreateNoWindow=true,WorkingDirectory=stage});
        }catch{}
    }
    sealed class Group {internal string Name;internal int Id;internal ushort Language;}
    internal static void Patch(string target,string icon){
        byte[] ico=File.ReadAllBytes(icon);Require(ico.Length>22&&BitConverter.ToUInt16(ico,0)==0&&BitConverter.ToUInt16(ico,2)==1,"图标格式无效");int count=BitConverter.ToUInt16(ico,4);Require(count>=1&&count<=16&&ico.Length>=6+16*count,"图标条目无效");
        var groups=new List<Group>();IntPtr module=LoadLibraryEx(target,IntPtr.Zero,0x22);Require(module!=IntPtr.Zero,"无法读取程序图标");try{ResourceName names=delegate(IntPtr h,IntPtr t,IntPtr n,IntPtr p){long number=n.ToInt64();string name=(number>>16)==0?null:Marshal.PtrToStringUni(n);int id=name==null?(int)number:0;ResourceLanguage languages=delegate(IntPtr mh,IntPtr mt,IntPtr mn,ushort lang,IntPtr mp){groups.Add(new Group{Name=name,Id=id,Language=lang});return true;};EnumResourceLanguages(h,t,n,languages,IntPtr.Zero);return true;};EnumResourceNames(module,new IntPtr(14),names,IntPtr.Zero);}finally{FreeLibrary(module);}Require(groups.Count>0,"程序缺少图标资源");
        IntPtr update=BeginUpdateResource(target,false);Require(update!=IntPtr.Zero,"不能修改程序图标");bool committed=false;try{
            foreach(var group in groups){using(var ms=new MemoryStream())using(var writer=new BinaryWriter(ms)){writer.Write((ushort)0);writer.Write((ushort)1);writer.Write((ushort)count);for(int i=0;i<count;i++){int pos=6+16*i;uint size=BitConverter.ToUInt32(ico,pos+8),offset=BitConverter.ToUInt32(ico,pos+12);Require(size>0&&offset>=6+16*count&&(long)offset+size<=ico.Length,"图标内容越界");byte[] frame=new byte[size];Array.Copy(ico,(int)offset,frame,0,(int)size);Require(UpdateResource(update,new IntPtr(3),new IntPtr(500+i),group.Language,frame,size),"写入图标失败");writer.Write(ico,pos,12);writer.Write((ushort)(500+i));}byte[] raw=ms.ToArray();IntPtr name=group.Name==null?new IntPtr(group.Id):Marshal.StringToHGlobalUni(group.Name);try{Require(UpdateResource(update,new IntPtr(14),name,group.Language,raw,(uint)raw.Length),"写入图标组失败");}finally{if(group.Name!=null)Marshal.FreeHGlobal(name);}}}
            Require(EndUpdateResource(update,false),"提交图标失败");committed=true;
        }finally{if(!committed)EndUpdateResource(update,true);}
    }
    internal static void ApplyPlan(string file){
        IconPlan plan=null;try{
            file=Path.GetFullPath(file);plan=Json().Deserialize<IconPlan>(File.ReadAllText(file));Require(plan!=null&&Path.GetFullPath(plan.Stage)==Path.GetDirectoryName(file)&&Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath)==plan.Stage,"图标助手目录不匹配");Require(Path.GetFileName(plan.Target)=="订单工作台.exe"&&Path.GetFullPath(plan.Icon)==Path.Combine(plan.Stage,"icon.ico"),"图标目标不匹配");Require(UpdateCore.Hash(plan.Icon)==plan.IconHash,"图标已变化");
            try{using(var parent=Process.GetProcessById(plan.ParentId)){if(parent.StartTime.ToUniversalTime().Ticks==plan.ParentStarted)Require(parent.WaitForExit(120000),"程序尚未退出");}}catch(ArgumentException){}
            for(int retry=0;retry<30;retry++){try{using(var dataLock=new FileStream(Path.Combine(plan.Data,"running.lock"),FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.None)){
                Require((File.GetAttributes(Path.GetDirectoryName(plan.Target))&FileAttributes.ReparsePoint)==0,"图标目标不能是链接");Require(UpdateCore.Hash(plan.Target)==plan.ExeHash&&FileVersionInfo.GetVersionInfo(plan.Target).FileVersion==plan.Version,"程序已经更新，本次图标更新取消");
                string temp=Path.Combine(Path.GetDirectoryName(plan.Target),".order-icon-"+Guid.NewGuid().ToString("N")+".exe");File.Copy(plan.Target,temp);try{Patch(temp,plan.Icon);Require(FileVersionInfo.GetVersionInfo(temp).FileVersion==plan.Version,"图标修改改变了程序版本");using(var icon=Icon.ExtractAssociatedIcon(temp))Require(icon!=null,"新程序图标无法读取");File.Replace(temp,plan.Target,Path.Combine(plan.Stage,"original-program.exe"));}finally{if(File.Exists(temp))File.Delete(temp);}
                File.WriteAllText(Path.Combine(plan.Data,"program-icon-state.json"),Json().Serialize(new Dictionary<string,string>{{"executableHash",UpdateCore.Hash(plan.Target)},{"iconHash",plan.IconHash}}));SHChangeNotify(0x2000,0x5,plan.Target,IntPtr.Zero);SHChangeNotify(0x08000000,0,null,IntPtr.Zero);File.WriteAllText(Path.Combine(plan.Stage,"result.txt"),"成功");return;
            }}catch(IOException){if(retry==29)throw;Thread.Sleep(1000);}}
        }catch(Exception ex){if(plan!=null)try{File.WriteAllText(Path.Combine(plan.Stage,"result.txt"),"未修改："+ex.Message);}catch{}}
    }
}
