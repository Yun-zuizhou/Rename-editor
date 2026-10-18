<#
  BatchRename（资源管理器批量重命名）安装脚本
  默认安装 Win11 新式一级右键菜单（MSIX 稀疏包 + IExplorerCommand）；
  只写 HKCU / 当前用户证书 / 当前用户 Appx，不需要管理员权限。

  用法：
    powershell -ExecutionPolicy Bypass -File .\Install.ps1
    powershell -ExecutionPolicy Bypass -File .\Install.ps1 -ClassicVerbs   # 额外注册经典菜单项（显示在「显示更多选项」里）
    powershell -ExecutionPolicy Bypass -File .\Install.ps1 -SkipPackage    # 只装经典菜单项，不碰 MSIX
#>
[CmdletBinding()]
param(
    [switch]$ClassicVerbs,
    [switch]$SkipPackage,
    [switch]$NoCopy
)

$ErrorActionPreference = 'Stop'

$root       = Split-Path -Parent $MyInvocation.MyCommand.Path
$srcExe     = Join-Path $root 'dist\BatchRename.exe'
$srcDll     = Join-Path $root 'shell\dist\DSHBRShell.dll'
$srcManifest = Join-Path $root 'msix\AppxManifest.xml'
$installDir = Join-Path $env:LOCALAPPDATA 'Programs\DSHBatchRename'
$exe        = Join-Path $installDir 'BatchRename.exe'
$dll        = Join-Path $installDir 'Shell\DSHBRShell.dll'

Write-Host '== 批量重命名 · 安装 ==' -ForegroundColor Cyan

# ---------------------------------------------------------------- 1. 复制文件
if (-not $NoCopy) {
    if (-not (Test-Path -LiteralPath $srcExe)) {
        throw "找不到 $srcExe —— 请先运行 powershell -ExecutionPolicy Bypass -File .\Build.ps1 编译。"
    }
    New-Item -ItemType Directory -Force -Path $installDir, (Split-Path -Parent $dll) | Out-Null
    Copy-Item -LiteralPath $srcExe -Destination $exe -Force
    Write-Host "  主程序: $exe"
    if (Test-Path -LiteralPath $srcDll) {
        Copy-Item -LiteralPath $srcDll -Destination $dll -Force
        Write-Host "  菜单扩展: $dll"
    }
} elseif (-not (Test-Path -LiteralPath $exe)) {
    $exe = $srcExe
    Write-Host "  使用未复制的 exe: $exe"
}

# ---------------------------------------------------------------- 2. Win11 新式右键菜单
$packageOk = $false
if (-not $SkipPackage) {
    if (-not (Test-Path -LiteralPath $srcManifest)) {
        Write-Host '  跳过新式菜单：未找到 msix\AppxManifest.xml' -ForegroundColor DarkYellow
    } elseif (-not (Test-Path -LiteralPath $dll)) {
        Write-Host '  跳过新式菜单：未找到 shell\dist\DSHBRShell.dll（先运行 shell\Build-Shell.ps1）' -ForegroundColor DarkYellow
    } else {
        # 松散文件注册：安装目录本身就是包目录，清单和资产复制进去即可，无需签名
        Copy-Item -LiteralPath $srcManifest -Destination (Join-Path $installDir 'AppxManifest.xml') -Force
        $srcAssets = Join-Path $root 'msix\stage\Assets'
        if (Test-Path -LiteralPath $srcAssets) {
            Copy-Item -LiteralPath $srcAssets -Destination $installDir -Recurse -Force
        }
        $register = Join-Path $root 'tools\Register-LoosePackage.ps1'
        & powershell.exe -NoLogo -NoProfile -NonInteractive -ExecutionPolicy Bypass `
            -File $register -ManifestPath (Join-Path $installDir 'AppxManifest.xml')
        if ($LASTEXITCODE -ne 0) { throw "新式菜单注册失败: $LASTEXITCODE" }
        $packageOk = $true
    }
}

# ---------------------------------------------------------------- 3. 经典菜单项（可选）
if ($ClassicVerbs) {
    $cu = [Microsoft.Win32.Registry]::CurrentUser
    $verbs = @(
        @{ Key = '*\shell\DshBatchRename';                        Label = '批量重命名(&R)';                  Cmd = '"' + $exe + '" "%1"';           Multi = $true  },
        @{ Key = '*\shell\DshBatchRenameQuick';                   Label = '快速重命名（沿用上次规则）(&Q)';  Cmd = '"' + $exe + '" --quick "%1"';   Multi = $true  },
        @{ Key = 'Directory\shell\DshBatchRename';                Label = '批量重命名(&R)';                  Cmd = '"' + $exe + '" "%1"';           Multi = $true  },
        @{ Key = 'Directory\Background\shell\DshBatchRename';     Label = '批量重命名（本文件夹）(&R)';      Cmd = '"' + $exe + '" --folder "%V"';  Multi = $false },
        @{ Key = 'Directory\Background\shell\DshBatchRenameUndo'; Label = '撤销上次批量重命名(&U)';          Cmd = '"' + $exe + '" --undo';         Multi = $false }
    )
    foreach ($v in $verbs) {
        $full = 'Software\Classes\' + $v.Key
        $k = $cu.CreateSubKey($full)
        if ($null -eq $k) { throw "无法创建注册表项 HKCU\$full" }
        $k.SetValue('', $v.Label)
        $k.SetValue('Icon', $exe + ',0')
        if ($v.Multi) { $k.SetValue('MultiSelectModel', 'Player') }
        $ck = $k.CreateSubKey('command')
        $ck.SetValue('', $v.Cmd)
        $ck.Close()
        $k.Close()
        Write-Host ("  经典菜单项: " + $v.Key)
    }
}

# 通知资源管理器刷新关联（失败不影响安装结果）
try { & ie4uinit.exe -show 2>$null | Out-Null } catch { }
try { & ie4uinit.exe -ClearIconCache 2>$null | Out-Null } catch { }

Write-Host ''
Write-Host '安装完成。' -ForegroundColor Green
if ($packageOk) {
    Write-Host '  Win11 新式右键菜单（一级菜单，无需「显示更多选项」）:'
    Write-Host '    选中文件 → 右键 → 「批量重命名…」            打开带预览的窗口'
    Write-Host '    选中文件 → 右键 → 「快速重命名（沿用上次规则）」 真正一键'
    Write-Host '    选中文件夹 → 右键 → 同上两项'
    Write-Host '    文件夹空白处右键 → 「批量重命名（本文件夹）…」/「撤销上次批量重命名」'
    Write-Host ''
    Write-Host '  提示：新式菜单条目要重启一次资源管理器（或注销重登）才会出现。' -ForegroundColor DarkYellow
} else {
    Write-Host '  仅安装了经典菜单项（显示在「显示更多选项」里）。' -ForegroundColor DarkYellow
}
