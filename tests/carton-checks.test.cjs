const {test}=require('node:test');
const assert=require('node:assert/strict');
const W=require('../源码/model.js');
const B=require('../源码/batch.js');

function readyOrder(id='a',quantity=5){
 const order=W.newOrder({
  id,number:'TEST-'+id,contractNumber:'TEST',customer:'虚构客户',model:'DEMO-'+id,quantity,
  appearance:true,machineConfirmed:true,machineCode:'M',procurementSent:true,plate:'existing'
 });
 for(const [key] of W.MATERIALS)Object.assign(order.materials[key],{status:'confirmed',code:key});
 return order;
}

test('纸箱图案已确认仍须单独核对箱型和尺寸',()=>{
 const order=readyOrder();
 assert.equal(W.stage(order).key,'material');
 assert.equal(W.tracks(order)[3],0);
 assert.deepEqual(W.planStructuralIssues(order),['纸箱箱型尚未与业务确认','纸箱尺寸尚未核对']);
 order.cartonType='kraft';
 assert.deepEqual(W.planStructuralIssues(order),['纸箱尺寸尚未核对']);
 order.cartonSizeConfirmed=true;
 assert.deepEqual(W.planStructuralIssues(order),[]);
 assert.equal(W.tracks(order)[3],2);
});

test('业务确认文本带数量和两种箱型，计划交接带已核对内容',()=>{
 const order=readyOrder('a',5);
 assert.match(W.handoff(order,'sales'),/5 台.*牛皮纸箱或彩箱/);
 order.cartonType='color';
 order.cartonSizeConfirmed=true;
 assert.match(W.handoff(order,'plan'),/纸箱箱型：彩箱\n纸箱尺寸：已核对/);
});

test('批量确认只应用到所选且需要纸箱的机型',()=>{
 const a=readyOrder(),b=readyOrder('b',500),c=readyOrder('c');
 c.materials.carton.status='na';
 const result=B.preview([a,b,c],{
  contract:'TEST',ids:['b','c'],cartonType:'color',cartonSizeConfirmed:true,planSent:true
 });
 assert.deepEqual(result.issues,[]);
 assert.deepEqual(result.orders[0],a);
 assert.equal(result.orders[1].cartonType,'color');
 assert.equal(result.orders[1].cartonSizeConfirmed,true);
 assert.equal(result.orders[1].planSent,true);
 assert.equal(result.orders[2].cartonType,'');
 assert.equal(result.orders[2].planSent,true);
});

test('旧已交计划保留历史状态，未交计划的旧订单仍须补核对',()=>{
 const order=readyOrder();
 delete order.cartonType;
 delete order.cartonSizeConfirmed;
 const request={contract:'TEST',ids:['a'],planSent:true};
 assert.equal(B.preview([order],request).issues.length,2);
 order.planSent=true;
 assert.equal(W.complete(order),true);
 assert.equal(W.cartonSizeLabel(order),'历史交接未记录');
 const supplemented=B.preview([order],{...request,planSent:false,cartonSizeConfirmed:true});
 assert.deepEqual(supplemented.issues,[]);
 assert.equal(supplemented.orders[0].planSent,true);
 assert.equal(supplemented.orders[0].cartonType,undefined);
 const revoked=B.preview(supplemented.orders,{...request,planSent:false,cartonSizeConfirmed:false});
 assert.equal(revoked.orders[0].planSent,false);
});
