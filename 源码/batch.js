(function(root){
'use strict';
const W=typeof module!=='undefined'?require('./model.js'):root.W;
const P=typeof module!=='undefined'?require('./production.js'):root.P;
const E=typeof module!=='undefined'?require('./evidence.js'):root.E;
const STATUS=['confirmed','requested','received','na'];
const PLATE=['existing','requested','ready','na'];
const copy=value=>JSON.parse(JSON.stringify(value));
function preview(orders,request,at=new Date().toISOString()){
 const issues=[],ids=new Set(request.ids||[]),contract=request.contract;
 const scope=orders.filter(o=>P.contract(o)===contract&&!o.archived);
 if(!scope.length)issues.push('此订单没有可办理的未归档机型。');
 if(!ids.size)issues.push('请至少选择一个机型。');
 if([...ids].some(id=>!scope.some(o=>o.id===id)))issues.push('选择中包含其他订单或归档机型，请重新打开整单确认。');
 if((request.note||'').length>2000)issues.push('确认依据 / 备注最多 2000 字。');
 const materials=request.materials||{};
 if(Object.keys(materials).some(k=>!W.MATERIALS.some(([name])=>name===k)))issues.push('存在不支持的包材项目。');
 if(Object.values(materials).some(v=>!STATUS.includes(v)))issues.push('存在不支持的包材状态。');
 if(request.plate&&!PLATE.includes(request.plate))issues.push('存在不支持的网版状态。');
 const codeEdits=request.codeEdits||{};
 if(typeof codeEdits!=='object'||Array.isArray(codeEdits))issues.push('料号修改格式错误。');
 else for(const[id,fields]of Object.entries(codeEdits)){
  if(!ids.has(id)||!scope.some(o=>o.id===id)){issues.push('料号修改包含未选择或其他订单的机型。');continue;}
  if(!fields||typeof fields!=='object'||Array.isArray(fields)){issues.push('机型料号修改格式错误。');continue;}
  for(const[key,value]of Object.entries(fields))if(!['machine',...W.MATERIALS.map(([k])=>k)].includes(key)||typeof value!=='string'||value.trim().length>300||/[\t\r\n]/.test(value))issues.push('料号项目或内容无效，每项最多300字且不可包含多行文本。');
 }
 const evidence=request.evidence||[];
 if(!Array.isArray(evidence)||evidence.length>50)issues.push('整单每次最多关联 50 个依据文件');
 else for(const item of evidence){try{if(!item||Object.keys(item).some(k=>!['path','stage','note'].includes(k)))throw Error('依据文件只允许路径、流程与说明');E.merge({},[item.path],item.stage,item.note||'');}catch(e){issues.push(e.message);}}
 const hasAction=request.appearance||request.machineConfirmed||Object.keys(materials).length||request.plate||request.procurementSent||request.planSent||evidence.length||Object.values(codeEdits).some(x=>x&&Object.keys(x).length);
 if(!hasAction)issues.push('请选择本次要确认的事项。');
 if(issues.length)return {orders:copy(orders),rows:[],issues,total:ids.size,changedCount:0};
 const candidates=copy(orders),rows=[];
 for(const o of candidates){
  if(!ids.has(o.id))continue;
  const before=orders.find(x=>x.id===o.id),warnings=[];let machineCodeChanged=false,materialCodeChanged=false;
  for(const[key,value]of Object.entries(codeEdits[o.id]||{})){
   if(key==='machine'){if(o.machineCode!==value.trim()){o.machineCode=value.trim();machineCodeChanged=true;}}
   else if((materials[key]??o.materials[key].status)==='na')warnings.push('本次'+W.MATERIALS.find(([k])=>k===key)[1]+'为不需要，料号保持原样');
   else if(o.materials[key].code!==value.trim()){o.materials[key].code=value.trim();materialCodeChanged=true;}
  }
  if(machineCodeChanged&&before.procurementSent)o.procurementSent=false;
  if((machineCodeChanged||materialCodeChanged)&&before.planSent)o.planSent=false;
  if(request.appearance||request.machineConfirmed)o.appearance=true;
  if(request.machineConfirmed)o.machineConfirmed=true;
  if((before.appearance!==o.appearance||before.machineConfirmed!==o.machineConfirmed)&&before.procurementSent)o.procurementSent=false;
  let materialChanged=false;
  for(const [key,value] of Object.entries(materials)){if(o.materials[key].status!==value)materialChanged=true;o.materials[key].status=value;}
  if(o.materials.silk.status!=='confirmed'&&o.materials.silk.status!=='na'&&['existing','requested','ready'].includes(o.plate))o.plate='unknown';
  if(request.plate){if(o.materials.silk.status==='na')warnings.push('无需丝印，网版记录保持原样');else o.plate=request.plate;}
  if(before.planSent&&(materialChanged||before.plate!==o.plate))o.planSent=false;
  if(!W.procurementEligible(o))o.procurementSent=false;
  if(request.procurementSent){o.procurementSent=true;if(!o.procurementDate)o.procurementDate=W.today();}
  if(W.planStructuralIssues(o).length)o.planSent=false;
  if(request.planSent){o.planSent=true;if(!o.planDate)o.planDate=W.today();}
  const changes=[];
  if(before.appearance!==o.appearance)changes.push('机器外形：已确认');
  if(before.machineConfirmed!==o.machineConfirmed)changes.push('机器状态：已确认');
  if(before.machineCode!==o.machineCode)changes.push('机器料号：'+(before.machineCode||'未填写')+' → '+(o.machineCode||'未填写'));
  for(const[key,label]of W.MATERIALS)if(before.materials[key].code!==o.materials[key].code)changes.push(label+'料号：'+(before.materials[key].code||'未填写')+' → '+(o.materials[key].code||'未填写'));
  for(const [key,label] of W.MATERIALS)if(before.materials[key].status!==o.materials[key].status)changes.push(label+'：'+W.STATUS[before.materials[key].status]+' → '+W.STATUS[o.materials[key].status]);
  if(before.plate!==o.plate)changes.push('网版：'+W.PLATE[before.plate]+' → '+W.PLATE[o.plate]);
  if(before.procurementSent!==o.procurementSent)changes.push('采购交接：'+(o.procurementSent?'已交采购':'需重新核对'));
  if(before.planSent!==o.planSent)changes.push('计划交接：'+(o.planSent?'已交计划':'需重新核对'));
  if(before.planSent&&!o.planSent)warnings.push('要求或网版已变化，原计划交接需重新核对');
  if(before.procurementSent&&!o.procurementSent)warnings.push('机器资料已变化，原采购交接需重新核对');
  const fileProblems=[];
  try{for(const item of evidence)Object.assign(o,E.merge(o,[item.path],item.stage,item.note||'','',at).order);}catch(e){fileProblems.push(e.message);}
  changes.push(...E.changes(before,o));
  const problems=[...fileProblems,...W.validate(o)];
  for(const problem of problems)issues.push(P.display(o)+' / '+o.model+'：'+problem);
  if(changes.length){o.updatedAt=at;o.history.push({at,text:'整单批量确认（'+P.alias(o)+'，本次选择 '+ids.size+' 个机型）：'+changes.join('；')+(request.note?.trim()?'；确认依据 / 备注：'+request.note.trim():'')});}
  rows.push({id:o.id,label:P.display(o),model:o.model,quantity:o.quantity,changes,warnings,problems,changed:changes.length>0});
 }
 return {orders:candidates,rows,issues,total:rows.length,changedCount:rows.filter(r=>r.changed).length};
}
function codeImpact(orders,request){const selected=new Set(request.ids||[]),result={machine:false,material:false,any:false};for(const o of orders){if(!selected.has(o.id)||P.contract(o)!==request.contract||o.archived)continue;for(const[k,value]of Object.entries(request.codeEdits?.[o.id]||{})){if(typeof value!=='string')continue;if(k==='machine'&&value.trim()!==o.machineCode)result.machine=true;else if(W.MATERIALS.some(([key])=>key===k)&&(request.materials?.[k]??o.materials[k].status)!=='na'&&value.trim()!==o.materials[k].code)result.material=true;}}result.any=result.machine||result.material;return result;}
function savedState(orders,contract,ids){
 const selected=new Set(ids),scope=orders.filter(o=>!o.archived&&P.contract(o)===contract&&selected.has(o.id)),total=scope.length;
 const checks=Object.fromEntries(['appearance','machineConfirmed','procurementSent','planSent'].map(key=>{const done=scope.filter(o=>o[key]).length;return [key,{done,total,checked:total>0&&done===total,mixed:done>0&&done<total}];}));
 const field=get=>{const counts={};for(const o of scope){const value=get(o);counts[value]=(counts[value]||0)+1;}const values=Object.keys(counts);return {value:values.length===1?values[0]:'keep',mixed:values.length>1,counts};};
 return {total,checks,materials:Object.fromEntries(W.MATERIALS.map(([key])=>[key,field(o=>o.materials[key].status)])),plate:field(o=>o.plate)};
}
const api={preview,savedState,codeImpact};if(typeof module!=='undefined')module.exports=api;root.B=api;
})(typeof window!=='undefined'?window:globalThis);
