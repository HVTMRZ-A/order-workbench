(function(root){
'use strict';
function htmlText(html){
 if(html.length>2000000)throw Error('复制的文档内容过大，请缩小选择范围。');
 const template=document.createElement('template');template.innerHTML=html;
 const skipped=new Set(['SCRIPT','STYLE','NOSCRIPT','IFRAME','OBJECT','EMBED','IMG','LINK','META','SVG','MATH']);
 const text=node=>{if(node.nodeType===3)return node.textContent;if(node.nodeType!==1||skipped.has(node.tagName))return '';if(node.tagName==='BR')return '\n';let result=[...node.childNodes].map(text).join('');if(['P','DIV','LI','H1','H2','H3','H4','H5','H6'].includes(node.tagName))result+='\n';return result;};
 const output=[];
 const walk=node=>{if(node.nodeType===3){if(node.textContent.trim())output.push(node.textContent);return;}if(node.nodeType!==1)return;if(skipped.has(node.tagName))return;
  if(node.tagName==='TABLE'){for(const row of node.querySelectorAll('tr')){if(row.closest('table')!==node)continue;const cells=[...row.children].filter(x=>['TD','TH'].includes(x.tagName)).map(x=>text(x).replace(/\s+/g,' ').trim());if(cells.some(Boolean))output.push(cells.join('\t')+'\n');}return;}
  if(['P','DIV','LI','H1','H2','H3','H4','H5','H6'].includes(node.tagName)&&!node.querySelector('table')){output.push(text(node));return;}
  if(node.tagName==='BR'){output.push('\n');return;}for(const child of node.childNodes)walk(child);
 };
 for(const node of template.content.childNodes)walk(node);
 return output.join('').replace(/\r\n?/g,'\n').replace(/\n[ \t]*\n+/g,'\n').trim();
}
function read(data){
 const plain=data?.getData('text/plain')||data?.getData('text')||'',html=data?.getData('text/html')||'';
 const rendered=html?htmlText(html):'',text=rendered&&(/<table\b/i.test(html)||!plain.trim())?rendered:plain;
 if(text.length>100000)throw Error('一次最多粘贴100000个字符，请缩小选择范围。');
 return {text:text.replace(/\r\n?|[\u000b\u2028\u2029]/g,'\n'),format:rendered&&text===rendered?'文档表格/富文本':'纯文字'};
}
root.CodeClipboard={htmlText,read};
})(typeof window!=='undefined'?window:globalThis);
