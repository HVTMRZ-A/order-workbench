using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Web.Script.Serialization;

internal static class AuditTrail {
    static readonly string[] Materials={"silk","nameplate","manual","carton"};
    static readonly string[] Stages={"machine","purchase","silk","nameplate","manual","carton","plan"};
    static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=32*1024*1024,RecursionLimit=100};}
    static Dictionary<string,object> D(object o){return o as Dictionary<string,object>??new Dictionary<string,object>();}
    static string S(Dictionary<string,object>d,string k){return Accounts.Text(d,k);}
    static object V(Dictionary<string,object>d,string k){object v;return d.TryGetValue(k,out v)?v:null;}
    static bool B(Dictionary<string,object>d,string k){return V(d,k) is bool&&(bool)d[k];}
    static Dictionary<string,object> M(Dictionary<string,object>o,string k){return D(V(D(V(o,"materials")),k));}
    static Dictionary<string,List<string>> Risks(Dictionary<string,object>o){
        var r=new Dictionary<string,List<string>>();bool machine=S(o,"machineCode").Trim()=="";
        if(machine&&B(o,"machineConfirmed"))r["machine"]=new List<string>{"machine"};
        if(machine&&B(o,"procurementSent"))r["purchase"]=new List<string>{"machine"};
        foreach(string key in Materials)if(S(M(o,key),"status")=="confirmed"&&S(M(o,key),"code").Trim()=="")r[key]=new List<string>{key};
        if(B(o,"planSent")){var missing=new List<string>();if(machine)missing.Add("machine");foreach(string key in Materials)if(S(M(o,key),"status")!="na"&&S(M(o,key),"code").Trim()=="")missing.Add(key);if(missing.Count>0)r["plan"]=missing;}
        return r;
    }
    static string Signature(IEnumerable<string> items){return String.Join(",",items.OrderBy(x=>x,StringComparer.Ordinal));}
    static string SavedSignature(Dictionary<string,object>ack){var raw=V(ack,"missing") as object[];return raw==null?"":Signature(raw.OfType<string>());}
    internal static bool Accepted(Dictionary<string,object>o,string stage){
        var risks=Risks(o);List<string> missing;if(!risks.TryGetValue(stage,out missing))return false;
        var ack=D(V(D(V(o,"riskAcceptances")),stage));return S(ack,"accountId")!=""&&S(ack,"at")!=""&&SavedSignature(ack)==Signature(missing);
    }
    static bool Equal(object a,object b){return Json().Serialize(a)==Json().Serialize(b);}
    static Dictionary<string,object> Clone(Dictionary<string,object>d){return D(Json().DeserializeObject(Json().Serialize(d)));}
    static readonly Dictionary<string,string> Fields=new Dictionary<string,string>{
        {"number","明细记录号"},{"alias","订单简称"},{"contractNumber","完整合同编号"},{"customer","客户"},{"customerCode","客户编号"},{"model","公司型号"},{"customerModel","客户型号"},{"quantity","数量"},{"unit","单位"},{"sales","对接业务"},{"due","交期"},{"priority","优先级"},{"notes","订单备注"},
        {"appearance","机器外形"},{"machineConfirmed","机器状态"},{"machineReference","机器确认依据"},{"machineSource","机器料号来源"},{"machineCode","机器料号"},{"procurementSent","采购交接"},{"procurementDate","采购日期"},{"procurementNote","采购备注"},{"plate","丝印网版"},{"plateNote","网版说明"},{"planSent","计划交接"},{"planDate","计划日期"},{"planNote","计划备注"},{"archived","归档状态"},
        {"productRequirements","产品要求"},{"packagingRequirements","包材要求"},{"category","产品大类"},{"sourceOperator","生产单经办人"},{"orderDate","下单日期"},{"orderProperty","订单属性"},{"transport","运输方式"}
    };
    static string Label(string key){return key=="machine"?"机器料号":key=="silk"?"丝印料号":key=="nameplate"?"铭牌料号":key=="manual"?"说明书料号":key=="carton"?"纸箱料号":key;}
    static object Change(string field,string label,object before,object after){return new Dictionary<string,object>{{"field",field},{"label",label},{"before",before},{"after",after}};}
    static List<object> Changes(Dictionary<string,object>before,Dictionary<string,object>after){
        var changes=new List<object>();foreach(var f in Fields){object a=V(before,f.Key),b=V(after,f.Key);if(!Equal(a,b)&&!(before.Count==0&&(b==null||b is string&&(string)b==""||b is bool&&!(bool)b)))changes.Add(Change(f.Key,f.Value,a,b));}
        foreach(string key in Materials)foreach(string name in new[]{"status","code","source","reference","note"}){object a=V(M(before,key),name),b=V(M(after,key),name);if(!Equal(a,b)&&!(before.Count==0&&(b==null||b is string&&(string)b=="")))changes.Add(Change("materials."+key+"."+name,Label(key).Replace("料号","")+new Dictionary<string,string>{{"status","确认状态"},{"code","料号"},{"source","料号来源"},{"reference","资料版本"},{"note","要求"}}[name],a,b));}
        if(!Equal(V(before,"evidence"),V(after,"evidence"))){var a=V(before,"evidence") as object[]??new object[0];var b=V(after,"evidence") as object[]??new object[0];foreach(var item in a){var d=D(item);if(!b.Any(x=>S(D(x),"id")==S(d,"id")))changes.Add(Change("evidence.remove","解除依据文件",d,null));}foreach(var item in b){var d=D(item);var old=a.FirstOrDefault(x=>S(D(x),"id")==S(d,"id"));if(!Equal(old,d))changes.Add(Change(old==null?"evidence.add":"evidence.edit",old==null?"新增依据文件":"修改依据文件",old,d));}}
        var history=V(after,"history") as object[]??new object[0];var oldHistory=V(before,"history") as object[]??new object[0];var notes=history.Select(D).Where(x=>S(x,"text").StartsWith("跟进：")&&!oldHistory.Any(y=>Equal(y,x))).Select(x=>S(x,"text")).ToArray();if(notes.Length>0)changes.Add(Change("followup","补充跟进记录",null,String.Join("；",notes)));
        return changes;
    }
    internal static void Prepare(Dictionary<string,object>current,Dictionary<string,object>incoming,Dictionary<string,object>user){
        var oldOrders=(object[])current["orders"];var requests=V(incoming,"riskAcknowledgements") as object[]??new object[0];string at=DateTime.UtcNow.ToString("o"),transaction=Guid.NewGuid().ToString("N");
        foreach(object raw in (object[])incoming["orders"]){
            var o=D(raw);var before=D(oldOrders.FirstOrDefault(x=>S(D(x),"id")==S(o,"id")));var risks=Risks(o);var oldRisks=Risks(before);var acknowledgements=new Dictionary<string,object>();var accepted=new List<object>();
            foreach(var risk in risks){var oldAck=D(V(D(V(before,"riskAcceptances")),risk.Key));List<string> oldMissing;
                bool prior=oldAck.Count>0&&SavedSignature(oldAck)==Signature(risk.Value)&&S(oldAck,"accountId")!="";
                bool changed=!oldRisks.TryGetValue(risk.Key,out oldMissing)||Signature(oldMissing)!=Signature(risk.Value);
                if(prior){acknowledgements[risk.Key]=oldAck;continue;}
                var approval=requests.Select(D).FirstOrDefault(x=>S(x,"orderId")==S(o,"id")&&S(x,"stage")==risk.Key&&SavedSignature(x)==Signature(risk.Value)&&B(x,"accepted")&&S(x,"accountId")==S(user,"id"));
                if(approval!=null){var entry=new Dictionary<string,object>{{"accountId",user["id"]},{"username",user["username"]},{"name",user["name"]},{"at",at},{"missing",risk.Value.ToArray()}};acknowledgements[risk.Key]=entry;accepted.Add(new Dictionary<string,object>{{"stage",risk.Key},{"missing",risk.Value.ToArray()},{"warning","料号缺失可能导致错料、错购或排产错误，需后续补齐并再次核对。"}});}
                else if(changed)throw new InvalidDataException("缺少料号：请先阅读风险提醒并由当前登录账户明确确认后再保存。");
            }
            o["riskAcceptances"]=acknowledgements;
            var audit=before.Count>0?new List<object>(V(before,"audit") as object[]??new object[0]):new List<object>();
            // Imported historical records remain labelled as imported; they never become current-account events.
            if(before.Count==0&&V(o,"audit") is object[]){var old=(object[])o["audit"];if(old.Length>0)audit.Add(new Dictionary<string,object>{{"id",Guid.NewGuid().ToString("N")},{"at",at},{"accountId",user["id"]},{"username",user["username"]},{"name",user["name"]},{"kind","import-history"},{"changes",new object[]{Change("backup.audit","从备份带入历史记录",null,old.Length+"条（原始记录随备份保留）")}},{"importedAudit",old}});}
            var changes=Changes(before,o);if(!Equal(V(before,"riskAcceptances"),acknowledgements)&&acknowledgements.Count==0&&D(V(before,"riskAcceptances")).Count>0)changes.Add(Change("risk.cleared","原缺料号风险已解除或确认已撤销",V(before,"riskAcceptances"),null));
            if(changes.Count>0||accepted.Count>0){audit.Add(new Dictionary<string,object>{{"id",Guid.NewGuid().ToString("N")},{"transaction",transaction},{"at",at},{"accountId",user["id"]},{"username",user["username"]},{"name",user["name"]},{"kind",before.Count==0?"create":"edit"},{"changes",changes.ToArray()},{"risks",accepted.ToArray()}});o["updatedAt"]=at;}
            o["audit"]=audit.ToArray();o["legacyHistory"]=V(before,"legacyHistory")??(before.Count>0?V(before,"history")??new object[0]:new object[0]);
            if(before.Count>0&&changes.Count==0&&accepted.Count==0)foreach(string key in new[]{"audit","legacyHistory","riskAcceptances","updatedAt"}){if(before.ContainsKey(key))o[key]=before[key];else o.Remove(key);}
        }
        incoming.Remove("riskAcknowledgements");incoming.Remove("actor");
    }
}
