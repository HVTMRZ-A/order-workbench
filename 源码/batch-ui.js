'use strict';
let batchContract='',batchAlias='',batchEvidence=[],batchTouched=new Set(),batchSavedEvidenceSignature='';
function batchRequest(){const form=$('#batch-form');if(!form)return null;return {contract:batchContract,ids:$$('[data-batch-id]:checked').map(el=>el.dataset.batchId),appearance:batchTouched.has('appearance')&&$('#batch-appearance').checked,machineConfirmed:batchTouched.has('machineConfirmed')&&$('#batch-machine').checked,materials:Object.fromEntries($$('[data-batch-material]').filter(el=>batchTouched.has('material.'+el.dataset.batchMaterial)&&el.value!=='keep').map(el=>[el.dataset.batchMaterial,el.value])),plate:batchTouched.has('plate')&&$('#batch-plate').value!=='keep'?$('#batch-plate').value:'',procurementSent:batchTouched.has('procurementSent')&&$('#batch-purchase').checked,planSent:batchTouched.has('planSent')&&$('#batch-plan').checked,note:$('#batch-note').value,evidence:batchEvidence,codeEdits:batchCodesRequest()};}
function batchPreview(){const request=batchRequest();return request?B.preview(activeOrders(),request):null;}
function batchMessage(text){$('#batch-message').textContent=text;$('#batch-message').hidden=false;$('#batch-message').scrollIntoView({block:'nearest'});}
function openBatch(id){
 if(!dataReady||saving||evidenceBusy){toast('正在读取或保存，请稍后再操作',true);return;}
 if(dirty()||pendingNote()||pendingNewOrder()||pendingProduction()||pendingBatch()){toast('请先保存或关闭当前编辑，再进行整单确认',true);return;}
 const anchor=activeOrders().find(o=>o.id===id);if(!anchor||anchor.archived){toast('请在未归档的订单中使用整单确认',true);return;}
 if(draft&&!closeDrawer())return;
 batchContract=P.contract(anchor);batchAlias=P.alias(anchor);batchEvidence=[];batchTouched.clear();resetBatchCodes();batchSavedEvidenceSignature='';
 const scope=activeOrders().filter(o=>P.contract(o)===batchContract&&!o.archived);
 const statusChoices={pending:'待确认',keep:'保持各机型原状态',confirmed:'已确认',requested:'已向业务索取',received:'资料已收到',na:'不需要'};
 $('#batch-content').innerHTML=`<form id="batch-form"><div class="dialog-heading"><div><div class="eyebrow">整单办理 · 逐项选择</div><h2>整单确认 · ${esc(batchAlias)}</h2></div><button type="button" class="icon-btn" data-close="batch-dialog" aria-label="关闭整单确认">×</button></div><p class="dialog-intro">${esc(batchContract)} · 全单 ${scope.length} 个未归档机型，包括搜索或筛选中未显示的机型。默认全选，可取消不适用的机型。</p><p class="batch-resume-note">已自动显示上次保存的状态，可接着办理。部分确认用横线标记；已完成的勾选锁定显示，本次只保存新选择的事项。包材可改回待确认，网版可改回待检查；选择“保持各机型原状态”撤销该项本次选择。机器和交接勾选的撤销请在机型详情中修改。</p><div class="batch-presets"><span>快捷选择</span><button type="button" class="secondary small" data-batch-preset="machine">机器状态已确认</button><button type="button" class="secondary small" data-batch-preset="requirements">全部要求已确认</button><button type="button" class="text-btn" data-batch-preset="clear">重置确认事项</button></div><div class="batch-fields"><section class="batch-section"><h3>机器与交接</h3><label class="check-row"><input type="checkbox" id="batch-appearance" data-batch-state="appearance" data-batch-change><span>机器外形已确认<small class="batch-saved-status" data-batch-saved="appearance"></small></span></label><label class="check-row"><input type="checkbox" id="batch-machine" data-batch-state="machineConfirmed" data-batch-change><span>机器状态已确认（同时确认外形）<small class="batch-saved-status" data-batch-saved="machineConfirmed"></small></span></label><label class="check-row"><input type="checkbox" id="batch-purchase" data-batch-state="procurementSent" data-batch-change><span>已将各机型料号提供给采购<small class="batch-saved-status" data-batch-saved="procurementSent"></small></span></label><label class="check-row"><input type="checkbox" id="batch-plan" data-batch-state="planSent" data-batch-change><span>已将各机型齐备料号交由计划下单<small class="batch-saved-status" data-batch-saved="planSent"></small></span></label><p class="section-note">料号可在下方逐机型填写；变更已交接料号后须重新核对，也可在本次重新勾选交接。</p></section><section class="batch-section"><h3>丝印与包材</h3><div class="form-grid">${W.MATERIALS.map(([key,label])=>`<label class="field">${label}<select data-batch-material="${key}" data-batch-state="material.${key}" data-batch-change>${options(statusChoices,'keep')}</select><small class="batch-saved-status" data-batch-saved="material.${key}"></small></label>`).join('')}<label class="field full">二厂丝印网版<select id="batch-plate" data-batch-state="plate" data-batch-change>${options({unknown:W.PLATE.unknown,keep:'保持各机型原状态',existing:W.PLATE.existing,requested:W.PLATE.requested,ready:W.PLATE.ready,na:W.PLATE.na},'keep')}</select><small class="batch-saved-status" data-batch-saved="plate"></small></label></div></section></div>${batchCodesHTML(scope)}${batchEvidenceHTML()}<label class="field">本次共同确认依据 / 备注（写入各机型跟进记录）<textarea id="batch-note" rows="2" maxlength="2000" data-batch-change placeholder="例如：业务已确认本单各机型外形与包材版本"></textarea></label><div class="batch-scope-heading"><h3>机型范围与变更预览</h3><div><button type="button" class="text-btn" data-batch-select="all">全选</button><button type="button" class="text-btn" data-batch-select="none">全不选</button></div></div><div class="batch-products">${scope.map(o=>`<label class="batch-product"><input type="checkbox" data-batch-id="${esc(o.id)}" data-batch-change checked><span><b>${esc(o.model)}</b><small>${esc(P.display(o))} · ${o.quantity} ${esc(o.unit||'台')}</small><span data-batch-result="${esc(o.id)}"></span></span></label>`).join('')}</div><div id="batch-summary" class="hint-box" role="status"></div><div id="batch-message" class="production-warnings" role="alert" hidden></div><label class="check-row production-ack"><input type="checkbox" id="batch-ack"><span>我已核对所选机型与变更，确认这些事项适用于本次选择的全部机型。</span></label><div class="dialog-actions batch-actions"><button type="button" class="secondary" data-close="batch-dialog">取消</button><button type="submit" class="primary" id="batch-save" disabled>应用并保存</button></div></form>`;
 setupBatchCodes();
 $('#batch-form').addEventListener('submit',e=>{e.preventDefault();void saveBatch();});
 $('#batch-form').addEventListener('input',e=>{if(e.target.id!=='batch-ack'){$('#batch-ack').checked=false;$('#batch-message').hidden=true;}if(e.target.dataset.batchCodeId){batchCodeChange(e.target);return;}if(e.target.dataset.batchState)trackBatchStateChange(e.target);refreshBatch();});
 $('#batch-form').addEventListener('change',e=>{if(e.target.dataset.batchState)trackBatchStateChange(e.target);if(e.target.id==='batch-machine'&&e.target.checked){$('#batch-appearance').checked=true;batchTouched.add('appearance');}if(e.target.id==='batch-appearance'&&!e.target.checked&&batchTouched.has('machineConfirmed')){$('#batch-machine').checked=false;batchTouched.delete('machineConfirmed');}if(e.target.id!=='batch-ack')$('#batch-ack').checked=false;refreshBatch();});
 $('#batch-form').addEventListener('click',e=>{const fileAction=e.target.closest('[data-batch-file]');if(fileAction){const action=fileAction.dataset.batchFile;if(action.startsWith('saved-')){void batchSavedEvidenceAction(action,Number(fileAction.dataset.index));return;}if(action==='save'){void saveBatchEvidence();return;}if(action==='pick')pickEvidence($('#batch-evidence-stage').value,'',false,batchEvidenceContext());if(action==='add')addBatchTypedPath();if(action==='remove'){batchEvidence.splice(Number(fileAction.dataset.index),1);batchEvidenceChanged();}return;}const preset=e.target.closest('[data-batch-preset]');if(preset){const mode=preset.dataset.batchPreset;batchTouched.clear();renderBatchSavedState();if(mode!=='clear'){for(const id of ['batch-appearance','batch-machine']){const el=$('#'+id);if(!el.checked){el.checked=true;batchTouched.add(el.dataset.batchState);}}}if(mode==='requirements')for(const el of $$('[data-batch-material]')){if(el.value!=='confirmed'){el.value='confirmed';batchTouched.add(el.dataset.batchState);}}$('#batch-ack').checked=false;refreshBatch();}const selection=e.target.closest('[data-batch-select]');if(selection){$$('[data-batch-id]').forEach(el=>el.checked=selection.dataset.batchSelect==='all');$('#batch-ack').checked=false;refreshBatch();}});
 $('#batch-dialog').showModal();refreshBatch();void refreshBatchSavedEvidenceStatus();
}
function refreshBatch(){
 renderBatchSavedState();refreshBatchCodes();renderBatchSavedEvidence();
 refreshBatchEvidenceSave();
 const result=batchPreview();if(!result)return;
 if($('#batch-evidence-count'))$('#batch-evidence-count').textContent=`一次拖入，同一原路径将关联到当前勾选的 ${batchRequest().ids.length} 个机型。可添加多份文件，并分别选择流程。`;
 const idle=!batchHasActions(batchRequest())&&batchRequest().ids.length>0;
 const map=new Map(result.rows.map(row=>[row.id,row])),selected=new Set(batchRequest().ids);
 for(const field of $$('[data-batch-result]')){const row=map.get(field.dataset.batchResult);field.innerHTML=!row?'<span class="muted">'+(selected.has(field.dataset.batchResult)?'已显示保存状态，本次尚无新增事项':'未选择')+'</span>':`<span class="batch-changes">${row.changes.length?row.changes.map(esc).join('；'):'所选事项与原状态一致，无需重复保存'}</span>${row.warnings.map(w=>'<span class="batch-row-warning">'+esc(w)+'</span>').join('')}${row.problems.map(w=>'<span class="batch-row-error">'+esc(w)+'</span>').join('')}`;}
 $('#batch-summary').innerHTML=idle?'已显示所选 '+batchRequest().ids.length+' 个机型的保存状态，继续选择未完成的事项即可。':result.issues.length?'<b>请先处理：</b><ul>'+result.issues.map(issue=>'<li>'+esc(issue)+'</li>').join('')+'</ul>':`已选 ${result.total} 个机型，${result.changedCount} 个将更新，${result.total-result.changedCount} 个无需修改。料号修改与确认事项见上方输入和下方预览，未改项目保留原值。`;
 if(!B.hasNonEvidenceActions(batchRequest())&&batchEvidence.length&&!result.issues.length)$('#batch-summary').innerHTML=`本次仅增加依据：已选 ${result.total} 个机型，${result.changedCount} 个将更新，无需勾选流程确认。`+' 也可点击拖入区的“仅保存依据文件”。';
 if(batchCodesPendingText()&&!B.onlyEvidence(batchRequest()))$('#batch-summary').innerHTML='长文本尚未应用，请先识别并填入所选结果，或清空识别区后保存手动填写的料号。';
 $('#batch-summary').classList.toggle('warning',!idle&&result.issues.length>0);
 const evidenceOnly=B.onlyEvidence(batchRequest());
 $('#batch-ack').closest('label').hidden=evidenceOnly;
 $('#batch-save').disabled=saving||evidenceBusy||(!evidenceOnly&&batchCodesPendingText())||!!$('#batch-evidence-path').value.trim()||result.issues.length>0||!result.changedCount||(!evidenceOnly&&!$('#batch-ack').checked);
 $('#batch-save').textContent=saving?'正在保存…':evidenceOnly?`保存依据文件 · ${result.changedCount} 个机型`:`应用并保存 ${result.changedCount} 个机型`;
 syncSaveStatus();
}
async function saveBatch(){
 if(saving||evidenceBusy)return false;
 if(B.onlyEvidence(batchRequest()))return saveBatchEvidence(true);
 if(batchCodesPendingText()){lastSaveProblem='长文本尚未应用，请先识别并填入所选结果，或清空识别区。';batchMessage(lastSaveProblem);return false;}
 if($('#batch-evidence-path').value.trim()){lastSaveProblem='请先点击“添加路径”，核对待关联文件后再勾选确认。';batchMessage(lastSaveProblem);return false;}
 const result=batchPreview();if(!result)return false;
 if(!$('#batch-ack').checked){lastSaveProblem='请先核对整单变更预览，并勾选确认。';batchMessage(lastSaveProblem);return false;}
 if(result.issues.length){lastSaveProblem=result.issues[0];batchMessage(lastSaveProblem);return false;}
 if(!result.changedCount){lastSaveProblem='本次没有需要更新的事项，请取消整单确认或调整选择。';batchMessage(lastSaveProblem);return false;}
 $('#batch-save').disabled=true;const form=$('#batch-form');form.inert=true;
 try{await persist(result.orders);$('#batch-dialog').close();render();toast(`${demo?'示例已更新':'已保存'}：${batchAlias} 的 ${result.changedCount} 个机型已批量更新`);syncSaveStatus();return true;}
 catch(e){lastSaveProblem=errMsg(e);batchMessage(lastSaveProblem);refreshBatch();return false;}
 finally{form.inert=false;}
}
$('#batch-dialog').addEventListener('cancel',e=>{if(saving||evidenceBusy)e.preventDefault();});

