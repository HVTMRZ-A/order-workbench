'use strict';
let preferences=null,preferencesDraft=null,settingsBusy=false,settingsReady=false;
const appearanceMedia=window.matchMedia('(prefers-color-scheme: dark)');
const iconPaths={
 flow:'<path d="M6 5h12v5H6zM9 15h9v5H6v-5h3zm3-5v5M9 17.5h6"/>',
 box:'<path d="m12 3 9 5-9 5-9-5 9-5zm-9 5v10l9 5 9-5V8M12 13v10M7.5 5.5l9 5"/>',
 grid:'<rect x="3" y="3" width="7" height="7" rx="1"/><rect x="14" y="3" width="7" height="7" rx="1"/><rect x="3" y="14" width="7" height="7" rx="1"/><rect x="14" y="14" width="7" height="7" rx="1"/>',
 check:'<path d="M9 3h6l1 3h4v15H4V6h4l1-3zm0 0v5h6V3M8 14l3 3 5-6"/>'
};
const iconNames={sequence:'序 · 经典',flow:'流程',box:'交付',grid:'工作台',check:'确认'};
function iconMarkup(key){return key==='sequence'?'序':`<svg viewBox="0 0 24 26" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${iconPaths[key]||iconPaths.flow}</svg>`;}
function settingsPending(){return !!preferencesDraft&&JSON.stringify(preferencesDraft)!==JSON.stringify(preferences);}
function settingsIsBusy(){return settingsBusy;}
function effectiveTheme(p){return p.theme==='system'?(appearanceMedia.matches?'dark':'light'):p.theme;}
function paintBrand(el,p){el.classList.toggle('custom-image',p.icon==='custom');if(p.icon==='custom'){const img=document.createElement('img');img.src=p.image;img.alt='自定义工作台图标';el.replaceChildren(img);}else el.innerHTML=iconMarkup(p.icon);}
function applyAppearance(p){const theme=effectiveTheme(p);document.documentElement.dataset.theme=theme;document.querySelector('meta[name="color-scheme"]').content=theme;paintBrand($('.brand-mark'),p);if(nativeHost)window.chrome.webview.postMessage({type:'appearance',theme});}
appearanceMedia.addEventListener('change',()=>{const p=preferencesDraft||preferences;if(p?.theme==='system')applyAppearance(p);});
function settingsNote(text,error=false){const el=$('#settings-note');el.textContent=text;el.classList.toggle('error-text',error);el.setAttribute('role',error?'alert':'status');}
function refreshSettings(){
 if(!preferencesDraft)return;
 for(const b of $$('#settings-dialog [data-theme-choice]')){const on=b.dataset.themeChoice===preferencesDraft.theme;b.setAttribute('aria-pressed',String(on));b.classList.toggle('chosen',on);}
 for(const b of $$('#settings-dialog [data-icon-choice]')){const on=b.dataset.iconChoice===preferencesDraft.icon;b.setAttribute('aria-pressed',String(on));b.classList.toggle('chosen',on);}
 const custom=$('#settings-custom');custom.disabled=!preferencesDraft.image;custom.classList.toggle('chosen',preferencesDraft.icon==='custom');custom.setAttribute('aria-pressed',String(preferencesDraft.icon==='custom'));
 if(preferencesDraft.image)paintBrand($('#settings-custom-preview'),{icon:'custom',image:preferencesDraft.image});else $('#settings-custom-preview').textContent='＋';
 paintBrand($('#settings-brand-preview'),preferencesDraft);
 $('#settings-save').disabled=saving||settingsBusy||!settingsPending();
 $('#settings-form').inert=saving||settingsBusy;
 $('#settings-status').textContent=settingsBusy?'正在处理图片…':settingsPending()?'预览中 · 尚未保存':'当前设置已保存';
 applyAppearance(preferencesDraft);syncSaveStatus();
}
async function openSettings(){
 if(saving||evidenceBusy||$('dialog[open]'))return;
 const button=$('#settings-open');button.disabled=true;
 try{preferences=await api('settings');settingsReady=true;preferencesDraft=JSON.parse(JSON.stringify(preferences));$('#settings-dialog').showModal();settingsNote('修改后可先预览，点击“保存设置”后保留。');renderAccount();refreshSettings();}
 catch(e){toast('无法读取设置：'+errMsg(e),true);}finally{button.disabled=false;}
}
function cancelSettings(){if(saving||settingsBusy)return;preferencesDraft=null;$('#settings-dialog').close();if(preferences)applyAppearance(preferences);syncSaveStatus();}
async function saveSettings(){
 if(settingsBusy||saving)return false;if(!preferencesDraft)return true;
 if(!settingsPending()){cancelSettings();return true;}
 const next=JSON.parse(JSON.stringify(preferencesDraft));saving=true;saveError=false;refreshSettings();
 try{preferences=await api('settings',next);preferencesDraft=null;saveError=false;settingsReady=true;applyAppearance(preferences);$('#settings-dialog').close();toast('设置已保存 · 重启和升级后继续保留');return true;}
 catch(e){saveError=true;lastSaveProblem=errMsg(e);settingsNote('保存未成功：'+errMsg(e)+' 当前预览已保留。',true);return false;}
 finally{saving=false;if(preferencesDraft)refreshSettings();syncSaveStatus();}
}
async function normalizeIcon(file){
 if(!file||!/^image\/(png|jpeg|webp|x-icon|vnd.microsoft.icon)$/.test(file.type)||!/\.(png|jpe?g|webp|ico)$/i.test(file.name))throw Error('请选择 PNG、JPG、WebP 或 ICO 图片。');
 if(file.size>5*1024*1024)throw Error('图片不能超过 5 MB。');
 const data=await new Promise((resolve,reject)=>{const r=new FileReader();r.onload=()=>resolve(r.result);r.onerror=()=>reject(Error('读取图片失败，请重新选择。'));r.readAsDataURL(file);});
 const img=await new Promise((resolve,reject)=>{const i=new Image();i.onload=()=>resolve(i);i.onerror=()=>reject(Error('图片无法解码，请换一张图片。'));i.src=data;});
 if(!img.naturalWidth||!img.naturalHeight||img.naturalWidth>4096||img.naturalHeight>4096)throw Error('图片长宽均需不超过 4096 像素。');
 const canvas=document.createElement('canvas');canvas.width=128;canvas.height=128;const ctx=canvas.getContext('2d'),scale=Math.min(128/img.naturalWidth,128/img.naturalHeight),w=img.naturalWidth*scale,h=img.naturalHeight*scale;
 ctx.imageSmoothingQuality='high';ctx.drawImage(img,(128-w)/2,(128-h)/2,w,h);return canvas.toDataURL('image/png');
}
for(const el of $$('[data-icon-preview]'))el.innerHTML=iconMarkup(el.dataset.iconPreview);
$('#settings-open').onclick=openSettings;
$('#settings-form').addEventListener('submit',e=>{e.preventDefault();void saveSettings();});
$('#settings-dialog').addEventListener('click',async e=>{
 if(saving||settingsBusy||!preferencesDraft)return;
 if(e.target.closest('[data-settings-cancel]')){cancelSettings();return;}
 const theme=e.target.closest('[data-theme-choice]'),icon=e.target.closest('[data-icon-choice]');
 if(theme)preferencesDraft.theme=theme.dataset.themeChoice;
 if(icon)preferencesDraft.icon=icon.dataset.iconChoice;
 if(e.target.closest('#settings-custom')&&preferencesDraft.image)preferencesDraft.icon='custom';
 if(e.target.closest('#settings-image-pick')){$('#settings-image').value='';$('#settings-image').click();return;}
 if(e.target.closest('#settings-reset')){preferencesDraft={...preferencesDraft,theme:'light',icon:'sequence',image:''};settingsNote('已预览默认外观，保存后生效。');}
 if(theme||icon||e.target.closest('#settings-custom')||e.target.closest('#settings-reset'))refreshSettings();
});
$('#settings-image').addEventListener('change',async e=>{
 const file=e.target.files[0];if(!file||!preferencesDraft||saving||settingsBusy)return;
 settingsBusy=true;refreshSettings();
 try{const image=await normalizeIcon(file);preferencesDraft.image=image;preferencesDraft.icon='custom';settingsNote('图片已处理为本地图标，保存后无需保留原图片。');}
 catch(error){settingsNote(errMsg(error),true);}finally{settingsBusy=false;refreshSettings();}
});
$('#settings-dialog').addEventListener('cancel',e=>{e.preventDefault();cancelSettings();});
$('#settings-dialog').addEventListener('close',()=>{if(preferencesDraft&&!saving){preferencesDraft=null;if(preferences)applyAppearance(preferences);syncSaveStatus();}});
void(async()=>{try{const initial=await api('settings');if(!settingsReady){preferences=initial;settingsReady=true;if(!preferencesDraft)applyAppearance(preferences);}}catch(e){toast('外观设置读取失败：'+errMsg(e),true);}})();
