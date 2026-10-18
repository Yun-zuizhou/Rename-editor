# 打包 Win11 右键菜单的 MSIX 稀疏包（未签名，签名在 Install.ps1 里做）
# 依赖：D:\Windows Kits\10 的 makeappx.exe
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$msixDir = Join-Path $root 'msix'
$stage = Join-Path $msixDir 'stage'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$kitsRoot = 'D:\Windows Kits\10\bin'
$sdkVer = Get-ChildItem $kitsRoot -Directory -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' -and (Test-Path (Join-Path $_.FullName 'x64\makeappx.exe')) } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $sdkVer) { throw '找不到 makeappx.exe' }
$makeappx = Join-Path $sdkVer.FullName 'x64\makeappx.exe'

# ---- 1. 生成资产图（44 / 150 / 50），从 256px 主图标缩放 ----
Add-Type -AssemblyName System.Drawing
$masterPath = Join-Path $root 'shell\preview-main.png'
$assets = Join-Path $stage 'Assets'
New-Item -ItemType Directory -Force -Path $assets | Out-Null
$map = @(
    @{ Name = 'Square44x44Logo.png';  Size = 44 },
    @{ Name = 'Square150x150Logo.png'; Size = 150 },
    @{ Name = 'StoreLogo.png';         Size = 50 }
)
if (Test-Path $masterPath) {
    # 有图标母版就重新生成
    $src = [System.Drawing.Image]::FromFile($masterPath)
    try {
        foreach ($m in $map) {
            $bmp = New-Object System.Drawing.Bitmap($m.Size, $m.Size)
            $g = [System.Drawing.Graphics]::FromImage($bmp)
            $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
            $g.Clear([System.Drawing.Color]::Transparent)
            $g.DrawImage($src, 0, 0, $m.Size, $m.Size)
            $g.Dispose()
            $bmp.Save((Join-Path $assets $m.Name), [System.Drawing.Imaging.ImageFormat]::Png)
            $bmp.Dispose()
            "asset $($m.Name) $($m.Size)px"
        }
    }
    finally { $src.Dispose() }
} else {
    # 没有母版就沿用仓库里已提交的资产图
    $missing = $map | Where-Object { -not (Test-Path (Join-Path $assets $_.Name)) }
    if ($missing) { throw "缺少 msix\stage\Assets 里的资产图，且没有图标母版 shell\preview-main.png（先跑 tools\MakeIcon.exe 生成母版）" }
    'asset 沿用已有资产图'
}

# ---- 2. 暂存清单 ----
Copy-Item (Join-Path $msixDir 'AppxManifest.xml') (Join-Path $stage 'AppxManifest.xml') -Force

# ---- 3. 打包 ----
$msix = Join-Path $dist 'DSHBatchRename.Shell.msix'
if (Test-Path $msix) { Remove-Item $msix -Force }
& $makeappx pack /o /nv /d $stage /p $msix
if ($LASTEXITCODE -ne 0) { throw "makeappx 失败: $LASTEXITCODE" }
"OK  $msix  $((Get-Item $msix).Length) bytes"
