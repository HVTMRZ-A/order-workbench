(function(root){
'use strict';
const W=typeof module!=='undefined'?require('./model.js'):root.W;
function shortName(number){const clean=String(number||'').trim();const match=clean.match(/^\d*[A-Za-z]+(\d[A-Za-z0-9]*)$/);return match?match[1]:clean;}
function contract(o){return o.contractNumber||o.number;}
function alias(o){return o.alias||shortName(contract(o));}
function display(o){return alias(o)+(o.sourceSerial?' · '+o.sourceSerial:'');}
function groups(orders){const map=new Map();for(const o of orders){const key=contract(o);if(!map.has(key))map.set(key,[]);map.get(key).push(o);}return [...map.values()];}
function summary(orders){
 const total=orders.length,done=orders.filter(W.complete).length,finished=total>0&&done===total;
 const conditions=orders.flatMap(W.checks),passed=conditions.filter(Boolean).length;
 const progress=finished?100:Math.min(99,conditions.length?Math.round(passed/conditions.length*100):0);
 const rank={machine:0,purchase:1,material:2,plan:3,done:4};
 const remaining=orders.filter(o=>!W.complete(o)).sort((a,b)=>rank[W.stage(a).key]-rank[W.stage(b).key]);
 const next=remaining[0],stage=finished?{key:'done',label:'整单已完成',tone:'green'}:next?W.stage(next):{key:'machine',label:'待登记产品',tone:'blue'};
 const waiting=remaining.filter(W.waiting).length,ready=remaining.filter(o=>!W.planIssues(o).length).length;
 const taskCounts=new Map();for(const o of remaining)for(const task of W.todos(o)){if(task.who==='业务'||task.who==='二厂')taskCounts.set(task.who,(taskCounts.get(task.who)||0)+1);}
 const todo=finished?'所有产品已完成交接':next?(W.todos(next)[0]?.text||stage.label):'暂无产品';
 const waitText=[...taskCounts].map(([who,n])=>'待'+who+' '+n+' 项').join(' · ');
 const trackRows=orders.map(W.tracks),trackCounts=[0,1,2,3,4].map(i=>trackRows.filter(t=>t[i]===2).length);
 const tracks=trackCounts.map((n,i)=>total&&n===total?2:trackRows.some(t=>t[i]>0)?1:0);
 const dates=(finished?orders:remaining).map(o=>o.due).filter(Boolean).sort();
 const quantities=new Map();for(const o of orders){const unit=o.unit||'台';quantities.set(unit,(quantities.get(unit)||0)+Number(o.quantity));}
 return {total,done,finished,progress,stage,waiting,ready,todo,waitText,trackCounts,tracks,due:dates[0]||'',quantityText:[...quantities].map(([unit,n])=>n+' '+unit).join(' · '),urgent:remaining.some(o=>o.priority==='urgent')};
}
function normalize(o){return Object.assign({alias:shortName(o.contractNumber||o.number),contractNumber:'',customerModel:'',customerCode:'',sourceSerial:'',productRequirements:'',packagingRequirements:'',category:'',unit:'台',sourceOperator:'',orderDate:'',orderProperty:'',transport:'',productionSource:null},o);}
function toOrders(preview,sheet,edited,existing=[]){const fields=sheet.fields,get=k=>edited[k]??fields[k]?.value??'',incomingContract=get('contractNumber').trim(),sameContract=existing.find(o=>importText(contract(o))===importText(incomingContract)),contractNumber=sameContract?contract(sameContract):incomingContract,aliasValue=sameContract?alias(sameContract):get('alias').trim()||shortName(contractNumber),customer=get('customer').trim()||get('customerCode').trim();const allItems=edited.items||sheet.items,indices=edited.selectedIndices===undefined?allItems.map((_,i)=>i):edited.selectedIndices;if(!Array.isArray(indices)||indices.some(i=>!Number.isInteger(i)||i<0||i>=allItems.length)||new Set(indices).size!==indices.length)throw Error('导入行选择无效，请重新选择');const selected=new Set(indices),items=allItems.map((item,index)=>({item,index})).filter(x=>selected.has(x.index));const usedNumbers=new Set(existing.map(o=>importText(o.number)));const now=new Date().toISOString();return items.map(({item,index:i})=>{const serial=item.serial||String(i+1);let number=sheet.items.length===1?contractNumber:contractNumber+'-'+String(i+1).padStart(2,'0');if(usedNumbers.has(importText(number))){let n=1;while(usedNumbers.has(importText(contractNumber+'-'+String(n).padStart(2,'0'))))n++;number=contractNumber+'-'+String(n).padStart(2,'0');}usedNumbers.add(importText(number));const source={filename:preview.filename,sha256:preview.sha256,sheet:sheet.sheet,row:item.row,cells:item.cells,headerQuantity:fields.headerQuantity.value,detailTotal:sheet.detailTotal,notes:fields.notes.value,warnings:[...sheet.warnings],originalItem:sheet.items[i],importedAt:now};
const o=normalize(W.newOrder({number,alias:aliasValue,contractNumber,customer,customerCode:get('customerCode'),customerModel:item.customerModel||'',model:item.model,quantity:Number(item.quantity),sourceSerial:serial,category:item.category||'',unit:item.unit||'台',due:get('due'),sourceOperator:get('sourceOperator'),orderDate:get('orderDate'),orderProperty:get('orderProperty'),transport:get('transport'),productRequirements:item.productRequirements||'',packagingRequirements:item.packagingRequirements||'',notes:get('notes'),machineCode:item.machineCode||'',machineReference:preview.filename+'｜'+sheet.sheet+'!'+(item.cells?.model||''),productionSource:source}));
if(/丝印/i.test(item.productRequirements))o.materials.silk.note=item.productRequirements;
if(/箱|包装/.test(item.productRequirements))o.materials.carton.note=item.productRequirements;
for(const[k]of W.MATERIALS)o.materials[k].reference=preview.filename+'｜'+sheet.sheet+' 第'+item.row+'行（要求待核对）';
o.history.push({at:now,text:'从生产单导入：'+preview.filename+'，'+sheet.sheet+' 第'+item.row+'行；仅登记资料，所有确认与交接均待办理。'});return o;});}
function importText(value){return String(value??'').normalize('NFKC').trim().replace(/\s+/g,' ').toUpperCase();}
function importProduct(o){const raw=o.productionSource?.originalItem;return {model:importText(raw?.model||o.model),customerModel:importText(raw?.customerModel||o.customerModel),code:importText(raw?.machineCode||o.machineCode),serial:importText(raw?.serial||o.sourceSerial)};}
function sameImportProduct(a,b){return !!a.model&&a.model===b.model&&(!a.customerModel||!b.customerModel||a.customerModel===b.customerModel)&&(!a.code||!b.code||a.code===b.code);}
function importState(preview,sheet,edited,existing){
 const all=toOrders(preview,sheet,{...edited,selectedIndices:sheet.items.map((_,i)=>i)}),key=importText(all[0]?.contractNumber),same=existing.filter(o=>importText(contract(o))===key),used=new Set(),rows=all.map((o,i)=>({index:i,order:o,duplicate:null,reason:''}));
 function take(row,test,reason){const index=same.findIndex((o,i)=>!used.has(i)&&test(o));if(index<0)return;used.add(index);row.duplicate=same[index];row.reason=reason+(same[index].archived?'（已归档）':'');}
 // First reserve exact source rows, then stable serial/model matches, before model fallback.
 for(const r of rows)take(r,o=>!!preview.sha256&&o.productionSource?.sha256===preview.sha256&&o.productionSource?.sheet===sheet.sheet&&Number(o.productionSource?.row)===Number(sheet.items[r.index].row),'同一生产单行已导入');
 for(const r of rows)if(!r.duplicate){const item=importProduct(r.order);take(r,o=>!!item.serial&&item.serial===importProduct(o).serial&&sameImportProduct(item,importProduct(o)),'同一序号和机型已导入');}
 for(const r of rows)if(!r.duplicate){const item=importProduct(r.order);take(r,o=>sameImportProduct(item,importProduct(o)),'同合同机型已导入');}
 const count=rows.filter(r=>r.duplicate).length;return {rows,existingCount:same.length,duplicateCount:count,remainingCount:rows.length-count,alias:same[0]?alias(same[0]):''};
}
function errors(orders,existing){const messages=[];if(!orders.length)messages.push('请至少选择一行需要导入的产品');const seen=new Set();for(const o of orders){messages.push(...W.validate(o).map(e=>o.number+'：'+e));if(!o.contractNumber.trim())messages.push('请填写完整合同编号');if(seen.has(importText(o.number)))messages.push('导入明细订单号重复');seen.add(importText(o.number));if(existing.some(x=>importText(x.number)===importText(o.number)))messages.push('已存在相同订单号：'+o.number+'，本次不会覆盖原流程。');}return messages;}
const api={importState,importText,shortName,contract,alias,display,groups,summary,normalize,toOrders,errors};if(typeof module!=='undefined')module.exports=api;root.P=api;
})(typeof window!=='undefined'?window:globalThis);
