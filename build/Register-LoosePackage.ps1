# 在 Windows PowerShell 5.1 下执行：以「松散文件注册」方式安装包标识（开发者模式）
# 这条路不需要证书/签名，绕开了非交互会话里无法把自签名证书装进根证书存储的问题。
# 由 Install.ps1 调用（必须用 powershell.exe，因为 Appx 模块不在 pwsh 7 里）
param(
    [Parameter(Mandatory = $true)][string]$ManifestPath
)

$ErrorActionPreference = 'Stop'
$packageName = 'DSHBatchRename.Shell'

if (-not (Test-Path -LiteralPath $ManifestPath)) { throw "找不到清单: $ManifestPath" }

# 开发者模式检查（松散注册需要）
$unlock = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\AppModelUnlock' -ErrorAction SilentlyContinue
$devMode = ($unlock.AllowDevelopmentWithoutDevLicense -eq 1) -or ($unlock.AllowAllTrustedApps -eq 1)
Write-Host ("  开发者模式/旁加载: " + $(if ($devMode) { '已开启' } else { '未开启（可能失败）' }))

# 同版本已注册要先卸载，否则 0x80073CF9
$old = Get-AppxPackage -Name $packageName -ErrorAction SilentlyContinue
if ($old) {
    Write-Host "  卸载旧注册 $($old.Version)"
    Remove-AppxPackage -Package $old.PackageFullName
}

Add-AppxPackage -Register $ManifestPath -ErrorAction Stop
$pkg = Get-AppxPackage -Name $packageName
if (-not $pkg) { throw '注册后仍查不到包' }
Write-Host "  已注册: $($pkg.PackageFullName)"
Write-Host "  安装位置: $($pkg.InstallLocation)"
