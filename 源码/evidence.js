(function(root){
'use strict';
const STAGES={machine:'机器确认',purchase:'采购交接',silk:'丝印',nameplate:'铭牌',manual:'说明书',carton:'纸箱',plate:'二厂网版',plan:'计划交接',other:'其他依据'};
function normalizePath(input){
 if(typeof input!=='string')throw Error('请填写完整文件路径');
 let value=input.trim();if(value.startsWith('"')&&value.endsWith('"'))value=value.slice(1,-1).trim();value=value.replace(/\//g,'\\');
 if(!value||value.length>4096||/[\x00-\x1f"<>|?*]/.test(value)||value.startsWith('\\\\?\\')||value.startsWith('\\\\.\\'))throw Error('文件路径格式无效，请选择普通文件或粘贴完整路径');
 let prefix,parts;
 if(/^[A-Za-z]:\\/.test(value)){prefix=value.slice(0,2).toUpperCase()+'\\';parts=value.slice(3).split('\\');if(value.slice(2).includes(':'))throw Error('文件路径不能包含额外的冒号');}
 else if(value.startsWith('\\\\')){const all=value.slice(2).split('\\');if(all.length<2||!all[0]||!all[1]||all.some(x=>x.includes(':'))||['.','..'].includes(all[0])||['.','..'].includes(all[1]))throw Error('共享路径应包含服务器和共享目录');prefix='\\\\'+all[0]+'\\'+all[1];parts=all.slice(2);}
 else throw Error('请使用带盘符的完整路径或共享文件夹路径，不能只填文件名');
 const rest=[];for(const part of parts){if(!part||part==='.')continue;if(part==='..'){if(!rest.length)throw Error('文件路径不能越过根目录');rest.pop();}else rest.push(part);}
 return prefix+(rest.length?(prefix.endsWith('\\')?'':'\\')+rest.join('\\'):'');
}
const key=path=>normalizePath(path).toLowerCase();
const name=path=>String(path||'').replace(/[\\/]+$/,'').split(/[\\/]/).pop()||path;
const list=order=>Array.isArray(order?.evidence)?order.evidence:[];
function errors(value){if(value===undefined)return [];const issues=[];if(!Array.isArray(value))return ['依据文件记录格式错误'];if(value.length>200)return ['每个机型最多关联 200 个依据文件'];const ids=new Set(),links=new Set();for(const item of value){if(!item||typeof item!=='object'||Array.isArray(item)){issues.push('依据文件记录必须是对象');continue;}if(Object.keys(item).some(k=>!['id','stage','path','note','addedAt'].includes(k)))issues.push('依据文件只允许保存路径和关联说明');if(typeof item.id!=='string'||!item.id||item.id.length>150||ids.has(item.id))issues.push('依据文件标识缺失或重复');ids.add(item.id);if(!Object.hasOwn(STAGES,item.stage))issues.push('依据文件关联的流程无效');if(typeof item.note!=='string'||item.note.length>2000||typeof item.addedAt!=='string'||item.addedAt.length>100)issues.push('依据文件说明或时间格式错误');try{const signature=item.stage+'|'+key(item.path);if(links.has(signature))issues.push('同一步骤重复关联了相同路径');links.add(signature);}catch(e){issues.push(e.message);}}return issues;}
function merge(order,paths,stage,note='',replaceId='',at=new Date().toISOString()){
 if(!Object.hasOwn(STAGES,stage))throw Error('请选择关联流程');if(typeof note!=='string'||note.length>2000)throw Error('依据说明最多 2000 字');if(!Array.isArray(paths)||!paths.length||paths.length>50)throw Error('请一次关联 1 至 50 个文件');if(replaceId&&paths.length!==1)throw Error('重新关联时请选择一份文件');
 const normalized=[...new Map(paths.map(p=>{const path=normalizePath(p);return [key(path),path];})).values()];
 const result=JSON.parse(JSON.stringify(order)),records=[...list(result)];let changed=0,duplicates=0;
 if(replaceId){const index=records.findIndex(x=>x.id===replaceId);if(index<0)throw Error('原关联已不存在，请重新打开详情');const path=normalized[0];if(records.some(x=>x.id!==replaceId&&x.stage===stage&&key(x.path)===key(path)))throw Error('此步骤已有相同路径');const next={...records[index],stage,path,note:note.trim()};if(JSON.stringify(next)!==JSON.stringify(records[index])){records[index]=next;changed++;}}
 else for(const path of normalized){if(records.some(x=>x.stage===stage&&key(x.path)===key(path))){duplicates++;continue;}records.push({id:'f_'+Date.now().toString(36)+'_'+Math.random().toString(36).slice(2,12),stage,path,note:note.trim(),addedAt:at});changed++;}
 if(records.length>200)throw Error('每个机型最多关联 200 个依据文件');if(changed)result.evidence=records;return {order:result,changed,duplicates};
}
function changes(before,after){const old=new Map(list(before).map(x=>[x.id,x])),next=new Map(list(after).map(x=>[x.id,x])),result=[];for(const [id,item]of next){const previous=old.get(id);if(!previous)result.push('关联依据文件 ['+STAGES[item.stage]+'] '+name(item.path)+'：'+item.path);else if(JSON.stringify(previous)!==JSON.stringify(item))result.push('更新依据文件 ['+STAGES[item.stage]+'] '+name(item.path)+'：'+(previous.path===item.path?'说明或关联步骤已更新':previous.path+' → '+item.path));}for(const [id,item]of old)if(!next.has(id))result.push('解除文件关联 ['+STAGES[item.stage]+'] '+name(item.path)+'（原文件保留）');return result;}
const api={STAGES,normalizePath,key,name,list,errors,merge,changes};if(typeof module!=='undefined')module.exports=api;root.E=api;
})(typeof window!=='undefined'?window:globalThis);
