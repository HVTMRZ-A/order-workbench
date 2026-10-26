using System;
using System.IO;
using System.Drawing;
using System.Collections.Generic;
using System.Web.Script.Serialization;

internal static class SettingsStore {
    internal static readonly object Gate=new object();
    static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=2*1024*1024};}
    static string Text(Dictionary<string,object> d,string k){object v;return d.TryGetValue(k,out v)&&v is string?(string)v:"";}
    internal static Dictionary<string,object> Defaults(){return new Dictionary<string,object>{{"schema",1},{"revision",0},{"theme","light"},{"icon","sequence"},{"image",""}};}
    internal static Dictionary<string,object> Read(string dir){
        string p=Path.Combine(dir,"settings.json");if(!File.Exists(p))return Defaults();
        if(new FileInfo(p).Length>2*1024*1024)throw new InvalidDataException("设置文件过大，请保留原文件并检查备份。");
        var d=Json().DeserializeObject(File.ReadAllText(p,System.Text.Encoding.UTF8)) as Dictionary<string,object>;
        Validate(d);return d;
    }
    internal static void Validate(Dictionary<string,object> d){
        object v;if(d==null||!d.TryGetValue("schema",out v)||!(v is int)||((int)v)!=1)throw new InvalidDataException("设置版本不支持。");
        if(!d.TryGetValue("revision",out v)||!(v is int)||((int)v)<0)throw new InvalidDataException("设置版本号无效。");
        if(Array.IndexOf(new[]{"light","dark","system"},Text(d,"theme"))<0)throw new InvalidDataException("请选择有效外观模式。");
        string icon=Text(d,"icon"),image=Text(d,"image");
        if(Array.IndexOf(new[]{"sequence","flow","box","grid","check","custom"},icon)<0)throw new InvalidDataException("图标选项无效。");
        if(image!=""){
            const string prefix="data:image/png;base64,";
            if(!image.StartsWith(prefix,StringComparison.Ordinal)||image.Length>300000)throw new InvalidDataException("自定义图标必须是工作台处理后的PNG图片。");
            byte[] raw;try{raw=Convert.FromBase64String(image.Substring(prefix.Length));}catch{throw new InvalidDataException("图标内容无效。");}
            if(raw.Length<24||raw[0]!=137||raw[1]!=80||raw[2]!=78||raw[3]!=71||raw[4]!=13||raw[5]!=10||raw[6]!=26||raw[7]!=10)throw new InvalidDataException("图片格式无效。");
            int width=(raw[16]<<24)|(raw[17]<<16)|(raw[18]<<8)|raw[19],height=(raw[20]<<24)|(raw[21]<<16)|(raw[22]<<8)|raw[23];
            if(width<1||height<1||width>256||height>256)throw new InvalidDataException("图标尺寸不正确。");
            using(var stream=new MemoryStream(raw))using(var img=Image.FromStream(stream,true,true)){if(img.Width!=width||img.Height!=height)throw new InvalidDataException("图标无法读取。");}
        }
        if(icon=="custom"&&image=="")throw new InvalidDataException("请先导入自定义图片。");
    }
    internal static Dictionary<string,object> Save(string dir,Dictionary<string,object> incoming){
        Validate(incoming);var current=Read(dir);
        if((int)current["revision"]!=(int)incoming["revision"])throw new SettingsConflictException();
        var next=Defaults();next["revision"]=(int)current["revision"]+1;next["theme"]=incoming["theme"];next["icon"]=incoming["icon"];next["image"]=incoming["image"];next["savedAt"]=DateTime.UtcNow.ToString("o");
        string file=Path.Combine(dir,"settings.json"),temp=file+".tmp";byte[] bytes=System.Text.Encoding.UTF8.GetBytes(Json().Serialize(next));
        using(var output=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){output.Write(bytes,0,bytes.Length);output.Flush(true);}
        if(File.Exists(file))File.Replace(temp,file,Path.Combine(dir,"settings.previous.json"),true);else File.Move(temp,file);
        return next;
    }
}
internal sealed class SettingsConflictException:Exception {internal SettingsConflictException():base("另一个窗口已修改设置。请取消后重新打开设置，再进行修改。"){} }
