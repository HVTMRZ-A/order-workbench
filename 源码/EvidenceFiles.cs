using System;
using System.IO;
using System.Diagnostics;
using System.Collections.Generic;
using System.Text.RegularExpressions;

internal static class EvidenceFiles {
    static readonly string[] stages={"machine","purchase","silk","nameplate","manual","carton","plate","plan","other"};
    static void Require(bool ok,string message){if(!ok)throw new InvalidDataException(message);}
    static string Text(Dictionary<string,object> data,string key){object value;return data.TryGetValue(key,out value)&&value is string?(string)value:"";}
    internal static string NormalizePath(string input){
        string path=(input??"").Trim();if(path.StartsWith("\"")&&path.EndsWith("\"")&&path.Length>=2)path=path.Substring(1,path.Length-2).Trim();path=path.Replace('/','\\');
        Require(path.Length>0&&path.Length<=4096,"请填写完整文件路径");Require(!path.StartsWith(@"\\?\")&&!path.StartsWith(@"\\.\"),"不支持设备路径");
        Require(!Regex.IsMatch(path,"[\\x00-\\x1f\"<>|?*]"),"文件路径包含无效字符");
        bool drive=Regex.IsMatch(path,@"^[A-Za-z]:\\");bool shared=Regex.IsMatch(path,@"^\\\\[^\\:]+\\[^\\:]+(?:\\.*)?$");Require(drive||shared,"请使用带盘符的完整路径或共享文件夹路径");Require(!path.Substring(drive?2:0).Contains(":"),"文件路径不能包含额外的冒号");
        return Path.GetFullPath(path);
    }
    internal static void ValidateLinks(Dictionary<string,object> order){
        object value;if(!order.TryGetValue("evidence",out value))return;var links=value as object[];Require(links!=null&&links.Length<=200,"依据文件列表格式错误或超过 200 个");var ids=new HashSet<string>();var unique=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach(object item in links){var entry=item as Dictionary<string,object>;Require(entry!=null,"依据文件记录必须是对象");foreach(string field in entry.Keys)Require(Array.IndexOf(new[]{"id","stage","path","note","addedAt"},field)>=0,"依据文件只允许保存路径和关联说明");
            foreach(string field in new[]{"id","stage","path","note","addedAt"})Require(entry.ContainsKey(field)&&entry[field] is string,"依据文件字段缺失："+field);
            string id=Text(entry,"id"),stage=Text(entry,"stage"),path=NormalizePath(Text(entry,"path"));Require(id.Length>0&&id.Length<=150&&ids.Add(id),"依据文件标识缺失或重复");Require(Array.IndexOf(stages,stage)>=0,"依据文件关联流程无效");Require(Text(entry,"note").Length<=2000&&Text(entry,"addedAt").Length<=100,"依据文件说明或时间格式错误");Require(unique.Add(stage+"|"+path),"同一步骤重复关联了相同路径");
        }
    }
    internal static object Status(string input){string path=NormalizePath(input);bool file=File.Exists(path),folder=!file&&Directory.Exists(path);return new{path=path,exists=file||folder,kind=file?"file":folder?"directory":"missing"};}
    internal static object Action(string input,string action){
        string path=NormalizePath(input);bool file=File.Exists(path),folder=!file&&Directory.Exists(path);
        if(action=="open"){
            Require(file||folder,"路径无法访问，文件可能已移动或共享目录暂未连接，请重新关联");
            if(folder)Process.Start(new ProcessStartInfo("explorer.exe","\""+path+"\""){UseShellExecute=true});
            else{string extension=Path.GetExtension(path).ToLowerInvariant();Require(Array.IndexOf(new[]{".exe",".com",".bat",".cmd",".ps1",".psm1",".vbs",".vbe",".js",".jse",".wsf",".wsh",".hta",".msi",".msp",".scr",".lnk",".url",".reg"},extension)<0,"此类文件请使用“定位”在文件夹中查看");Process.Start(new ProcessStartInfo(path){UseShellExecute=true});}
        }else if(action=="reveal"){
            if(file)Process.Start(new ProcessStartInfo("explorer.exe","/select,\""+path+"\""){UseShellExecute=true});
            else{string parent=folder?path:Path.GetDirectoryName(path);Require(Directory.Exists(parent),"所在文件夹无法访问，请检查路径或重新关联");Process.Start(new ProcessStartInfo("explorer.exe","\""+parent+"\""){UseShellExecute=true});}
        }else throw new InvalidDataException("不支持的文件操作");
        return new{ok=true};
    }
}
