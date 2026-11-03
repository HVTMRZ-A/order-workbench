using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

// 独立事项与订单使用同一次原子保存；操作账户和时间仅由本地服务生成。
internal static class TrackingItems {
    static object V(Dictionary<string,object> d,string k){object v;return d.TryGetValue(k,out v)?v:null;}
    static Dictionary<string,object> D(object o){return o as Dictionary<string,object>??new Dictionary<string,object>();}
    static string S(Dictionary<string,object> d,string k){return V(d,k) as string??"";}
    static object[] A(Dictionary<string,object> d,string k){return V(d,k) as object[]??new object[0];}
    static void Check(bool ok,string message){if(!ok)throw new InvalidDataException(message);}
    internal static void Validate(Dictionary<string,object> data){
        if(data.ContainsKey("tracking")){
            Check(data["tracking"] is object[],"持续追踪列表格式错误");var items=(object[])data["tracking"];Check(items.Length<=5000,"持续追踪最多支持 5000 项");var ids=new HashSet<string>();
            foreach(object raw in items){var item=D(raw);Check(S(item,"id").Length>0&&S(item,"id").Length<=100&&ids.Add(S(item,"id")),"持续追踪标识缺失或重复");Check(V(item,"text") is string&&S(item,"text").Trim().Length>0&&S(item,"text").Length<=4000,"请填写持续追踪事项，最多 4000 字");Check(V(item,"contract") is string&&S(item,"contract").Length<=200,"持续追踪关联订单格式错误");Check(V(item,"confirmed") is bool,"持续追踪确认状态格式错误");}
        }
        if(data.ContainsKey("trackingAudit"))Check(data["trackingAudit"] is object[],"持续追踪记录格式错误");
    }
    static object Change(string field,string label,object before,object after){return new Dictionary<string,object>{{"field",field},{"label",label},{"before",before},{"after",after}};}
    static Dictionary<string,object> Event(Dictionary<string,object> user,string at,string kind,Dictionary<string,object> before,Dictionary<string,object> after,object[] changes){
        return new Dictionary<string,object>{{"id",Guid.NewGuid().ToString("N")},{"at",at},{"accountId",user["id"]},{"username",user["username"]},{"name",user["name"]},{"kind",kind},{"itemId",S(after,"id")==""?S(before,"id"):S(after,"id")},{"text",S(after,"text")==""?S(before,"text"):S(after,"text")},{"contract",S(after,"contract")},{"oldContract",S(before,"contract")},{"changes",changes}};
    }
    internal static void Prepare(Dictionary<string,object> current,Dictionary<string,object> incoming,Dictionary<string,object> user){
        if(!incoming.ContainsKey("tracking")){
            if(current.ContainsKey("tracking"))incoming["tracking"]=current["tracking"];
            if(current.ContainsKey("trackingAudit"))incoming["trackingAudit"]=current["trackingAudit"];else incoming.Remove("trackingAudit");
            incoming.Remove("trackingImportedAudit");return;
        }
        Validate(incoming);string at=DateTime.UtcNow.ToString("o");var previous=A(current,"tracking").Select(D).ToDictionary(x=>S(x,"id"));var audit=new List<object>(A(current,"trackingAudit"));var result=new List<object>();var seen=new HashSet<string>();
        foreach(object raw in (object[])incoming["tracking"]){
            var item=D(raw);string id=S(item,"id"),text=S(item,"text").Trim(),contract=S(item,"contract").Trim();bool confirmed=(bool)item["confirmed"];seen.Add(id);Dictionary<string,object> before;if(!previous.TryGetValue(id,out before))before=new Dictionary<string,object>();
            var next=new Dictionary<string,object>{{"id",id},{"text",text},{"contract",contract},{"confirmed",confirmed},{"createdAt",before.Count>0?S(before,"createdAt"):at},{"updatedAt",at},{"confirmedAt",confirmed?(V(before,"confirmed") is bool&&(bool)before["confirmed"]?S(before,"confirmedAt"):at):""}};
            var changes=new List<object>();foreach(string key in new[]{"text","contract","confirmed"}){object old=V(before,key),value=next[key];if(!Object.Equals(old,value))changes.Add(Change(key,key=="text"?"追踪事项":key=="contract"?"关联订单":"事项确认",old,value));}
            if(changes.Count==0)result.Add(before);else{result.Add(next);audit.Add(Event(user,at,before.Count==0?"tracking-create":"tracking-edit",before,next,changes.ToArray()));}
        }
        foreach(var pair in previous)if(!seen.Contains(pair.Key))audit.Add(Event(user,at,"tracking-delete",pair.Value,new Dictionary<string,object>(),new object[]{Change("text","删除追踪事项",S(pair.Value,"text"),null)}));
        object imported;if(incoming.TryGetValue("trackingImportedAudit",out imported)){
            Check(imported is object[],"追踪备份历史格式错误");var history=(object[])imported;Check(history.Length<=20000,"追踪备份历史过多");
            if(history.Length>0){var entry=Event(user,at,"import-history",new Dictionary<string,object>(),new Dictionary<string,object>(),new object[]{Change("backup.trackingAudit","带入追踪历史（外部备份原始记录）",null,history.Length+" 条")});entry["importedAudit"]=history;audit.Add(entry);}
        }
        incoming["tracking"]=result.ToArray();incoming["trackingAudit"]=audit.ToArray();incoming.Remove("trackingImportedAudit");
    }
}