function batchEvidenceHTML(){return `<section class="batch-evidence evidence-zone" data-evidence-zone="batch" aria-label="整单依据文件区"><div class="evidence-heading"><h3>整单依据文件 · 拖一份即可</h3><button type="button" class="secondary small" data-batch-file="pick">选择文件（可多选）</button></div><p id="batch-evidence-count" class="section-note"></p><div class="form-grid"><label class="field">本次文件关联流程<select id="batch-evidence-stage">${options(E.STAGES,'machine')}</select></label><label class="field">本次文件说明 / 版本<input id="batch-evidence-note" maxlength="2000" placeholder="例如：整单外形确认，最终版"></label></div><p class="evidence-drop-hint">将整单依据文件拖到这里 · 仅记录原路径 · Excel 也不会导入生产单</p><div class="batch-path-entry"><input id="batch-evidence-path" maxlength="4096" data-batch-change placeholder="也可粘贴完整路径，再点击添加路径" aria-label="整单依据完整路径"><button type="button" class="secondary small" data-batch-file="add">添加路径</button></div><div class="batch-evidence-section-title"><h4>本次待保存 <span id="batch-evidence-pending-count">0</span></h4><small>保存后才会关联到所选机型</small></div><div id="batch-evidence-list"></div><div class="batch-evidence-save-row"><button type="button" class="secondary" id="batch-evidence-save" data-batch-file="save" disabled>仅保存依据文件</button><span id="batch-evidence-save-note" class="section-note">添加文件后即可单独保存，不需要确认流程。</span></div><p id="batch-evidence-pending-empty" class="section-note">本次尚未新增文件。</p><div class="batch-evidence-section-title"><h4>已保存依据 <span id="batch-evidence-saved-count">0</span></h4><button type="button" class="text-btn" data-batch-file="saved-check">重新检查路径</button></div><p class="section-note">显示本单全部未归档机型的已有依据，查看或打开不会修改订单。</p><div id="batch-evidence-saved-list"></div><p class="section-note">可点击“仅保存依据文件”单独保存，也可随整单事项一起保存；文件关联不会自动确认流程。</p></section>`;}
function batchEvidenceContext(){return {batch:true,contract:batchContract,stage:$('#batch-evidence-stage').value,note:$('#batch-evidence-note').value};}
function queueBatchEvidence(paths,context){
 if(!$('#batch-dialog').open||batchContract!==context.contract||saving)return false;
 try{
  E.merge({},paths,context.stage,context.note);
  const next=batchEvidence.slice();let duplicates=0;
  for(const p of paths){const path=E.normalizePath(p);if(next.some(x=>x.stage===context.stage&&E.key(x.path)===E.key(path))){duplicates++;continue;}next.push({path,stage:context.stage,note:context.note.trim()});}
  if(next.length>50)throw Error('整单每次最多关联 50 个依据文件');
  batchEvidence=next;batchEvidenceChanged();if(duplicates)batchMessage('已跳过 '+duplicates+' 个重复路径；核对下方预览后保存。');return true;
 }catch(e){lastSaveProblem=errMsg(e);batchMessage(lastSaveProblem);return false;}
}
function batchEvidenceChanged(){
 $('#batch-evidence-list').innerHTML=batchEvidence.map((item,index)=>`<div class="evidence-item"><div><b>${esc(E.name(item.path))}</b><span class="badge">${esc(E.STAGES[item.stage])}</span><small class="evidence-path">${esc(item.path)}</small>${item.note?'<p class="evidence-note">'+esc(item.note)+'</p>':''}</div><button type="button" class="text-btn" data-batch-file="remove" data-index="${index}">移出待关联</button></div>`).join('');
 $('#batch-ack').checked=false;$('#batch-message').hidden=true;refreshBatch();
}
function addBatchTypedPath(){if(saving||evidenceBusy)return;const input=$('#batch-evidence-path');if(queueBatchEvidence([input.value],batchEvidenceContext())){input.value='';refreshBatch();}}

