(function(root){
 'use strict';
 function validate(items){
  if(!Array.isArray(items)||items.length>5000)throw Error('持续追踪列表无效，最多 5000 项');
  const ids=new Set();for(const item of items){if(!item||typeof item.id!=='string'||!item.id||item.id.length>100||ids.has(item.id))throw Error('持续追踪标识缺失或重复');ids.add(item.id);if(typeof item.text!=='string'||!item.text.trim()||item.text.length>4000)throw Error('请填写追踪事项，最多 4000 字');if(typeof item.contract!=='string'||item.contract.length>200||typeof item.confirmed!=='boolean')throw Error('持续追踪资料格式错误');}return items;
 }
 function merge(current,incoming,overwrite){validate(current);validate(incoming);const result=current.map(x=>({...x}));let count=0;for(const item of incoming){const index=result.findIndex(x=>x.id===item.id);if(index>=0&&!overwrite)continue;if(index>=0)result[index]={...item};else result.push({...item});count++;}validate(result);return {items:result,count};}
 function filter(items,state='',query=''){const q=query.trim().toLowerCase();return items.filter(x=>(state===''||x.confirmed===(state==='confirmed'))&&(!q||[x.text,x.contract].join(' ').toLowerCase().includes(q))).slice().sort((a,b)=>Number(a.confirmed)-Number(b.confirmed)||(b.updatedAt||'').localeCompare(a.updatedAt||''));}
 const api={validate,merge,filter};if(typeof module==='object'&&module.exports)module.exports=api;else root.Tracking=api;
})(typeof globalThis==='undefined'?this:globalThis);
