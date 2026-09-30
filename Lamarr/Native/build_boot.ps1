param(
    [string]$BootName = '',
    [string]$BootTfm = '',
    [string]$BootCsproj = '',
    [string]$BootDll = '',
    [switch]$Build,
    [switch]$TriageKnobs,
    [switch]$SkipResidue,
    [string]$Win32Res = '',
    [switch]$Configure,
    [switch]$Fresh
)
$ErrorActionPreference = 'Stop'
function get_msbuild_prop([string]$sProj, [string[]]$rgName) {
    $rgArg = @()
    foreach ($sName in (@($rgName) + 'MSBuildProjectName')) { $rgArg += ('-getProperty:' + $sName) }
    $sJson = (& dotnet msbuild $sProj @rgArg '-nologo' | Out-String)
    if ($LASTEXITCODE -ne 0) { throw ('msbuild -getProperty failed: ' + $sProj) }
    try { return (ConvertFrom-Json $sJson).Properties } catch { throw ('msbuild -getProperty returned unexpected output: ' + $sJson) }
}
function get_payload_project([string]$sSearchRoot) {
    $rgProj = @(Get-ChildItem -Path $sSearchRoot -Filter '*.csproj' -File | Where-Object { (Get-Content $_.FullName -Raw) -match 'PayloadPack\.targets' })
    if ($rgProj.Count -ne 1) { throw ('payload project unresolved under ' + $sSearchRoot) }
    return $rgProj[0].FullName
}
$ROOT_DIR = $PSScriptRoot
if (-not $BootCsproj) { $BootCsproj = Join-Path $ROOT_DIR 'BootAntheil\Boot.csproj' }
if ((!$BootTfm -or !$BootName) -and -not $Fresh) {
    $rgBootDll = @(Get-ChildItem -Path (Join-Path $PSScriptRoot 'BootAntheil\bin\Release') -Filter '*.dll' -Recurse -File -ErrorAction SilentlyContinue)
    if ($rgBootDll.Count -eq 1) {
        if (!$BootTfm) { $BootTfm = Split-Path (Split-Path $rgBootDll[0].FullName -Parent) -Leaf }
        if (!$BootName) { $BootName = [IO.Path]::GetFileNameWithoutExtension($rgBootDll[0].Name) }
    }
}
if (!$BootTfm -or !$BootName) {
    $sPayload = get_payload_project (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
    $oProp = get_msbuild_prop $sPayload @('AssemblyName', 'TargetFramework')
    if (!$BootName) { $BootName = $oProp.AssemblyName + '.Boot' }
    if (!$BootTfm) { $BootTfm = $oProp.TargetFramework }
}
if (!$BootTfm -or !$BootName) { throw 'BootName/BootTfm unresolved: pass them explicitly' }
if (-not $BootDll) { $BootDll = Join-Path $ROOT_DIR "BootAntheil\bin\Release\$BootTfm\$BootName.dll" }

function build_boot {
    Write-Host "-- Doing build: $BootCsproj ($BootTfm)"
    $rgArgs = @($BootCsproj, '-c', 'Release', "-p:BootName=$BootName", "-p:BootTfm=$BootTfm", '-p:EnableRuntimePackDownload=false', '-v', 'q')
    if ($Win32Res) { $rgArgs += ('-p:Win32Resource=' + $Win32Res) }
    if ($TriageKnobs) { $rgArgs += '-p:LamarrTriageKnobs=1' }
    & dotnet build @rgArgs | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet build Boot failed ($LASTEXITCODE)" }
    if (-not (Test-Path $BootDll)) { throw "boot dll not found after build: $BootDll" }
}

Write-Host ('-- Configuring done (boot=' + $BootName + ' tfm=' + $BootTfm + ' fresh=' + $(if ($Fresh) { 'ON' } else { 'OFF' }) + ')')
if ($Configure) { exit 0 }
if ($Build) { build_boot; Write-Host ("-- Built boot: " + $BootDll) }

if (-not $SkipResidue -and (Test-Path $BootDll)) {
    & (Join-Path $PSScriptRoot 'pe.ps1') -Residue -Path $BootDll -Quiet
    if ($LASTEXITCODE -ne 0) { throw "residue gate FAILED on $BootDll (see scan output); use -SkipResidue to override" }
}