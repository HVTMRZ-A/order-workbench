$ErrorActionPreference = 'Stop'
Set-Location -LiteralPath $PSScriptRoot
$taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $taskCompiler)) { $taskCompiler = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path -LiteralPath $taskCompiler)) { throw '未找到 .NET Framework 编译器。' }
if (-not (Test-Path -LiteralPath '..\Microsoft.Web.WebView2.Core.dll')) { throw '请保持完整目录结构：源码文件夹应位于程序与 WebView2 组件旁。' }
& $taskCompiler /nologo /target:winexe /platform:anycpu /optimize+ /win32icon:app.ico /out:..\订单工作台.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.Xml.Linq.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll /reference:..\Microsoft.Web.WebView2.Core.dll /reference:..\Microsoft.Web.WebView2.WinForms.dll /resource:index.html,index.html /resource:style.css,style.css /resource:model.js,model.js /resource:production.js,production.js /resource:production-ui.js,production-ui.js /resource:evidence.js,evidence.js /resource:evidence-ui.js,evidence-ui.js /resource:batch.js,batch.js /resource:batch-ui.js,batch-ui.js /resource:save-state.js,save-state.js /resource:app.js,app.js /resource:app.ico,app.ico Program.cs ProductionPreview.cs WorkbenchWindow.cs EvidenceFiles.cs UpdateCore.cs UpdateDialog.cs
if ($LASTEXITCODE -ne 0) { throw '编译失败。请先退出正在运行的程序，再检查编译错误。' }
Write-Host '编译成功，data 文件夹保持原样。'

& $taskCompiler /nologo /target:winexe /platform:anycpu /optimize+ /win32icon:app.ico /out:..\订单更新助手.exe /reference:System.Windows.Forms.dll /reference:System.Drawing.dll /reference:System.Web.Extensions.dll /reference:System.IO.Compression.dll /reference:System.IO.Compression.FileSystem.dll UpdaterProgram.cs UpdateCore.cs
if ($LASTEXITCODE -ne 0) { throw '更新助手编译失败。' }
