# Cloudflare 更新托管

项目名：`order-workbench-updates`。地址：https://order-workbench-updates.nuoyanzhang99.workers.dev/

发布新版本时，先构建并验证正式便携 ZIP，再运行：

```powershell
python hosting/build.py --version 1.21.0 --package dist/order-workbench-v1.21.0-portable.zip --sha256 <已核对的SHA256> --notes "本次正式版本说明"
```

到现有 Cloudflare Worker 的“新部署”上传生成的 hosting/upload.zip，不要创建重复项目。版本清单、下载页和程序包应在同一次部署发布。只上传正式程序，不上传 data、账户或依据文件。

部署后检查 latest.json、程序包实际下载与哈希。GitHub Release 和 Cloudflare 清单应保持一致；GitHub 源是旧版升级到双源版本的过渡通道。正式包超过静态单文件 25 MiB 时需另行调整托管方案。
