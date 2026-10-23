<#
    NetPulse 发布脚本
    -----------------
    1. 编译（调用 build.ps1）
    2. 把 dist 里的 NetPulse.exe 连同 README / LICENSE / THIRD-PARTY 打成一个 zip
    产物：dist\NetPulse-<version>-win.zip

    用法：
        .\publish.ps1
        .\publish.ps1 -Version 3.0.1
#>
[CmdletBinding()]
param(
    [string]$Version = ''
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot

Get-Process NetPulse -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 200

& (Join-Path $root 'build.ps1')

if (-not $Version) {
    # 版本号只认一个源头：src\AssemblyInfo.cs
    $info = Get-Content (Join-Path $root 'src\AssemblyInfo.cs') -Encoding UTF8 -Raw
    $m = [regex]::Match($info, 'AssemblyInformationalVersion\("([^"]+)"\)')
    if (-not $m.Success) { throw 'AssemblyInfo.cs 里没找到 AssemblyInformationalVersion' }
    $Version = $m.Groups[1].Value
}

$stage = Join-Path $root 'dist\pkg'
if (Test-Path $stage) { Remove-Item $stage -Recurse -Force }
New-Item -ItemType Directory -Force -Path $stage | Out-Null

Copy-Item (Join-Path $root 'dist\NetPulse.exe') $stage
foreach ($doc in 'README.md', 'LICENSE', 'THIRD-PARTY.md', 'CHANGELOG.md') {
    $p = Join-Path $root $doc
    if (Test-Path $p) { Copy-Item $p $stage }
}

$zip = Join-Path $root ("dist\NetPulse-$Version-win.zip")
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force

$size = [math]::Round((Get-Item $zip).Length / 1KB, 1)
Write-Host "[publish] 打包完成 → $zip  ($size KB)" -ForegroundColor Green
