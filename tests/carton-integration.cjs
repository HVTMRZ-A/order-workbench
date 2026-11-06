// Exercise the shipped executable with isolated, fictional orders.
const fs=require('node:fs');
const path=require('node:path');
const assert=require('node:assert/strict');
const {spawn}=require('node:child_process');
const {chromium}=require('playwright');
const Auth=require('./test-auth.cjs');
const W=require('../源码/model.js');

const root=path.resolve(__dirname,'..');
const dataDir=path.join(root,'test-results','carton-'+Date.now());
fs.mkdirSync(dataDir,{recursive:true});
function order(id,quantity){
 const item=W.newOrder({
  id,number:'TEST-'+id,contractNumber:'TEST',customer:'虚构客户',model:'DEMO-'+id,quantity,
  appearance:true,machineConfirmed:true,machineCode:'M',procurementSent:true,plate:'existing'
 });
 for(const [key] of W.MATERIALS)Object.assign(item.materials[key],{status:'confirmed',code:key});
 return item;
}
const legacy=order('legacy',3);
legacy.contractNumber='OLD';
legacy.planSent=true;
delete legacy.cartonType;
delete legacy.cartonSizeConfirmed;
const seed={schema:1,revision:0,orders:[order('a',5),order('b',500),legacy]};
const ordersPath=path.join(dataDir,'orders.json');
fs.writeFileSync(ordersPath,JSON.stringify(seed));
const child=spawn(path.join(root,'订单工作台.exe'),['--no-browser','--data-dir',dataDir],{windowsHide:true,stdio:'ignore'});
const wait=ms=>new Promise(resolve=>setTimeout(resolve,ms));
let instance,browser;
async function call(route,body){
 const response=await fetch(instance.url+'api/'+route,{
  method:body===undefined?'GET':'POST',
  headers:{'Content-Type':'application/json','X-Workbench-Token':new URL(instance.url).pathname.split('/')[1]},
  body:body===undefined?undefined:JSON.stringify(body)
 });
 return {status:response.status,data:await response.json()};
}
const getOrder=async id=>(await call('data')).data.orders.find(item=>item.id===id);
(async()=>{
 for(let i=0;i<100&&!instance;i++){
  try{const current=JSON.parse(fs.readFileSync(path.join(dataDir,'instance.json'),'utf8'));if(current.pid===child.pid)instance=current;}catch{}
  if(!instance)await wait(100);
 }
 assert.ok(instance,'程序未启动');
 await Auth.login(instance);
 assert.equal(fs.readFileSync(ordersPath,'utf8'),JSON.stringify(seed));
 const invalid=structuredClone(seed);
 invalid.orders[0].planSent=true;
 assert.equal((await call('data',invalid)).status,400);
 delete invalid.orders[0].cartonType;
 delete invalid.orders[0].cartonSizeConfirmed;
 assert.equal((await call('data',invalid)).status,400);
 assert.equal(fs.readFileSync(ordersPath,'utf8'),JSON.stringify(seed));
 console.log('后端拦截缺失确认的新交接，旧已交计划的数据原样读取');

 browser=await chromium.launch({headless:true,executablePath:process.env.WORKBENCH_EDGE_PATH||
  path.join(process.env['ProgramFiles(x86)']||'C:/Program Files (x86)','Microsoft/Edge/Application/msedge.exe')});
 const context=await browser.newContext({viewport:{width:1440,height:1000}});
 await Auth.attach(context,instance);
 const page=await context.newPage();
 const errors=[];
 page.on('pageerror',error=>errors.push(error.message));
 await page.goto(instance.url);
 await page.waitForFunction(()=>dataReady);
 const boxType=page.locator('[data-field=cartonType]');
 const boxSize=page.locator('[data-field=cartonSizeConfirmed]');
 const plan=page.locator('[data-field=planSent]');
 const save=async()=>{
  await page.locator('[data-action=save-order]').click();
  await page.waitForFunction(()=>!saving&&!dirty());
 };
 await page.evaluate(()=>openOrder('a'));
 assert.match(await page.locator('#plan-gate').innerText(),/箱型尚未与业务确认.*尺寸尚未核对/s);
 assert.equal(await plan.isDisabled(),true);
 await boxSize.check();
 assert.equal(await plan.isDisabled(),true);
 await boxType.selectOption('kraft');
 assert.equal(await plan.isEnabled(),true);
 await save();
 assert.equal((await getOrder('a')).cartonSizeConfirmed,true);
 assert.equal((await getOrder('a')).cartonType,'kraft');
 assert.equal((await getOrder('a')).planSent,false);
 await page.evaluate(()=>closeDrawer());
 await page.evaluate(()=>openBatch('b'));
 await page.locator('[data-batch-id=a]').uncheck();
 await page.locator('[data-batch-preset=requirements]').click();
 assert.equal(await page.locator('[data-batch-state=cartonType]').inputValue(),'keep');
 assert.equal(await page.locator('[data-batch-state=cartonSizeConfirmed]').inputValue(),'keep');
 await page.locator('#batch-plan').check();
 assert.match(await page.locator('#batch-summary').innerText(),/箱型尚未与业务确认/);
 assert.equal(await page.locator('#batch-save').isDisabled(),true);
 await page.locator('[data-batch-state=cartonType]').selectOption('color');
 await page.locator('[data-batch-state=cartonSizeConfirmed]').selectOption('confirmed');
 await page.locator('#batch-ack').check();
 await page.locator('#batch-save').click();
 await page.waitForFunction(()=>!document.querySelector('#batch-dialog').open);
 const saved=await getOrder('b');
 assert.equal(saved.cartonType,'color');
 assert.equal(saved.cartonSizeConfirmed,true);
 assert.equal(saved.planSent,true);
 assert.equal((await getOrder('a')).cartonType,'kraft');
 assert.ok(saved.audit.at(-1).changes.some(change=>change.field==='cartonSizeConfirmed'));
 assert.ok(saved.audit.at(-1).changes.some(change=>change.field==='cartonType'));
 console.log('单机型独立保存、整单按机型选择、交计划校验和账户留痕通过');

 await page.evaluate(()=>openOrder('legacy'));
 assert.equal(await boxSize.isChecked(),false);
 assert.equal(await plan.isChecked(),true);
 await boxSize.check();
 await save();
 assert.equal((await getOrder('legacy')).planSent,true);
 assert.equal((await getOrder('legacy')).cartonType,undefined);
 assert.equal((await getOrder('legacy')).cartonSizeConfirmed,true);
 await page.evaluate(()=>closeDrawer());
 await page.evaluate(()=>openOrder('b'));
 await boxSize.uncheck();
 assert.equal(await plan.isChecked(),false);
 assert.equal(await plan.isDisabled(),true);
 await save();
 assert.equal((await getOrder('b')).planSent,false);
 assert.deepEqual(errors,[]);
 console.log('旧已交计划可补确认，撤销尺寸后须重新核对计划交接');
})().catch(error=>{console.error(error);process.exitCode=1;}).finally(async()=>{
 await browser?.close();
 if(instance)await call('quit',{}).catch(()=>{});
 for(let i=0;i<25&&child.exitCode===null;i++)await wait(100);
 if(child.exitCode===null)child.kill();
});
