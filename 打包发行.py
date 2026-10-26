from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import hashlib, json, re, sys

root=Path(__file__).resolve().parent
version=sys.argv[1] if len(sys.argv)>1 else '1.16.0'
assert re.fullmatch(r'\d+\.\d+\.\d+',version),'版本必须为三段数字'
source=root/'源码'/'UpdateCore.cs'
assert ('VersionText="'+version+'"') in source.read_text(encoding='utf-8-sig'),'源码版本与发行版本不一致'
runtime=['订单工作台.exe','订单更新助手.exe','Microsoft.Web.WebView2.Core.dll','Microsoft.Web.WebView2.WinForms.dll','runtimes/win-x64/native/WebView2Loader.dll','runtimes/win-x86/native/WebView2Loader.dll','先看这里.txt','验证说明.txt','WebView2组件许可.txt']
for name in runtime:assert (root/name).is_file(),'缺少完整组件：'+name
files=[{'path':name,'size':(root/name).stat().st_size,'sha256':hashlib.sha256((root/name).read_bytes()).hexdigest()} for name in runtime]
manifest={'schema':1,'version':version,'files':files}
manifest_text=json.dumps(manifest,ensure_ascii=False,indent=2)+'\n'
(root/'update-manifest.json').write_text(manifest_text,encoding='utf-8')
dist=root/'dist';dist.mkdir(exist_ok=True)
archive=dist/('order-workbench-v'+version+'-portable.zip')
with ZipFile(archive,'w',ZIP_DEFLATED,compresslevel=9) as z:
 for name in runtime:z.write(root/name,name)
 for path in sorted((root/'源码').iterdir()):
  if path.is_file() and path.suffix.lower() in ['.cs','.js','.css','.html','.ps1','.ico']:z.write(path,path.relative_to(root).as_posix())
 z.writestr('update-manifest.json',manifest_text)
with ZipFile(archive) as z:
 assert z.testzip() is None
 for f in files:assert hashlib.sha256(z.read(f['path'])).hexdigest()==f['sha256']
 assert not any(n.lower().startswith(('data/','work/','tests/')) or n.endswith(('orders.json','instance.json')) for n in z.namelist())
print(json.dumps({'文件':str(archive),'文件数':len(z.namelist()),'大小':archive.stat().st_size,'SHA256':hashlib.sha256(archive.read_bytes()).hexdigest()},ensure_ascii=False,indent=2))
