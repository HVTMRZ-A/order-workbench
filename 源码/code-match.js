(function(root){
'use strict';
const TYPES={machine:'机器',silk:'丝印',nameplate:'铭牌',manual:'说明书',carton:'纸箱'};
const norm=value=>String(value??'').normalize('NFKC').toUpperCase().replace(/[‐‑‒–—−]/g,'-').replace(/\s*-\s*/g,'-');
const key=value=>norm(value).trim();
function identifiers(value){const all=norm(value).match(/[A-Z][A-Z0-9]*(?:[-/][A-Z0-9]+)*/g)||[];return [...new Set(all.filter(x=>/\d/.test(x)&&x.length>=3))].sort((a,b)=>b.length-a.length).slice(0,1);}
const findCodes=text=>norm(text).match(/(?<![A-Z0-9.])\d+(?:\.\d+){3,}(?![A-Z0-9.])/g)||[];
function documentRows(text){
 const separator=text.includes('\t')?'\t':text.includes(',')?',':null,rows=[];let cells=[],cell='',quoted=false,line=1,start=1;
 const push=()=>{cells.push(cell);const columns=cells.map(x=>x.replace(/[\r\n]+/g,' ').trim()).filter(Boolean),raw=columns.join('\t').trim();if(raw)rows.push({line:start,raw,columns});cells=[];cell='';start=line;};
 for(let i=0;i<text.length;i++){const c=text[i];if(c==='"'&&(quoted||!cell.trim())){if(quoted&&text[i+1]==='"'){cell+='"';i++;}else quoted=!quoted;continue;}if(c==='\n'){line++;if(quoted)cell+='\n';else push();}else if(separator&&c===separator&&!quoted){cells.push(cell);cell='';}else cell+=c;}
 if(quoted)throw Error('文档文字中的引号未闭合，请检查复制范围或使用文档导入。');push();return rows;
}
function parse(text){
 text=String(text||'');if(text.length>100000)throw Error('一次最多粘贴100000个字符，请分批识别。');
 const rows=documentRows(text.replace(/\r\n?|[\u000b\u2028\u2029]/g,'\n')),lines=[];
 for(let i=0;i<rows.length;i++){
  let row={...rows[i]},codes=findCodes(row.raw);
  if(codes.length===1&&!identifiers(row.raw.replace(codes[0],'')).length){
   for(let n=0;n<12&&i+1<rows.length&&!findCodes(rows[i+1].raw).length;n++){const next=rows[++i];row.raw+='\t'+next.raw;row.columns=[...(row.columns||[]),...(next.columns||[])];if(identifiers(next.raw).length)break;}
  }
  lines.push(row);
 }
 if(lines.length>200)throw Error('一次最多识别200行，请分批粘贴。');
 return lines.map(({raw,line,columns})=>{const codes=findCodes(raw);let code='',error='';if(codes.length===1)code=codes[0];else error=codes.length?'同一行包含多个料号，请拆成多行':'未识别到点分数字料号';return {line,raw,code,description:columns.length>=3?columns.slice(2).join(' '):raw.replace(codes[0]||'','').trim(),category:columns.length>=2?columns[1]:'',error};});
}
function inferType(row){const classify=text=>{const matches=[['silk',/丝印/],['nameplate',/铭牌|标牌/],['manual',/说明书|用户手册|使用手册/],['carton',/纸箱|外箱|彩箱/]].filter(([,r])=>r.test(text));if(matches.length===1)return matches[0][0];if(matches.length>1)return '';return /逆变器|空壳机|机壳|整机|电池包|储能电池|充电器|控制器/.test(text)?'machine':'';};return classify(row.category||'')||classify(row.raw);}
function codeOf(o,type){return type==='machine'?o.machineCode:o.materials[type].code;}
function modelMatches(row,o){const tokens=new Set(norm(row.raw.replace(row.code,'')).match(/[A-Z][A-Z0-9]*(?:[-/][A-Z0-9]+)*/g)||[]);return [...identifiers(o.model),...identifiers(o.customerModel)].some(x=>tokens.has(x));}
function evaluate(row,orders,current={}){
 const o=orders.find(x=>x.id===row.orderId);row.current='';row.conflict=false;row.same=false;row.blocked=!!row.error;row.reason=row.error||'';
 if(!row.type||!TYPES[row.type]){row.blocked=true;row.reason=row.reason||'无法判断料号项目，请手动选择';}
 if(!o){row.blocked=true;row.reason=row.reason||(row.candidates.length>1?'匹配到多个机型，请指定':'未匹配到机型，请指定');}
 if(o&&row.type&&TYPES[row.type]){if(o.archived){row.blocked=true;row.reason='归档机型不能填写';}if(row.type!=='machine'&&o.materials[row.type].status==='na'){row.blocked=true;row.reason='此机型该项目为“不需要”';}row.current=current[o.id]?.[row.type]??codeOf(o,row.type);row.same=key(row.current)===key(row.code);row.conflict=!!row.current&&!row.same;}
 return row;
}
function markRows(rows){
 for(const r of rows){r.clash=false;r.duplicate=false;}const seen=new Map();for(const row of rows){if(!row.orderId||!row.type||row.blocked)continue;const id=JSON.stringify([row.orderId,row.type]);if(!seen.has(id))seen.set(id,[]);seen.get(id).push(row);}for(const group of seen.values()){const codes=new Set(group.map(r=>key(r.code)));if(codes.size>1)for(const row of group)row.clash=true;else group.slice(1).forEach(row=>row.duplicate=true);}return rows;
}
function recognize(text,orders,target='machine',current={}){
 if(target!=='auto'&&!TYPES[target])throw Error('请选择有效料号项目');
 const rows=parse(text).map((row,index)=>{const type=target==='auto'?inferType(row):target,candidates=orders.filter(o=>!o.archived&&modelMatches(row,o)).map(o=>o.id);return evaluate({...row,index,type,candidates,orderId:candidates.length===1?candidates[0]:'',use:false,replace:false,clash:false,duplicate:false},orders,current);});
 markRows(rows);
 for(const row of rows)row.use=!row.blocked&&!row.conflict&&!row.same&&!row.clash&&!row.duplicate;
 return rows;
}
function selectedEdits(rows,orders,current={}){
 const edits=Object.create(null),issues=[],chosen=rows.filter(r=>r.use),seen=new Map();if(!chosen.length)issues.push('请至少选择一行识别结果。');
 for(const raw of chosen){const r=evaluate({...raw},orders,current);if(r.blocked){issues.push('第'+r.line+'行：'+r.reason);continue;}if(r.conflict&&!r.replace){issues.push('第'+r.line+'行已有不同料号，请明确勾选替换。');continue;}const identity=JSON.stringify([r.orderId,r.type]);if(seen.has(identity)&&seen.get(identity)!==r.code){issues.push('同一机型的'+TYPES[r.type]+'选中了不同料号，请只保留一行。');continue;}seen.set(identity,r.code);if(!r.same){if(!edits[r.orderId])edits[r.orderId]={};edits[r.orderId][r.type]=r.code;}}
 return {edits,issues,count:Object.values(edits).reduce((n,x)=>n+Object.keys(x).length,0),selected:chosen.length};
}
const api={TYPES,norm,identifiers,documentRows,parse,inferType,codeOf,modelMatches,evaluate,markRows,recognize,selectedEdits};if(typeof module!=='undefined')module.exports=api;root.CodeMatch=api;
})(typeof window!=='undefined'?window:globalThis);