function batchHasActions(request){return !!(request&&(request.appearance||request.machineConfirmed||request.procurementSent||request.planSent||request.plate||Object.keys(request.materials||{}).length||(request.evidence||[]).length||Object.values(request.codeEdits||{}).some(fields=>Object.keys(fields).length)));}
function trackBatchStateChange(el){const key=el.dataset.batchState;if(el.type==='checkbox'&&!el.checked)batchTouched.delete(key);else batchTouched.add(key);}
function renderBatchSavedState(){
 const request=batchRequest(),impact=B.codeImpact(activeOrders(),request),state=B.savedState(activeOrders(),batchContract,$$('[data-batch-id]:checked').map(el=>el.dataset.batchId));
 for(const el of $$('[data-batch-state]')){
  const key=el.dataset.batchState,touched=batchTouched.has(key),label=$('[data-batch-saved="'+key+'"]');
  if(el.type==='checkbox'){
   const item=state.checks[key],invalidate=(key==='procurementSent'&&impact.machine||key==='planSent'&&impact.any)&&item.done>0;if(!touched){el.checked=invalidate?false:item.checked;el.indeterminate=!invalidate&&item.mixed;}else el.indeterminate=false;
   el.disabled=(!touched&&item.checked&&!invalidate)||!state.total;
   el.setAttribute('aria-checked',el.indeterminate?'mixed':String(el.checked));
   label.textContent=invalidate?'料号本次有变更，请重新核对并勾选；原已确认 '+item.done+'/'+state.total+' 个机型':!state.total?'未选择机型':item.done===state.total?'已保存：全部 '+state.total+' 个机型已确认':item.done?'已保存：'+item.done+'/'+state.total+' 个机型已确认，其余待办理':'已保存：尚未确认';
  }else{
   const item=key==='plate'?state.plate:state.materials[key.slice(9)],names=key==='plate'?W.PLATE:W.STATUS;
   if(!touched)el.value=item.value;
   for(const option of el.options)option.disabled=false;
   el.disabled=!state.total;
   label.textContent=!state.total?'未选择机型':'已保存：'+Object.entries(item.counts).map(([value,count])=>(names[value]||value)+' '+count+'/'+state.total).join('；');
  }
 }
}

