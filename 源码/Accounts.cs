using System;
using System.IO;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;

internal static class Accounts {
    static readonly object Gate=new object();
    static readonly Dictionary<string,Session> Sessions=new Dictionary<string,Session>();
    sealed class Session {internal Dictionary<string,object> User;internal DateTime Until;}
    static JavaScriptSerializer Json(){return new JavaScriptSerializer{MaxJsonLength=4*1024*1024};}
    internal static string Text(Dictionary<string,object> d,string k){object v;return d!=null&&d.TryGetValue(k,out v)&&v is string?(string)v:"";}
    static Dictionary<string,object> Read(string dir){
        string p=Path.Combine(dir,"accounts.json");if(!File.Exists(p))return new Dictionary<string,object>{{"schema",1},{"users",new object[0]}};
        var d=Json().DeserializeObject(File.ReadAllText(p)) as Dictionary<string,object>;object users;
        if(d==null||!d.ContainsKey("schema")||Convert.ToInt32(d["schema"])!=1||!d.TryGetValue("users",out users)||!(users is object[]))throw new InvalidDataException("账户文件无法读取，请保留文件并检查备份。");return d;
    }
    internal static bool HasUsers(string dir){lock(Gate)return ((object[])Read(dir)["users"]).Length>0;}
    internal static Dictionary<string,object> Public(Dictionary<string,object> user){return user==null?null:new Dictionary<string,object>{{"id",user["id"]},{"username",user["username"]},{"name",user["name"]}};}
    internal static Dictionary<string,object> Current(string cookie){
        if(String.IsNullOrEmpty(cookie))return null;string key="";foreach(string part in cookie.Split(';')){var item=part.Trim();if(item.StartsWith("ow_session="))key=item.Substring(11);}
        lock(Gate){Session s;if(!Sessions.TryGetValue(key,out s))return null;if(s.Until<DateTime.UtcNow){Sessions.Remove(key);return null;}s.Until=DateTime.UtcNow.AddHours(8);return s.User;}
    }
    static byte[] Derive(string password,byte[] salt,int iterations){using(var kdf=new Rfc2898DeriveBytes(password,salt,iterations))return kdf.GetBytes(32);}
    static bool Equal(byte[] a,byte[] b){int difference=a.Length^b.Length;for(int i=0;i<Math.Min(a.Length,b.Length);i++)difference|=a[i]^b[i];return difference==0;}
    internal static Dictionary<string,object> Create(string dir,Dictionary<string,object> input,Dictionary<string,object> current){lock(Gate){
        var data=Read(dir);var users=new List<object>((object[])data["users"]);
        if(users.Count>0&&current==null)throw new InvalidDataException("请先登录已有账户，再创建其他账户。");
        string username=Text(input,"username").Trim(),name=Text(input,"name").Trim(),password=Text(input,"password");
        if(!Regex.IsMatch(username,@"^[\p{L}\p{N}_.-]{2,40}$"))throw new InvalidDataException("账号为2至40个文字、数字、点、横线或下划线。");
        if(name.Length<1||name.Length>50)throw new InvalidDataException("请填写1至50字的账户姓名。");
        if(password.Length<8||password.Length>128)throw new InvalidDataException("密码需要8至128个字符。");
        if(users.Count>=100)throw new InvalidDataException("本机最多支持100个账户。");
        foreach(Dictionary<string,object> u in users)if(String.Equals(Text(u,"username"),username,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("该账号已存在。");
        byte[] salt=new byte[24];using(var random=RandomNumberGenerator.Create())random.GetBytes(salt);const int rounds=600000;
        var user=new Dictionary<string,object>{{"id",Guid.NewGuid().ToString("N")},{"username",username},{"name",name},{"salt",Convert.ToBase64String(salt)},{"hash",Convert.ToBase64String(Derive(password,salt,rounds))},{"iterations",rounds},{"createdAt",DateTime.UtcNow.ToString("o")}};
        users.Add(user);data["users"]=users.ToArray();Atomic(Path.Combine(dir,"accounts.json"),Json().Serialize(data));return Public(user);
    }}
    static readonly Dictionary<string,DateTime> RetryAfter=new Dictionary<string,DateTime>();
    static readonly Dictionary<string,int> Failures=new Dictionary<string,int>();
    internal static Dictionary<string,object> Login(string dir,Dictionary<string,object> input,out string session){lock(Gate){
        string username=Text(input,"username").Trim(),key=username.ToUpperInvariant();DateTime until;
        if(RetryAfter.TryGetValue(key,out until)&&until>DateTime.UtcNow)throw new InvalidDataException("尝试次数较多，请一分钟后再登录。");
        Dictionary<string,object> user=null;foreach(Dictionary<string,object> u in (object[])Read(dir)["users"])if(String.Equals(Text(u,"username"),username,StringComparison.OrdinalIgnoreCase))user=u;
        if(user==null||!Equal(Derive(Text(input,"password"),Convert.FromBase64String(Text(user,"salt")),Convert.ToInt32(user["iterations"])),Convert.FromBase64String(Text(user,"hash")))){
            int n=0;Failures.TryGetValue(key,out n);Failures[key]=n+1;if(n>=4)RetryAfter[key]=DateTime.UtcNow.AddMinutes(1);throw new InvalidDataException("账号或密码不正确。");
        }
        Failures.Remove(key);RetryAfter.Remove(key);session=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");var safe=Public(user);
        Sessions[session]=new Session{User=safe,Until=DateTime.UtcNow.AddHours(8)};return safe;
    }}
    internal static void Logout(string cookie){lock(Gate){foreach(string part in (cookie??"").Split(';')){var item=part.Trim();if(item.StartsWith("ow_session="))Sessions.Remove(item.Substring(11));}}}
    static void Atomic(string file,string text){string temp=file+".tmp";byte[] bytes=System.Text.Encoding.UTF8.GetBytes(text);using(var output=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None)){output.Write(bytes,0,bytes.Length);output.Flush(true);}if(File.Exists(file))File.Replace(temp,file,file+".previous",true);else File.Move(temp,file);}
}
