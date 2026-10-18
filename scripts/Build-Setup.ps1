<#
  打包单文件安装器：dist\RenameEditorSetup.exe
  把主程序、菜单 DLL、包清单、包图标全部嵌进去，双击即可安装。

  用法：
    powershell -ExecutionPolicy Bypass -File .\scripts\Build-Setup.ps1
#>
[CmdletBinding()]
param(
    [switch]$Rebuild   # 先重新编译主程序和菜单 DLL
)

$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $PSScriptRoot       # scripts\ 的上一层
$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path -LiteralPath $csc)) {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'
}

$exe      = Join-Path $root 'dist\BatchRename.exe'
$dll      = Join-Path $root 'dist\DSHBRShell.dll'
$manifest = Join-Path $root 'package\AppxManifest.xml'
$assets   = Join-Path $root 'package\Assets'
$icon     = Join-Path $root 'src\shell\shell.ico'
$source   = Join-Path $root 'src\setup\Setup.cs'
$out      = Join-Path $root 'dist\RenameEditorSetup.exe'

Write-Host '== 打包安装器 ==' -ForegroundColor Cyan

if ($Rebuild -or -not (Test-Path -LiteralPath $exe)) {
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\Build.ps1')
    if ($LASTEXITCODE -ne 0) { throw '主程序编译失败' }
}
if ($Rebuild -or -not (Test-Path -LiteralPath $dll)) {
    & powershell.exe -NoLogo -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'scripts\Build-Shell.ps1')
    if ($LASTEXITCODE -ne 0) { throw '菜单扩展编译失败' }
}

foreach ($f in @($exe, $dll, $manifest)) {
    if (-not (Test-Path -LiteralPath $f)) { throw "缺少输入文件: $f" }
}
if (-not (Test-Path -LiteralPath $assets)) { throw "缺少包图标目录: $assets（先运行 scripts\Pack-Shell.ps1）" }

$resArgs = @(
    "/resource:$exe,BatchRename.exe"
    "/resource:$dll,DSHBRShell.dll"
    "/resource:$manifest,AppxManifest.xml"
    "/resource:$(Join-Path $assets 'StoreLogo.png'),StoreLogo.png"
    "/resource:$(Join-Path $assets 'Square150x150Logo.png'),Square150x150Logo.png"
    "/resource:$(Join-Path $assets 'Square44x44Logo.png'),Square44x44Logo.png"
)

$cscArgs = New-Object 'System.Collections.Generic.List[string]'
foreach ($a in @('/nologo', '/target:winexe', '/optimize+', "/out:$out")) { $cscArgs.Add($a) }
if (Test-Path -LiteralPath $icon) { $cscArgs.Add("/win32icon:$icon") }
foreach ($r in $resArgs) { $cscArgs.Add($r) }
foreach ($r in @('/reference:System.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', $source)) { $cscArgs.Add($r) }

& $csc $cscArgs.ToArray()
if ($LASTEXITCODE -ne 0) { throw "编译失败: $LASTEXITCODE" }

$size = (Get-Item -LiteralPath $out).Length
Write-Host ("  输出: {0} ({1:N0} 字节 / {2:N1} MB)" -f $out, $size, ($size / 1MB))