function batchSavedEvidenceScope(){return activeOrders().filter(o=>P.contract(o)===batchContract&&!o.archived);}
function batchSavedEvidenceGroups(){return E.groupSaved(batchSavedEvidenceScope());}
function renderBatchSavedEvidence(){
 if(!$('#batch-evidence-saved-list'))return;
 $('#batch-evidence-pending-count').textContent=batchEvidence.length;
 $('#batch-evidence-pending-empty').hidden=!!batchEvidence.length;
 const scope=batchSavedEvidenceScope(),groups=batchSavedEvidenceGroups(),selected=new Set($$('[data-batch-id]:checked').map(el=>el.dataset.batchId)),signature=JSON.stringify([batchContract,[...selected],groups]);
 if(signature!==batchSavedEvidenceSignature){
  batchSavedEvidenceSignature=signature;
  $('#batch-evidence-saved-count').textContent=groups.length;
  $('#batch-evidence-saved-list').innerHTML=groups.length?groups.map((group,index)=>{
   const linked=new Set(group.links.map(x=>x.orderId)),selectedLinked=[...linked].filter(id=>selected.has(id)).length,notes=[...new Set(group.links.map(x=>x.note).filter(Boolean))];
   return `<div class="evidence-item batch-saved-evidence" data-batch-saved-file="${index}"><div><button type="button" class="evidence-name" data-batch-file="saved-open" data-index="${index}">${esc(E.name(group.path))}</button> <span class="badge">${esc(E.STAGES[group.stage])}</span><small class="evidence-path">${esc(group.path)}</small><p class="evidence-note">已关联本单 ${linked.size}/${scope.length} 个机型 · 本次所选 ${selectedLinked}/${selected.size} 个已关联</p>${notes.length===1?'<p class="evidence-note">'+esc(notes[0])+'</p>':notes.length>1?'<p class="evidence-note">有 '+notes.length+' 种关联说明，展开查看</p>':''}<details class="batch-evidence-links"><summary>查看关联机型、说明与时间</summary>${group.links.map(link=>`<div><b>${esc(link.model)}</b><small>${esc(link.number)}${selected.has(link.orderId)?' · 本次已选':' · 本次未选'}</small>${link.note?'<p>'+esc(link.note)+'</p>':''}<small>关联时间：${esc(link.addedAt?new Date(link.addedAt).toLocaleString('zh-CN'):'未记录')}</small></div>`).join('')}</details><span class="evidence-status" data-evidence-path="${esc(group.path)}">尚未检查</span></div><div class="evidence-item-actions"><button type="button" data-batch-file="saved-open" data-index="${index}">打开文件</button><button type="button" data-batch-file="saved-reveal" data-index="${index}">定位文件夹</button></div></div>`;
  }).join(''):'<p class="section-note">本单还没有已保存依据。拖入文件并保存后，下次打开会在这里显示。</p>';
 }
 updateEvidenceStatuses();
}
async function refreshBatchSavedEvidenceStatus(force=false){
 const contract=batchContract;
 const paths=[...new Map(batchSavedEvidenceGroups().map(item=>[E.key(item.path),item.path])).values()].filter(path=>force||!evidenceStatuses.has(E.key(path)));
 try{for(let i=0;i<paths.length;i+=100){if(!$('#batch-dialog').open||batchContract!==contract)return;const response=await api('files/status',{paths:paths.slice(i,i+100)});for(const item of response.files)evidenceStatuses.set(E.key(item.path),item);if($('#batch-dialog').open&&batchContract===contract)updateEvidenceStatuses();}}
 catch(e){if(force&&$('#batch-dialog').open&&batchContract===contract)evidenceNotice(errMsg(e));}
}
async function batchSavedEvidenceAction(action,index){
 if(action==='saved-check'){await refreshBatchSavedEvidenceStatus(true);return;}
 const item=batchSavedEvidenceGroups()[index];if(!item||!['saved-open','saved-reveal'].includes(action))return;
 try{await api('files/action',{path:item.path,action:action==='saved-open'?'open':'reveal'});}
 catch(e){evidenceNotice(errMsg(e));if($('#batch-dialog').open)void refreshBatchSavedEvidenceStatus(true);}
}

