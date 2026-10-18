<#
    Build.ps1 — 编译 BatchRename.exe
    用法: powershell -ExecutionPolicy Bypass -File Build.ps1
    说明: 用 .NET Framework 自带的 csc.exe 编译，无需安装任何 SDK。
#>
[CmdletBinding()]
param(
    [switch]$RunSelfTest
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcDir = Join-Path $root 'src'
$distDir = Join-Path $root 'dist'
$out = Join-Path $distDir 'BatchRename.exe'

$manifest = Join-Path $srcDir 'app.manifest'
if (-not (Test-Path -LiteralPath $manifest)) { throw "缺少清单文件: $manifest" }
$sources = @(Get-ChildItem -LiteralPath $srcDir -Filter '*.cs' -File | Sort-Object Name | ForEach-Object { $_.FullName })
if ($sources.Count -eq 0) { throw "在 $srcDir 下找不到 .cs 源码" }

# csc 4.0 不带 BOM 时按系统 ANSI 代码页解码，中文会乱码 —— 先把每个源文件补上 BOM。
foreach ($f in $sources) {
    $bytes = [System.IO.File]::ReadAllBytes($f)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    if (-not $hasBom) {
        $text = [System.IO.File]::ReadAllText($f, [System.Text.Encoding]::UTF8)
        [System.IO.File]::WriteAllText($f, $text, (New-Object System.Text.UTF8Encoding($true)))
    }
}

New-Item -ItemType Directory -Force -Path $distDir | Out-Null

$csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    $csc = Join-Path $env:SystemRoot 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}
if (-not (Test-Path -LiteralPath $csc)) { throw '找不到 csc.exe（需要 .NET Framework 4.x）' }

$cscArgs = @(
    '/nologo'
    '/target:winexe'
    '/platform:anycpu'
    '/optimize+'
    '/debug-'
    '/warn:4'
    '/r:System.dll'
    '/r:System.Core.dll'
    '/r:System.Drawing.dll'
    '/r:System.Windows.Forms.dll'
    "/win32manifest:$manifest"
    "/out:$out"
) + $sources

Write-Host ("编译: " + ($sources -join ', ')) -ForegroundColor Cyan
& $csc $cscArgs
if ($LASTEXITCODE -ne 0) { throw "编译失败，csc 退出码 $LASTEXITCODE" }

$fi = Get-Item -LiteralPath $out
Write-Host ("编译成功: {0} ({1:N0} 字节)" -f $fi.FullName, $fi.Length) -ForegroundColor Green

if ($RunSelfTest) {
    $logFile = Join-Path $root '_selftest.txt'
    if (Test-Path -LiteralPath $logFile) { Remove-Item -LiteralPath $logFile -Force }
    # 注意：沙箱里 Start-Process -Wait 会永久挂起；winexe 用 & 调用又不会等待，
    # 因此改为「启动 + 轮询结果文件」。
    & $out --selftest $logFile
    $deadline = (Get-Date).AddSeconds(120)
    while (-not (Test-Path -LiteralPath $logFile) -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 200
    }
    if (-not (Test-Path -LiteralPath $logFile)) { throw "自测超时，未生成 $logFile" }
    $content = Get-Content -LiteralPath $logFile -Encoding UTF8 -Raw
    $content -split "`r?`n" | ForEach-Object { if ($_ -ne '') { Write-Host $_ } }
    $ok = ($content -match 'RESULT:\s*ALL PASS')
    Write-Host ("自测: " + $(if ($ok) { 'ALL PASS' } else { '有问题' })) -ForegroundColor $(if ($ok) { 'Green' } else { 'Red' })
    if (-not $ok) { throw '自测未通过' }
}
