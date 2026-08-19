param(
    [Parameter(Mandatory)][string]$AsmDir,
    [Parameter(Mandatory)][string]$OutDir
)
$ErrorActionPreference = 'Stop'
$AsmDir, $OutDir = $AsmDir, $OutDir | ForEach-Object { $_.Trim().TrimEnd('\', '"') }
$sAsm = Join-Path $AsmDir 'lamarr_stub.asm'
if (!(Test-Path $sAsm)) { throw "lamarr_stub.asm not found: $sAsm" }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$sMl64 = $null
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $sVs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null
    if ($sVs) {
        $sMl64 = Get-ChildItem (Join-Path $sVs 'VC\Tools\MSVC\*\bin\Hostx64\x64\ml64.exe') -EA SilentlyContinue |
            Sort-Object FullName -Desc | Select-Object -First 1 -Exp FullName #降序取最新
    }
}
if (!$sMl64 -and $env:VCToolsInstallDir) {
    $c = Join-Path $env:VCToolsInstallDir 'bin\Hostx64\x64\ml64.exe'
    if (Test-Path $c) { $sMl64 = $c }
}
if (!$sMl64) {
    $c = Get-Command ml64 -EA SilentlyContinue
    if ($c) { $sMl64 = $c.Source }
}
if (!$sMl64) {
    throw 'MSVC toolchain not found (need ml64/link). Install VS Build Tools or build from an "x64 Native Tools" prompt.'
}

$sLink = Join-Path (Split-Path $sMl64) 'link.exe'
if (!(Test-Path $sLink)) { throw "link.exe not found next to ml64: $sLink" }

Write-Host "[build-stub] ml64: $sMl64`n[build-stub] out : $OutDir"

$sObj, $sDll = Join-Path $OutDir 'lamarr_stub.obj', (Join-Path $OutDir 'lamarr_stub.dll')

& $sMl64 /nologo /c ("/Fo" + $sObj) $sAsm
if ($LASTEXITCODE -ne 0) { throw "ml64 failed ($LASTEXITCODE)" }
& $sLink /nologo /dll /noentry /machine:x64 /subsystem:console ("/out:" + $sDll) $sObj
if ($LASTEXITCODE -ne 0) { throw "link failed ($LASTEXITCODE)" }

Write-Host "[build-stub] done: $sDll"