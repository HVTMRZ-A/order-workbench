'use strict';
let auditPromptResolve=null,auditPromptRequests=[],auditDialogOrder='';
const riskStageNames={machine:'机器状态确认',purchase:'采购交接',silk:'丝印要求确认',nameplate:'铭牌要求确认',manual:'说明书要求确认',carton:'纸箱要求确认',plan:'计划交接'};
const riskCodeNames={machine:'机器料号',silk:'丝印料号',nameplate:'铭牌料号',manual:'说明书料号',carton:'纸箱料号'};
function riskRequests(orders){const before=new Map(activeOrders().map(o=>[o.id,o]));return orders.flatMap(o=>W.riskRequests(before.get(o.id),o).map(r=>({...r,orderId:o.id,number:P.display(o),model:o.model})));}
async function authorizeRisk(orders){
 await ensureAccount();const requests=riskRequests(orders);if(!requests.length)return [];
 if(auditPromptResolve)throw Error('请先完成当前风险确认');auditPromptRequests=requests;
 $('#risk-account').textContent=accountSession.name+'（'+accountSession.username+'）';$('#risk-items').innerHTML=requests.map(r=>`<li><b>${esc(r.number)} · ${esc(r.model)}</b><span>${esc(riskStageNames[r.stage])}：缺少 ${r.missing.map(k=>esc(riskCodeNames[k])).join('、')}</span></li>`).join('');$('#risk-ack').checked=false;$('#risk-confirm').disabled=true;$('#risk-dialog').showModal();syncSaveStatus();
 return new Promise((resolve,reject)=>{auditPromptResolve={resolve,reject};});
}
function closeRisk(accepted){const pending=auditPromptResolve;if(!pending)return;auditPromptResolve=null;$('#risk-dialog').close();const requests=auditPromptRequests.map(({orderId,stage,missing})=>({orderId,stage,missing,accepted:true,accountId:accountSession.id}));auditPromptRequests=[];if(accepted)pending.resolve(requests);else pending.reject(Error('已取消缺料号风险确认，当前编辑保留，尚未保存。'));syncSaveStatus();}
$('#risk-ack').onchange=()=>$('#risk-confirm').disabled=!$('#risk-ack').checked;$('#risk-form').onsubmit=e=>{e.preventDefault();if($('#risk-ack').checked)closeRisk(true);};$('#risk-cancel').onclick=()=>closeRisk(false);$('#risk-dialog').addEventListener('cancel',e=>{e.preventDefault();closeRisk(false);});
function auditValue(value,key){
 if(value===null||value===undefined||value==='')return '未填写';if(typeof value==='boolean')return value?'已确认 / 已完成':'待办理';if(key==='priority')return value==='urgent'?'加急':'普通';if(key==='plate')return W.PLATE[value]||value;if(key.endsWith('.status'))return W.STATUS[value]||value;if(key==='machineSource'||key.endsWith('.source'))return value==='new'?'新建料号':'已有料号';if(typeof value==='object'){if(value.path)return (value.stage?E.STAGES[value.stage]+' · ':'')+value.path+(value.note?' · '+value.note:'');return JSON.stringify(value);}return String(value);
}
function accountLabel(entry){return entry.username?(entry.name||entry.username)+'（'+entry.username+'）':'旧版记录 · 未记录账户';}
function openAudit(id){
 const anchor=activeOrders().find(o=>o.id===id);if(!anchor)return;auditDialogOrder=id;const orders=activeOrders().filter(o=>P.contract(o)===P.contract(anchor));
 const rows=orders.flatMap(o=>(o.audit||[]).flatMap(entry=>[{...entry,order:o},...(entry.importedAudit||[]).map(old=>({...old,order:o,imported:true}))])).sort((a,b)=>b.at.localeCompare(a.at));
 const legacy=orders.flatMap(o=>(o.legacyHistory||(!o.audit?.length?o.history:[])||[]).map(h=>({...h,order:o}))).sort((a,b)=>b.at.localeCompare(a.at));
 $('#audit-title').textContent='修改记录 · '+P.alias(anchor);$('#audit-summary').textContent=P.contract(anchor)+' · '+orders.length+' 个机型 · '+rows.length+' 次账户操作';
 $('#audit-list').innerHTML=rows.map(entry=>`<article class="audit-entry"><div class="audit-entry-head"><b>${esc(accountLabel(entry))}</b><time>${esc(new Date(entry.at).toLocaleString('zh-CN',{hour12:false}))}</time></div><p class="audit-model">${esc(P.display(entry.order))} · ${esc(entry.order.model)}${entry.kind==='create'?' · 新建 / 导入':''}</p>${(entry.risks||[]).length?`<div class="audit-risk"><b>缺料号风险已明确确认</b>${entry.risks.map(r=>'<p>'+esc(riskStageNames[r.stage])+ '：'+r.missing.map(k=>esc(riskCodeNames[k])).join('、')+'；'+esc(r.warning)+'</p>').join('')}</div>`:''}<dl>${(entry.changes||[]).map(c=>`<div><dt>${esc(c.label)}</dt><dd><span>${esc(auditValue(c.before,c.field))}</span><span class="audit-arrow">→</span><span>${esc(auditValue(c.after,c.field))}</span></dd></div>`).join('')}</dl>${entry.importedAudit?.length?'<p class="settings-description">此处包含从备份带入的旧记录，保留其原始账户和时间，不作为当前账户新操作。</p>':''}</article>`).join('')||'<div class="hint-box">暂无本版账户修改记录。今后保存的修改会自动记录账户、时间和变更前后内容。</div>';
 $('#audit-legacy').innerHTML=legacy.length?'<details><summary>查看旧版跟进记录（未记录账户） · '+legacy.length+' 条</summary>'+legacy.map(h=>'<article class="audit-entry"><b>旧版记录 · 未记录账户</b><time>'+esc(new Date(h.at).toLocaleString('zh-CN',{hour12:false}))+'</time><p>'+esc(h.order.model)+' · '+esc(h.text)+'</p></article>').join('')+'</details>':'';
 $('#audit-dialog').showModal();
}
document.addEventListener('click',e=>{const button=e.target.closest('[data-audit-order]');if(button){e.stopImmediatePropagation();openAudit(button.dataset.auditOrder);}},true);
