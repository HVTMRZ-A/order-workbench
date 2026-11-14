'use strict';
let demoTracking=[],demoTrackingAudit=[],trackingDraft=null,trackingBaseline='';
const activeTracking=()=>demo?demoTracking:(db.tracking||[]);
const activeTrackingAudit=()=>demo?demoTrackingAudit:(db.trackingAudit||[]);
function pendingTracking(){return !!$('#tracking-dialog')?.open&&trackingSnapshot()!==trackingBaseline;}
function trackingSnapshot(){return JSON.stringify({text:$('#tracking-text').value,contract:$('#tracking-contract').value});}
function trackingDate(at){return at?new Date(at).toLocaleString('zh-CN',{hour12:false}):'未记录时间';}
function renderTracking(){
 const items=activeTracking(),live=items.filter(x=>!x.archived),pending=live.filter(x=>!x.confirmed).length,confirmed=live.length-pending;$('#nav-tracking').textContent=pending;
 $('#tracking-panel').hidden=view!=='tracking';$('.orders-panel').hidden=view==='tracking';$('.heading-import').hidden=view==='tracking';
 if(view!=='tracking')return;
 $('#page-subtitle').textContent='自行填写需要跟进的细节，勾选即可确认。已确认事项可归档，之后仍可查看和恢复。';
 $('#tracking-summary').textContent=live.length+' 项未归档 · '+pending+' 待确认 · '+confirmed+' 已确认 · '+(items.length-live.length)+' 已归档';
 $('#tracking-archive-confirmed').disabled=saving||!confirmed;$('#tracking-archive-confirmed').textContent='归档全部已确认'+(confirmed?'（'+confirmed+'）':'');
 const rows=Tracking.filter(items,$('#tracking-filter').value,$('#tracking-search').value);
 $('#tracking-list').innerHTML=rows.map(item=>{
  const orders=activeOrders().filter(o=>P.contract(o)===item.contract),finished=orders.length&&orders.every(W.complete),archived=orders.length&&orders.every(o=>o.archived);
  return `<article class="tracking-item ${item.confirmed?'confirmed':''}"><label class="tracking-check"><input type="checkbox" data-tracking-check="${esc(item.id)}" ${item.confirmed?'checked':''} ${saving||item.archived?'disabled':''} aria-label="确认事项：${esc(item.text)}"><span class="tracking-content"><b>${esc(item.text)}</b><small>${item.contract?esc(item.contract)+(archived?' · 订单已归档':finished?' · 订单已完成':''):'独立事项'}<span> · ${item.archived?'已归档':item.confirmed?'已确认':'待确认'}</span>${item.archived&&item.archivedAt?' · '+esc(trackingDate(item.archivedAt)):''}</small></span></label><div class="tracking-item-actions">${item.archived?`<button class="secondary small" data-tracking-restore="${esc(item.id)}" ${saving?'disabled':''}>恢复</button>`:`<button class="secondary small" data-tracking-edit="${esc(item.id)}" ${saving?'disabled':''}>编辑</button>${item.confirmed?`<button class="secondary small" data-tracking-archive="${esc(item.id)}" ${saving?'disabled':''}>归档</button>`:''}`}<button class="text-btn" data-tracking-delete="${esc(item.id)}" ${saving?'disabled':''}>删除</button></div></article>`;
 }).join('')||`<div class="empty"><div class="empty-symbol">☑</div><h3>${items.length?'暂无匹配的事项':'把需要继续留意的细节记在这里'}</h3><p>${items.length?'调整搜索或确认状态，查看其他事项。':'内容由你填写，不设固定流程；之后勾选确认即可。'}</p>${items.length?'':'<button class="primary" data-tracking-new>＋ 新增事项</button>'}</div>`;
}
function openTracking(id=''){
 if(saving||!dataReady)return;trackingDraft=id?activeTracking().find(x=>x.id===id):null;if(id&&(!trackingDraft||trackingDraft.archived))return;
 const groups=P.groups(activeOrders());const options=groups.map(g=>({contract:P.contract(g[0]),label:P.alias(g[0])+' · '+P.contract(g[0])+(g.every(o=>o.archived)?' · 已归档':g.every(W.complete)?' · 已完成':'')}));
 if(trackingDraft?.contract&&!options.some(x=>x.contract===trackingDraft.contract))options.push({contract:trackingDraft.contract,label:trackingDraft.contract});
 $('#tracking-contract').innerHTML='<option value="">独立事项（不关联订单）</option>'+options.map(x=>`<option value="${esc(x.contract)}">${esc(x.label)}</option>`).join('');
 $('#tracking-contract').value=trackingDraft?.contract||'';$('#tracking-text').value=trackingDraft?.text||'';$('#tracking-title').textContent=trackingDraft?'编辑追踪事项':'新增追踪事项';$('#tracking-note').textContent='只需填写事项内容；关联订单可选。';trackingBaseline=trackingSnapshot();$('#tracking-dialog').showModal();$('#tracking-text').focus();syncSaveStatus();
}
function closeTracking(){if(saving)return;if(pendingTracking()&&!confirm('事项尚未保存，放弃本次编辑吗？'))return;$('#tracking-dialog').close();trackingDraft=null;syncSaveStatus();}
async function persistTracking(items,history=[]){
 const before=activeTracking();await persist(activeOrders(),items,history);
 if(demo){const now=new Date().toISOString();for(const item of items){const old=before.find(x=>x.id===item.id);if(JSON.stringify(item)!==JSON.stringify(old))demoTrackingAudit.push({id:crypto.randomUUID(),at:now,name:accountSession?.name||'示例体验',username:accountSession?.username||'demo',kind:old?'tracking-edit':'tracking-create',itemId:item.id,text:item.text,contract:item.contract,oldContract:old?.contract||'',changes:[{field:'text',label:'追踪事项',before:old||null,after:item}]});}}
}
async function saveTracking(){
 if(saving)return false;if(!$('#tracking-form').reportValidity()){lastSaveProblem='请填写追踪事项内容。';return false;}
 const text=$('#tracking-text').value.trim();if(!text){lastSaveProblem='请填写追踪事项内容。';$('#tracking-note').textContent=lastSaveProblem;return false;}
 const now=new Date().toISOString(),item={...(trackingDraft||{id:crypto.randomUUID(),confirmed:false,archived:false,createdAt:now,confirmedAt:'',archivedAt:''}),text,contract:$('#tracking-contract').value,updatedAt:now};
 const items=activeTracking().map(x=>({...x})),index=items.findIndex(x=>x.id===item.id);if(index>=0)items[index]=item;else items.push(item);
 try{Tracking.validate(items);await persistTracking(items);$('#tracking-dialog').close();trackingDraft=null;render();toast('追踪事项已保存');return true;}
 catch(e){lastSaveProblem=errMsg(e);$('#tracking-note').textContent='保存未成功：'+lastSaveProblem+'。编辑内容已保留。';toast(lastSaveProblem,true);return false;}finally{syncSaveStatus();}
}
async function changeTracking(id,confirmed){
 if(saving){renderTracking();return;}const items=activeTracking().map(x=>({...x})),item=items.find(x=>x.id===id);if(!item||item.archived)return;
 item.confirmed=confirmed;item.updatedAt=new Date().toISOString();item.confirmedAt=confirmed?item.updatedAt:'';
 try{await persistTracking(items);render();toast(confirmed?'事项已确认并保存':'已恢复待确认并保存');}catch(e){renderTracking();toast('确认未保存：'+errMsg(e),true);}
}
async function archiveTracking(ids,archived=true){
 if(saving||!dataReady)return;
 try{const items=Tracking.archive(activeTracking(),ids,archived);await persistTracking(items);render();toast(archived?'事项已归档并保存，可在“已归档”中查看':'事项已恢复并保存');}catch(e){renderTracking();toast((archived?'归档':'恢复')+'未保存：'+errMsg(e),true);}
}
function archiveConfirmedTracking(){
 const items=activeTracking().filter(x=>!x.archived&&x.confirmed);if(saving||!items.length)return;
 if(confirm('归档全部 '+items.length+' 条已确认事项？\n归档后可在“已归档”中查看或恢复。'))archiveTracking(items.map(x=>x.id));
}
async function deleteTracking(id){
 const item=activeTracking().find(x=>x.id===id);if(!item||saving||!confirm('删除这条追踪事项？\n'+item.text))return;
 try{await persistTracking(activeTracking().filter(x=>x.id!==id));render();toast('事项已删除，修改记录仍保留');}catch(e){toast('删除未保存：'+errMsg(e),true);}
}
function trackingAuditValue(value,field){if(field==='archived')return value===null?'未建立':value?'已归档':'未归档';if(field==='confirmed')return value===null?'未建立':value?'已确认':'待确认';return auditValue(value,field);}
function trackingAuditHTML(entries,depth=0){
 if(depth>6)return '<p>更早的外部历史请在导出的完整备份中查看。</p>';
 return entries.filter(x=>x&&typeof x==='object').slice().sort((a,b)=>String(b.at||'').localeCompare(String(a.at||''))).map(entry=>`<article class="audit-entry"><div class="audit-entry-head"><b>${esc(accountLabel(entry))}</b><time>${esc(trackingDate(entry.at))}</time></div><p class="audit-model">持续追踪 · ${esc(entry.text||'备份历史')}${entry.kind==='tracking-delete'?' · 已删除':entry.kind==='tracking-archive'?' · 已归档':entry.kind==='tracking-restore'?' · 已恢复':''}${entry.contract?' · '+esc(entry.contract):''}${entry.imported?' · 外部备份原始记录':''}</p><dl>${(Array.isArray(entry.changes)?entry.changes:[]).filter(c=>c&&typeof c==='object').map(c=>`<div><dt>${esc(c.label)}</dt><dd><span>${esc(trackingAuditValue(c.before,c.field))}</span><span class="audit-arrow">→</span><span>${esc(trackingAuditValue(c.after,c.field))}</span></dd></div>`).join('')}</dl>${Array.isArray(entry.importedAudit)&&entry.importedAudit.length?'<details><summary>查看外部备份原始记录（账户与时间未经本机验证）</summary>'+trackingAuditHTML(entry.importedAudit.map(x=>({...x,imported:true})),depth+1)+'</details>':''}</article>`).join('');
}
function openTrackingAudit(){
 $('#audit-title').textContent='持续追踪 · 修改记录';$('#audit-summary').textContent='新增、修改、确认、取消确认、归档、恢复及删除均按当前账户留痕。';$('#audit-list').innerHTML=trackingAuditHTML(activeTrackingAudit())||'<div class="hint-box">暂无修改记录。</div>';$('#audit-legacy').innerHTML='';$('#audit-dialog').showModal();
}
$('#tracking-form').onsubmit=e=>{e.preventDefault();saveTracking();};$('#tracking-dialog').addEventListener('cancel',e=>{e.preventDefault();closeTracking();});
$('#tracking-search').oninput=renderTracking;$('#tracking-filter').onchange=renderTracking;$('#tracking-history').onclick=openTrackingAudit;$('#tracking-archive-confirmed').onclick=archiveConfirmedTracking;
document.addEventListener('click',e=>{const b=e.target.closest('[data-tracking-new],[data-tracking-edit],[data-tracking-delete],[data-tracking-cancel],[data-tracking-archive],[data-tracking-restore]');if(!b)return;if(b.hasAttribute('data-tracking-new'))openTracking();else if(b.hasAttribute('data-tracking-edit'))openTracking(b.dataset.trackingEdit);else if(b.hasAttribute('data-tracking-delete'))deleteTracking(b.dataset.trackingDelete);else if(b.hasAttribute('data-tracking-archive'))archiveTracking([b.dataset.trackingArchive]);else if(b.hasAttribute('data-tracking-restore'))archiveTracking([b.dataset.trackingRestore],false);else closeTracking();});
document.addEventListener('change',e=>{if(e.target.matches('[data-tracking-check]'))changeTracking(e.target.dataset.trackingCheck,e.target.checked);});
