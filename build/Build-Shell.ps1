# 编译 Win11 右键菜单扩展 DLL（x64）
# 依赖：VS 2026 的 MSVC + D:\Windows Kits\10 的 Windows SDK
# 直接设置 INCLUDE/LIB 环境变量后调用 cl.exe，不经过 vcvars（规避 cmd 引号问题）
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot       # build\ 的上一层
$src = Join-Path $root 'src\Shell'
$objDir = Join-Path $root 'out\shell'
$releaseDir = Join-Path $root 'release'
New-Item -ItemType Directory -Force -Path $objDir, $releaseDir | Out-Null

$vsRoot = 'C:\Program Files\Microsoft Visual Studio\18\Community'
$msvc = Get-ChildItem (Join-Path $vsRoot 'VC\Tools\MSVC') -Directory |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$cl = Join-Path $msvc.FullName 'bin\Hostx64\x64\cl.exe'
if (-not (Test-Path $cl)) { throw "找不到 cl.exe: $cl" }

$sdk = 'D:\Windows Kits\10'
$sdkVer = Get-ChildItem (Join-Path $sdk 'Include') -Directory |
    Where-Object { $_.Name -match '^\d+\.\d+\.\d+\.\d+$' -and (Test-Path (Join-Path $_.FullName 'um\shobjidl_core.h')) } |
    Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (-not $sdkVer) { throw "找不到可用的 Windows SDK 头文件" }
$v = $sdkVer.Name
$rc = "$sdk\bin\$v\x64\rc.exe"
if (-not (Test-Path $rc)) { throw "找不到 rc.exe: $rc" }

$env:INCLUDE = @(
    (Join-Path $msvc.FullName 'include'),
    "$sdk\Include\$v\ucrt",
    "$sdk\Include\$v\um",
    "$sdk\Include\$v\shared",
    "$sdk\Include\$v\winrt"
) -join ';'
$env:LIB = @(
    (Join-Path $msvc.FullName 'lib\x64'),
    "$sdk\Lib\$v\ucrt\x64",
    "$sdk\Lib\$v\um\x64"
) -join ';'
$env:PATH = (@(
    (Join-Path $msvc.FullName 'bin\Hostx64\x64'),
    "$sdk\bin\$v\x64"
) -join ';') + ';' + $env:PATH

$resFile = Join-Path $objDir 'resources.res'   # 中间产物统一丢到 out\，别污染 src\Shell
$dll = Join-Path $releaseDir 'DSHBRShell.dll'
if (Test-Path $dll) { Remove-Item $dll -Force }

"SDK $v | MSVC $($msvc.Name)"
& $rc /nologo /fo $resFile (Join-Path $src 'resources.rc')
if ($LASTEXITCODE -ne 0) { throw "rc 编译失败: $LASTEXITCODE" }

Push-Location $src
try {
    & $cl /nologo /LD /MT /O2 /EHsc /std:c++17 /utf-8 /DUNICODE /D_UNICODE /W3 `
        "/Fo$objDir\\" ShellCommand.cpp $resFile /link /DLL /EXPORT:DllGetClassObject /EXPORT:DllCanUnloadNow "/OUT:$dll" "/IMPLIB:$objDir\ShellCommand.lib" `
        shlwapi.lib ole32.lib oleaut32.lib shell32.lib user32.lib uuid.lib
    if ($LASTEXITCODE -ne 0) { throw "cl 编译失败: $LASTEXITCODE" }
}
finally { Pop-Location }

if (-not (Test-Path $dll)) { throw "未生成 $dll" }
"OK  $dll  $((Get-Item $dll).Length) bytes"
