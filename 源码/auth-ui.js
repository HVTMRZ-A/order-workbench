'use strict';
let accountSession=null,accountPromise=null,accountResolve=null,accountCreating=false,accountBootstrap=false,accountSwitching=false,accountFormBusy=false;
const aq=s=>document.querySelector(s);
async function accountApi(route,body){const response=await fetch('api/auth/'+route,{method:body===undefined?'GET':'POST',credentials:'same-origin',headers:{'Content-Type':'application/json','X-Workbench-Token':location.pathname.split('/').filter(Boolean)[0]},body:body===undefined?undefined:JSON.stringify(body)});let result;try{result=await response.json();}catch{throw Error('账户服务返回异常，请保留窗口后重试。');}if(!response.ok)throw Error(result.error||'账户操作未完成');return result;}
function renderAccount(){aq('#account-current').textContent=accountSession?accountSession.name+' · '+accountSession.username:'登录账户';aq('#account-current').title=accountSession?'当前登录账户：'+accountSession.username:'登录后记录操作人';const el=aq('#settings-account-name');if(el)el.textContent=accountSession?accountSession.name+'（'+accountSession.username+'）':'尚未登录';}
function showAccountForm(create,bootstrap=false){
 accountCreating=create;accountBootstrap=bootstrap;accountSwitching=!create&&!!accountSession;const form=aq('#account-form');form.reset();aq('#account-title').textContent=create?(bootstrap?'创建首个本机账户':'添加本机账户'):accountSwitching?'切换账户':'登录订单工作台';aq('#account-intro').textContent=bootstrap?'已有订单会保留。创建账户后，后续修改和风险确认将记录到实际登录账户。':create?'新账户与现有账户共用本机订单数据，各自的操作分别留痕。创建后会切换到新账户。':accountSwitching?'输入要切换的账号和密码。验证成功前仍保留当前账户，取消可直接返回。':'使用本机账户登录。修改记录和缺料号风险确认会记到当前账户名下。';aq('#account-name-row').hidden=!create;aq('#account-name').required=create;aq('#account-submit').textContent=create?'创建并登录':accountSwitching?'验证并切换':'登录';aq('#account-password').autocomplete=create?'new-password':'current-password';aq('#account-note').textContent='';aq('#account-back').hidden=!accountSession;aq('#account-back').textContent=accountSwitching?'取消切换':'返回';document.body.classList.toggle('account-locked',!accountSession);if(!aq('#account-dialog').open)aq('#account-dialog').showModal();
}
async function ensureAccount(){
 if(accountSession)return accountSession;if(accountPromise)return accountPromise;
 accountPromise=(async()=>{const status=await accountApi('me');if(status.user){accountSession=status.user;renderAccount();document.body.classList.remove('account-locked');return accountSession;}return new Promise(resolve=>{accountResolve=resolve;showAccountForm(!status.hasUsers,!status.hasUsers);});})();
 try{return await accountPromise;}finally{accountPromise=null;}
}
aq('#account-form').addEventListener('submit',async e=>{
 e.preventDefault();if(accountFormBusy||!e.target.reportValidity())return;accountFormBusy=true;const button=aq('#account-submit');button.disabled=true;aq('#account-back').disabled=true;aq('#account-note').textContent='正在验证账户…';
 try{const fields={username:aq('#account-username').value.trim(),name:aq('#account-name').value.trim(),password:aq('#account-password').value};if(accountCreating)await accountApi('create',fields);const r=await accountApi('login',fields);accountSession=r.user;aq('#account-password').value='';renderAccount();aq('#account-dialog').close();document.body.classList.remove('account-locked');if(accountResolve){accountResolve(accountSession);accountResolve=null;}aq('#account-note').textContent='';}
 catch(error){aq('#account-note').textContent=error.message;}finally{accountFormBusy=false;button.disabled=false;aq('#account-back').disabled=false;}
});
aq('#account-dialog').addEventListener('cancel',e=>{e.preventDefault();if(accountSession&&!accountFormBusy){aq('#account-password').value='';aq('#account-dialog').close();}});aq('#account-back').onclick=()=>{if(accountSession&&!accountFormBusy){aq('#account-password').value='';aq('#account-dialog').close();}};
async function switchAccount(create=false){
 if(typeof hasPendingChanges==='function'&&(hasPendingChanges()||saving||evidenceBusy)){toast('请先保存或取消当前编辑，再切换或添加账户。',true);return;}
 if(aq('#settings-dialog').open)cancelSettings();
 if(create){showAccountForm(true);return;}
 showAccountForm(false);
}
function openAccountMenu(){if(!accountSession){void ensureAccount();return;}if(aq('dialog[open]'))return;aq('#account-menu-user').textContent=accountSession.name;aq('#account-menu-id').textContent='账号：'+accountSession.username;aq('#account-menu').showModal();}
function closeAccountMenu(){aq('#account-menu').close();}
async function logoutAccount(){
 if(typeof hasPendingChanges==='function'&&(hasPendingChanges()||saving||evidenceBusy)){toast('请先保存或取消当前编辑，再退出登录。',true);return;}
 if(aq('#settings-dialog').open)cancelSettings();closeAccountMenu();
 try{await accountApi('logout',{});accountSession=null;renderAccount();await ensureAccount();if(typeof render==='function')render();}catch(e){toast(e.message,true);}
}
async function exitFromAccount(){
 if(window.chrome?.webview){window.chrome.webview.postMessage({type:'request-close'});return;}
 if(accountSession){aq('#quit').click();return;}
 try{const token=location.pathname.split('/').filter(Boolean)[0],r=await fetch('api/quit',{method:'POST',headers:{'Content-Type':'application/json','X-Workbench-Token':token},body:'{}'});if(!r.ok)throw Error('退出未完成，请重试。');if(window.workbench)window.workbench.allowClose();document.body.innerHTML='<div class="error-screen"><h1>工作台已退出</h1><p>数据已保留，可关闭此窗口。</p></div>';}catch(e){aq('#account-note').textContent=e.message;}
}
aq('#account-current').onclick=openAccountMenu;aq('#account-switch').onclick=()=>switchAccount(false);aq('#account-add').onclick=()=>switchAccount(true);
aq('#account-menu-close').onclick=closeAccountMenu;aq('#account-menu-switch').onclick=()=>{closeAccountMenu();void switchAccount(false);};aq('#account-menu-add').onclick=()=>{closeAccountMenu();void switchAccount(true);};aq('#account-menu-logout').onclick=logoutAccount;aq('#account-menu-exit').onclick=exitFromAccount;aq('#account-exit').onclick=exitFromAccount;
