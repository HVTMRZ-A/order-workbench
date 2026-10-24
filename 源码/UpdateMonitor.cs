using System;
using System.Threading.Tasks;

internal sealed class UpdateMonitor:IDisposable {
 readonly object gate=new object();readonly Func<Task<ReleaseInfo>> checker;readonly Func<DateTime> clock;
 Task<ReleaseInfo> flight;bool disposed,started;ReleaseInfo latest;DateTime checkedAt,lastSuccess,nextAutomatic;
 string state="idle",error="";
 internal event Action Changed;
 internal UpdateMonitor(Func<Task<ReleaseInfo>> check=null,Func<DateTime> now=null){checker=check??(()=>Task.Run(()=>UpdateCore.Check()));clock=now??(()=>DateTime.UtcNow);}
 internal void StartAutomatic(){lock(gate){if(started||disposed)return;started=true;nextAutomatic=clock().AddSeconds(5);}}
 internal bool ShouldCheckAutomatically(){lock(gate)return started&&!disposed&&flight==null&&clock()>=nextAutomatic;}
 internal UpdateStatus Status(){lock(gate){bool available=latest!=null&&UpdateCore.ParseVersion(latest.Version)>UpdateCore.CurrentVersion;return new UpdateStatus{State=state,Available=available,Version=latest==null?"":latest.Version,CheckedAt=checkedAt,Error=error,NextAutomatic=nextAutomatic};}}
 internal Task<ReleaseInfo> CheckAsync(bool force){
  TaskCompletionSource<ReleaseInfo> completion;Task<ReleaseInfo> pending;
  lock(gate){if(disposed)return Task.FromResult<ReleaseInfo>(null);if(flight!=null)return flight;if(!force&&latest!=null&&clock()-lastSuccess<TimeSpan.FromMinutes(5))return Task.FromResult(latest);completion=new TaskCompletionSource<ReleaseInfo>(TaskCreationOptions.RunContinuationsAsynchronously);flight=completion.Task;pending=flight;state="checking";error="";}
  Notify();Task running=Complete(completion);return pending;
 }
 async Task Complete(TaskCompletionSource<ReleaseInfo> completion){
  try{var result=await checker();if(result==null)throw new InvalidOperationException("更新检查没有返回版本信息");UpdateCore.ParseVersion(result.Version);lock(gate){latest=result;checkedAt=lastSuccess=clock();state=UpdateCore.ParseVersion(result.Version)>UpdateCore.CurrentVersion?"available":"current";error="";nextAutomatic=clock().AddHours(4);flight=null;}Notify();completion.TrySetResult(result);}
  catch(Exception ex){lock(gate){checkedAt=clock();error=UpdateCore.Friendly(ex);state="error";nextAutomatic=clock().AddMinutes(30);flight=null;}Notify();completion.TrySetException(ex);}
 }
 void Notify(){Action changed;lock(gate){if(disposed)return;changed=Changed;}if(changed!=null)try{changed();}catch{}}
 public void Dispose(){lock(gate){disposed=true;Changed=null;}}
}
internal sealed class UpdateStatus {public string State,Version,Error;public bool Available;public DateTime CheckedAt,NextAutomatic;}
