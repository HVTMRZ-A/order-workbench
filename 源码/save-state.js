(function(root){
'use strict';
function timestamp(value){if(!value)return '暂无保存记录';const date=new Date(value);if(Number.isNaN(date.getTime()))return '暂无保存记录';const pad=n=>String(n).padStart(2,'0');return `${date.getFullYear()}-${pad(date.getMonth()+1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}:${pad(date.getSeconds())}`;}
function describe({ready,saving,error,changed,demo}){if(saving)return {state:'saving',label:'正在保存，请稍候',hint:'写入完成前请保留此窗口'};if(error)return {state:'error',label:'保存失败 / 尚未确认保存',hint:'当前编辑仍保留，请检查后重试'};if(changed)return {state:'dirty',label:'有未保存更改',hint:'点击保存，或按 Ctrl+S'};if(!ready)return {state:'loading',label:'正在读取本地数据…',hint:''};if(demo)return {state:'demo',label:'示例模式，不写入正式订单',hint:'示例修改不计入最近保存时间'};return {state:'saved',label:'所有更改已保存',hint:'已写入本地订单文件'};}
const api={timestamp,describe};if(typeof module!=='undefined')module.exports=api;root.SaveState=api;
})(typeof window!=='undefined'?window:globalThis);
