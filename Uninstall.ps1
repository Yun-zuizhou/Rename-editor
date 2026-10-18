<#
  BatchRename 卸载脚本：删除右键菜单项（只动 HKCU）。

  用法：
    powershell -ExecutionPolicy Bypass -File .\Uninstall.ps1
    powershell -ExecutionPolicy Bypass -File .\Uninstall.ps1 -RemoveFiles      # 同时删除已安装的 exe 与配置
    powershell -ExecutionPolicy Bypass -File .\Uninstall.ps1 -KeepClassicMenu  # 保留经典右键菜单设置
#>
[CmdletBinding()]
param(
    [switch]$RemoveFiles,
    [switch]$KeepClassicMenu
)

$ErrorActionPreference = 'Stop'

$installDir = Join-Path $env:LOCALAPPDATA 'Programs\DSHBatchRename'
$dataDir    = Join-Path $env:LOCALAPPDATA 'DSHBatchRename'

Write-Host '== 批量重命名 · 卸载 ==' -ForegroundColor Cyan

$cu = [Microsoft.Win32.Registry]::CurrentUser
$keys = @(
    '*\shell\DshBatchRename',
    '*\shell\DshBatchRenameQuick',
    'Directory\shell\DshBatchRename',
    'Directory\Background\shell\DshBatchRename',
    'Directory\Background\shell\DshBatchRenameUndo'
)
foreach ($k in $keys) {
    $full = 'Software\Classes\' + $k
    try {
        $cu.DeleteSubKeyTree($full, $false)
        Write-Host "  已删除: $k"
    } catch {
        Write-Host ("  跳过: {0}（{1}）" -f $k, $_.Exception.Message)
    }
}

if (-not $KeepClassicMenu) {
    try {
        $cu.DeleteSubKeyTree('Software\Classes\CLSID\{86ca1aa0-34aa-4e8b-a509-50c905bae2a2}', $false)
        Write-Host '  已恢复 Win11 默认右键菜单'
    } catch { }
}

try { & ie4uinit.exe -show 2>$null | Out-Null } catch { }

if ($RemoveFiles) {
    foreach ($d in @($installDir, $dataDir)) {
        if (Test-Path -LiteralPath $d) {
            try { Remove-Item -LiteralPath $d -Recurse -Force; Write-Host "  已删除: $d" }
            catch { Write-Host ("  删除失败: {0}（{1}）" -f $d, $_.Exception.Message) }
        }
    }
}

Write-Host '卸载完成。' -ForegroundColor Green
