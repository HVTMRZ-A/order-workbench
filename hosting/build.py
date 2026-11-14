from pathlib import Path
from zipfile import ZipFile, ZIP_DEFLATED
import argparse, hashlib, json, shutil, re

root = Path(__file__).resolve().parent
public = root / 'public'
(public / 'packages').mkdir(parents=True, exist_ok=True)
parser = argparse.ArgumentParser(description='构建 Cloudflare 静态更新下载站（仅使用已核对的正式包）')
parser.add_argument('--version', required=True)
parser.add_argument('--package', required=True, type=Path)
parser.add_argument('--sha256', required=True)
parser.add_argument('--notes', required=True)
args = parser.parse_args()
version = args.version
assert re.fullmatch(r'\d+\.\d+\.\d+', version)
name = f'order-workbench-v{version}-portable.zip'
original = args.package.resolve()
sha = hashlib.sha256(original.read_bytes()).hexdigest()
assert sha == args.sha256.lower()
assert original.stat().st_size <= 25 * 1024 * 1024, '静态托管单文件限额 25 MiB'
with ZipFile(original) as package:
    update = json.loads(package.read('update-manifest.json'))
    assert update['version'] == version and len(update['files']) == 9
    assert not any(p.lower().startswith('data/') for p in package.namelist())
    for entry in update['files']:
        raw = package.read(entry['path'])
        assert len(raw) == entry['size'] and hashlib.sha256(raw).hexdigest() == entry['sha256']
shutil.copyfile(original, public / 'packages' / name)
manifest = dict(schema=1, repository='HVTMRZ-A/order-workbench', version=version,
    notes=args.notes,
    releaseUrl=f'https://github.com/HVTMRZ-A/order-workbench/releases/tag/v{version}',
    file='packages/' + name, size=original.stat().st_size, sha256=sha)
(public / 'latest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2)+'\n', encoding='utf-8')
(public / '_headers').write_text('/*\n  X-Content-Type-Options: nosniff\n  X-Robots-Tag: noindex\n/latest.json\n  Cache-Control: no-store\n  Content-Type: application/json; charset=utf-8\n/packages/*\n  Content-Disposition: attachment\n  Cache-Control: public, max-age=31536000, immutable\n', encoding='utf-8')
(public / 'index.html').write_text(f'''<!doctype html><html lang="zh-CN"><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>订单工作台 · 更新下载</title>
<style>body{{font:16px/1.8 system-ui,sans-serif;background:#f3f7f6;color:#173c37;margin:0;padding:8vw}}main{{max-width:700px;margin:auto;background:white;border-radius:18px;padding:40px}}h1{{font-size:30px}}a{{color:#087c70}}.download{{display:inline-block;background:#087c70;color:white;padding:10px 24px;border-radius:8px;text-decoration:none}}code{{overflow-wrap:anywhere;font-size:12px}}small{{color:#677f7a}}</style>
<main><small>订单 · 进度 · 协作</small><h1>订单工作台</h1><p>正式版本 v{version} · Windows 便携版</p><p><a class="download" href="packages/{name}">下载完整程序包</a></p><p>使用现有订单时，先保存并退出原程序，再覆盖程序文件，并保留原 data 文件夹。</p><p><a href="{manifest['releaseUrl']}">GitHub 备用下载及版本说明</a></p><p><small>SHA256</small><br><code>{sha}</code></p></main></html>''', encoding='utf-8')
archive = root / 'upload.zip'
with ZipFile(archive, 'w', ZIP_DEFLATED) as z:
    for filename in ['_headers','index.html','latest.json','packages/'+name]:
        z.write(public / filename, filename)
with ZipFile(archive) as z:
    assert len(z.namelist()) == 4 and z.testzip() is None
    assert not any('data/' in f for f in z.namelist())
print(json.dumps(dict(upload=str(archive),files=z.namelist(),size=archive.stat().st_size),ensure_ascii=True))
