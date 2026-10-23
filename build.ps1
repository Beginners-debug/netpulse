<#
    NetPulse 构建脚本
    -----------------
    优先使用已安装的 .NET SDK（dotnet build src\NetPulse.csproj）；
    没有 SDK 时退回仓库内置的 Roslyn 编译器（tools\roslyn\pkg\tasks\net472\csc.exe），
    直接调用 csc 编译单个 exe，不需要任何 SDK。

    用法：
        .\build.ps1              编译到 dist\NetPulse.exe
        .\build.ps1 -Run         编译完顺手启动
#>
[CmdletBinding()]
param(
    [switch]$Run
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$out = Join-Path $root 'dist\NetPulse.exe'
$src = Join-Path $root 'src'

if (-not (Test-Path $src)) { throw "找不到源码目录：$src" }

New-Item -ItemType Directory -Force -Path (Split-Path $out) | Out-Null

# ---------- 1) 首选 dotnet SDK ----------
$dotnet = Get-Command dotnet -ErrorAction SilentlyContinue
if ($dotnet -and (Test-Path (Join-Path $root 'src\NetPulse.csproj'))) {
    Write-Host '[build] 使用 .NET SDK 编译…' -ForegroundColor Cyan
    & $dotnet.Source build (Join-Path $root 'src\NetPulse.csproj') -c Release -o (Split-Path $out) --nologo -v m
    if ($LASTEXITCODE -ne 0) { throw "dotnet build 失败（$LASTEXITCODE）" }
}
else {
    # ---------- 2) 退回内置 Roslyn ----------
    $csc = Join-Path $root 'tools\roslyn\pkg\tasks\net472\csc.exe'
    if (-not (Test-Path $csc)) {
        throw "没有 .NET SDK，也没找到内置编译器 $csc`n请安装 .NET SDK：winget install Microsoft.DotNet.SDK.8"
    }

    $refRoot = Join-Path $env:USERPROFILE '.nuget\packages\microsoft.netframework.referenceassemblies.net48'
    $ref = Get-ChildItem $refRoot -Directory -ErrorAction SilentlyContinue |
           Sort-Object Name -Descending |
           ForEach-Object { Join-Path $_.FullName 'build\.NETFramework\v4.8' } |
           Where-Object { Test-Path $_ } |
           Select-Object -First 1
    if (-not $ref) { throw "找不到 .NET Framework 4.8 引用程序集（$refRoot）" }

    $sources = Get-ChildItem $src -Filter *.cs -Recurse | ForEach-Object { $_.FullName }

    $cscArgs = @(
        '/nologo', '/target:winexe', '/platform:anycpu', '/langversion:9', '/optimize+', '/debug-',
        '/deterministic', '/nowarn:1701,1702,1591',
        ('/win32icon:' + (Join-Path $root 'assets\netpulse.ico')),
        ('/resource:' + (Join-Path $root 'assets\digitfont.ttf') + ',NetPulse.digitfont.ttf'),
        ('/resource:' + (Join-Path $root 'assets\cjkfont.ttf') + ',NetPulse.cjkfont.ttf'),
        ('/out:' + $out),
        ('/r:' + (Join-Path $ref 'mscorlib.dll')),
        ('/r:' + (Join-Path $ref 'System.dll')),
        ('/r:' + (Join-Path $ref 'System.Core.dll')),
        ('/r:' + (Join-Path $ref 'System.Drawing.dll')),
        ('/r:' + (Join-Path $ref 'System.Windows.Forms.dll')),
        ('/r:' + (Join-Path $ref 'System.Xml.dll'))
    ) + $sources

    Write-Host '[build] 使用内置 Roslyn 编译…' -ForegroundColor Cyan
    & $csc $cscArgs
    if ($LASTEXITCODE -ne 0) { throw "csc 编译失败（$LASTEXITCODE）" }
}

$size = [math]::Round((Get-Item $out).Length / 1KB, 1)
Write-Host "[build] 完成 → $out  ($size KB)" -ForegroundColor Green

if ($Run) {
    Get-Process NetPulse -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 200
    Start-Process $out
    Write-Host '[build] 已启动' -ForegroundColor Green
}
