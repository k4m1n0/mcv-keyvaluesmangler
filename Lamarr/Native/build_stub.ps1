param(
    [Parameter(Mandatory = $true)][string]$AsmDir,
    [Parameter(Mandatory = $true)][string]$OutDir
)
$ErrorActionPreference = 'Stop'
$OutDir = $OutDir.Trim().TrimEnd('\', '"')
$AsmDir = $AsmDir.Trim().TrimEnd('\', '"')
$sAsm = Join-Path $AsmDir 'lamarr_stub.asm'
if (!(Test-Path $sAsm)) { throw "lamarr_stub.asm not found: $sAsm" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$sMl64 = $null

$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $sVs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null
    if ($sVs) {
        $c = Get-ChildItem (Join-Path $sVs 'VC\Tools\MSVC\*\bin\Hostx64\x64\ml64.exe') -ErrorAction SilentlyContinue |
            Sort-Object FullName -Descending | Select-Object -First 1
        if ($c) { $sMl64 = $c.FullName }
    }
}
# 2) VS developer environment (when building from "x64 Native Tools" prompt)
if (!$sMl64 -and $env:VCToolsInstallDir) {
    $c = Join-Path $env:VCToolsInstallDir 'bin\Hostx64\x64\ml64.exe'
    if (Test-Path $c) { $sMl64 = $c }
}
if (!$sMl64) {
    $c = Get-Command ml64 -ErrorAction SilentlyContinue
    if ($c) { $sMl64 = $c.Source }
}
if (!$sMl64) {
    throw 'MSVC toolchain not found (need ml64/link). Install VS Build Tools or build from an "x64 Native Tools" prompt.'
}
$sBin = Split-Path $sMl64
$sLink = Join-Path $sBin 'link.exe'
if (!(Test-Path $sLink)) { throw "link.exe not found next to ml64: $sLink" }

Write-Host "[build-stub] ml64: $sMl64"
Write-Host "[build-stub] out : $OutDir"

$sObj = Join-Path $OutDir 'lamarr_stub.obj'
$sDll = Join-Path $OutDir 'lamarr_stub.dll'

& $sMl64 /nologo /c ("/Fo" + $sObj) $sAsm
if ($LASTEXITCODE -ne 0) { throw "ml64 failed ($LASTEXITCODE)" }

& $sLink /nologo /dll /noentry /machine:x64 /subsystem:console ("/out:" + $sDll) $sObj
if ($LASTEXITCODE -ne 0) { throw "link failed ($LASTEXITCODE)" }

Write-Host "[build-stub] done: $sDll"
