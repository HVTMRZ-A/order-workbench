const fs=require('node:fs');
const path=require('node:path');
const assert=require('node:assert/strict');
const {spawn}=require('node:child_process');
const {chromium}=require('playwright');
const W=require('../源码/model.js');
const Auth=require('./test-auth.cjs');
const root=process.env.WORKBENCH_TEST_ROOT||path.resolve(__dirname,'..');
const results=path.join(root,'test-results','priority-'+Date.now());
const now=Date.now();
fs.mkdirSync(results,{recursive:true});
function item(id,contract,hours,code=''){
 return W.newOrder({id,number:contract+'-'+id,alias:contract,contractNumber:contract,customer:'虚构客户',model:'DEMO-'+id,quantity:1,createdAt:new Date(now-hours*3600000).toISOString(),machineCode:code});
}
const orders=[item('a','TEST-OLD',55),item('b','TEST-OLD',55),item('c','TEST-NEXT',47),item('d','TEST-FILLED',55,'M1'),{...item('e','TEST-ARCHIVE',70),archived:true}];
const seed=JSON.stringify({schema:1,revision:0,orders});
const file=path.join(results,'orders.json');
fs.writeFileSync(file,seed);
let processHandle,instance,browser;
const wait=ms=>new Promise(resolve=>setTimeout(resolve,ms));
async function launch(){
 processHandle=spawn(path.join(root,'订单工作台.exe'),['--no-browser','--data-dir',results],{windowsHide:true,stdio:'ignore'});
 for(let i=0;i<100;i++){
  try{const current=JSON.parse(fs.readFileSync(path.join(results,'instance.json'),'utf8'));if(current.pid===processHandle.pid){instance=current;return;}}catch{}
  await wait(100);
 }
 throw Error('启动失败');
}
async function call(route,body){
 const response=await fetch(instance.url+'api/'+route,{method:body===undefined?'GET':'POST',headers:{'Content-Type':'application/json','X-Workbench-Token':new URL(instance.url).pathname.split('/')[1]},body:body===undefined?undefined:JSON.stringify(body)});
 assert.equal(response.status,200);
 return response.json();
}
async function stop(){
 if(!processHandle)return;
 await call('quit',{}).catch(()=>{});
 for(let i=0;i<25&&processHandle.exitCode===null;i++)await wait(100);
 if(processHandle.exitCode===null)processHandle.kill();
 processHandle=null;
}
(async()=>{
 await launch();
 await Auth.login(instance);
 browser=await chromium.launch({headless:true,executablePath:process.env.WORKBENCH_EDGE_PATH||path.join(process.env['ProgramFiles(x86)']||'C:/Program Files (x86)','Microsoft/Edge/Application/msedge.exe')});
 const context=await browser.newContext({viewport:{width:1440,height:1000}});
 await Auth.attach(context,instance);
 let page=await context.newPage();
 const errors=[];
 page.on('pageerror',e=>errors.push(e.message));
 await page.clock.install({time:now});
 await page.goto(instance.url);
 await page.waitForFunction(()=>dataReady);
 assert.equal(fs.readFileSync(file,'utf8'),seed);
 assert.equal(await page.locator('#nav-priority').innerText(),'1');
 assert.match(await page.locator('#attention').innerText(),/1 笔订单超时/);
 await page.locator('#attention-action').click();
 assert.equal(await page.locator('#page-title').innerText(),'优先待办');
 assert.equal(await page.locator('#priority-list tbody tr').count(),1);
 assert.equal(await page.locator('#priority-list').innerText().then(t=>t.includes('DEMO-')),false);
 assert.equal(await page.locator('#order-list').isVisible(),false);
 await page.evaluate(()=>openOrder('c'));
 await page.locator('[data-tab=info]').click();
 await page.locator('[data-field=notes]').fill('未保存的测试草稿');
 await page.clock.fastForward(3600000);
 assert.equal(await page.locator('#nav-priority').innerText(),'2');
 assert.equal(await page.locator('#priority-list tbody tr').count(),2);
 assert.equal(await page.locator('[data-field=notes]').inputValue(),'未保存的测试草稿');
 assert.equal(await page.evaluate(()=>dirty()),true);
 page.once('dialog',d=>d.accept());
 await page.locator('[data-action=close-drawer]').first().click();
 assert.equal(fs.readFileSync(file,'utf8'),seed);
 console.log('整单去重、48小时到期自动加入、首页入口与草稿保持通过');

 async function saveCode(id,code){
  await page.evaluate(id=>openOrder(id),id);
  await page.locator('[data-field=machineCode]').fill(code);
  await page.locator('[data-action=save-order]').click();
  await page.waitForFunction(()=>!saving&&!dirty());
  await page.evaluate(()=>closeDrawer());
 }
 await saveCode('a','M-A');
 assert.equal(await page.locator('#priority-list tbody tr').count(),2);
 await page.locator('#priority-list [data-batch-order=b],#priority-list [data-batch-order=a]').click();
 await page.locator('[data-batch-code-id=b][data-batch-code-type=machine]').fill('M-B');
 await page.locator('#batch-ack').check();
 await page.locator('#batch-save').click();
 await page.waitForFunction(()=>!document.querySelector('#batch-dialog').open);
 assert.equal(await page.locator('#priority-list tbody tr').count(),1);
 assert.equal(await page.locator('#nav-priority').innerText(),'1');
 assert.match(await page.locator('#priority-list').innerText(),/TEST-NEXT/);
 console.log('单机型部分补齐保留，整单补齐后自动移出通过');
 await saveCode('c','M-C');
 assert.equal(await page.locator('#nav-priority').innerText(),'0');
 assert.match(await page.locator('#priority-list').innerText(),/暂无超时/);
 await saveCode('a','');
 assert.equal(await page.locator('#nav-priority').innerText(),'1');
 assert.match(await page.locator('#priority-list').innerText(),/TEST-OLD/);
 const beforeRestart=await call('data');
 await page.close();
 await stop();
 await launch();
 await Auth.login(instance);
 assert.deepEqual(await call('data'),beforeRestart);
 await Auth.attach(context,instance);
 page=await context.newPage();
 page.on('pageerror',e=>errors.push(e.message));
 await page.goto(instance.url);
 await page.waitForFunction(()=>dataReady);
 await page.locator('[data-view=priority]').click();
 assert.equal(await page.locator('#nav-priority').innerText(),'1');
 assert.equal(await page.locator('#priority-list tbody tr').count(),1);
 await page.waitForFunction(()=>settingsReady);
 await page.screenshot({path:path.join(results,'优先待办.png'),fullPage:true,animations:'disabled'});
 assert.deepEqual(errors,[]);
 console.log('补齐移出、清空重新列入、重启恢复和保存数据保持通过');
 console.log('测试目录 '+results);
})().catch(e=>{console.error(e);process.exitCode=1;}).finally(async()=>{await browser?.close();await stop();});
