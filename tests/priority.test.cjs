const {test}=require('node:test');
const assert=require('node:assert/strict');
const Priority=require('../源码/priority.js');
const now=Date.parse('2026-11-14T10:00:00+08:00');
function order(number, hours, machineCode='', extra={}){
 return {id:number,number,contractNumber:number,createdAt:new Date(now-hours*3600000).toISOString(),machineCode,...extra};
}
test('48小时前后按精确时间判定',()=>{
 const item=order('TEST',48);
 assert.equal(Priority.overdue([item],now-1).length,0);
 assert.equal(Priority.overdue([item],now).length,1);
});
test('同单多个缺料号机型只显示一个订单，部分补齐仍列入',()=>{
 const items=[order('TEST',50),order('TEST',50,'M',{id:'two'})];
 assert.equal(Priority.overdue(items,now).length,1);
 items[0].machineCode='M2';
 assert.equal(Priority.overdue(items,now).length,0);
});
test('整单从首次加入计时，补入机型不会推迟截止',()=>{
 const items=[order('TEST',60,'M1'),order('TEST',2,'',{id:'two'})];
 assert.equal(Priority.overdue(items,now).length,1);
 assert.equal(Priority.overdue(items,now)[0].deadline,now-12*3600000);
});
test('缺料号风险确认与手动加急不代替机器料号',()=>{
 const risk=order('TEST',60,' \n ',{planSent:true,priority:'normal',riskAcceptances:{machine:{accountId:'TEST'}}});
 assert.equal(Priority.overdue([risk],now).length,1);
 assert.equal(Priority.overdue([order('NEW',1,'',{priority:'urgent'})],now).length,0);
});
test('归档订单排除，缺失时间可回退到原导入时间',()=>{
 const items=[order('ARCHIVE',100,'',{archived:true}),order('IMPORTED',0,'',{createdAt:'',productionSource:{importedAt:new Date(now-50*3600000).toISOString()}})];
 assert.deepEqual(Priority.overdue(items,now).map(x=>x.order.number),['IMPORTED']);
});
test('按最早超时排序，计算不会改写数据',()=>{
 const items=[order('LATER',49),order('EARLIER',70)];
 const before=JSON.stringify(items);
 assert.deepEqual(Priority.overdue(items,now).map(x=>x.order.number),['EARLIER','LATER']);
 assert.equal(JSON.stringify(items),before);
});