function batchEvidenceRequest(){
 const request=batchRequest(),evidence=batchEvidence.map(item=>({...item})),path=$('#batch-evidence-path').value.trim();
 if(path){const stage=$('#batch-evidence-stage').value,note=$('#batch-evidence-note').value;E.merge({},[path],stage,note);const normalized=E.normalizePath(path);if(!evidence.some(item=>item.stage===stage&&E.key(item.path)===E.key(normalized)))evidence.push({path:normalized,stage,note:note.trim()});}
 return {contract:request.contract,ids:request.ids,evidence,note:request.note};
}
function refreshBatchEvidenceSave(){
 const button=$('#batch-evidence-save');if(!button)return;const request=batchRequest(),hasFiles=batchEvidence.length||!!$('#batch-evidence-path').value.trim();
 button.disabled=saving||evidenceBusy||!hasFiles||!request.ids.length;
 button.textContent=saving?'正在保存…':'仅保存依据文件';
 $('#batch-evidence-save-note').textContent=!request.ids.length?'请至少选择一个机型。':hasFiles?'仅保存到所选 '+request.ids.length+' 个机型；流程确认和料号草稿保留。':'添加文件后即可单独保存，不需要确认流程。';
}
async function saveBatchEvidence(closeWhenClean=false){
 if(saving||evidenceBusy)return false;const form=$('#batch-form');let result;
 try{const request=batchEvidenceRequest();if(!request.evidence.length){batchMessage('请先拖入依据文件、选择文件或填写完整路径。');return false;}result=B.previewEvidence(activeOrders(),request);if(result.issues.length){batchMessage(result.issues[0]);return false;}}
 catch(e){batchMessage(errMsg(e));return false;}
 form.inert=true;$('#batch-evidence-save').disabled=true;$('#batch-save').disabled=true;
 try{
  if(result.changedCount)await persist(result.orders);
  const remaining=B.hasNonEvidenceActions(batchRequest())||batchCodesPendingText();
  batchEvidence=[];$('#batch-evidence-path').value='';batchSavedEvidenceSignature='';if(!remaining)$('#batch-note').value='';batchEvidenceChanged();render();void refreshBatchSavedEvidenceStatus(true);
  if(closeWhenClean&&!pendingBatch())$('#batch-dialog').close();
  const message=result.changedCount?'依据文件已保存到 '+result.changedCount+' 个机型':'所选机型已有关联，无需重复保存';toast(message);if($('#batch-dialog').open){$('#batch-message').textContent=message+(remaining?'；其他料号或确认事项尚未保存，请继续办理。':'，可在“已保存依据”中查看。');$('#batch-message').hidden=false;}syncSaveStatus();return true;
 }catch(e){lastSaveProblem=errMsg(e);batchMessage(lastSaveProblem);refreshBatch();return false;}
 finally{form.inert=false;if($('#batch-dialog').open)refreshBatchEvidenceSave();}
}
