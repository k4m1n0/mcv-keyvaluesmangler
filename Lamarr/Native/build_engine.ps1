param(
    [string]$OutDir = "$env:TEMP",
    [string]$Seed = '123456789ABCDEF0',
    [string]$IsaName = '',
    [string]$CarrierIsa = '',
    [string]$PackerTfm = '',
    [switch]$Prod,
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
$ROOT = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
if (!$PackerTfm) {
    $sPackerProj = Join-Path $PSScriptRoot '..\Packer\Packer.csproj'
    if (Test-Path $sPackerProj) { $PackerTfm = (get_msbuild_prop $sPackerProj @('PackerTfm')).PackerTfm }
    if (!$PackerTfm -and -not $Fresh) {
        $rgGen = @(Get-ChildItem -Path (Join-Path $PSScriptRoot '..\Packer\bin\Release') -Filter 'LamarrPacker.exe' -Recurse -File -ErrorAction SilentlyContinue)
        if ($rgGen.Count -eq 1) { $PackerTfm = Split-Path (Split-Path $rgGen[0].FullName -Parent) -Leaf }
    }
    if (!$PackerTfm) { throw 'PackerTfm unresolved: pass -PackerTfm or build Lamarr\Packer\Packer.csproj first' }
}
$GEN = Join-Path $ROOT "Lamarr\Packer\bin\Release\$PackerTfm\LamarrPacker.exe"
if (!(Test-Path $GEN)) { throw "generator not built: $GEN (run: dotnet build Lamarr\Packer\Packer.csproj -c Release -p:PackerTfm=$PackerTfm)" }

if ([string]::IsNullOrEmpty($IsaName)) {
    $IsaName = (& $GEN --pick-isa --seed $Seed --role real).Trim()
    if ($LASTEXITCODE -ne 0) { throw "pick-isa failed ($LASTEXITCODE)" }
    if ([string]::IsNullOrEmpty($IsaName)) { throw "pick-isa returned empty" }
    Write-Host "-- ISA: $IsaName (seed-derived)"
}

$VSWHERE = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$MSVC = & $VSWHERE -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null
$ML = Get-ChildItem (Join-Path $MSVC 'VC\Tools\MSVC\*\bin\Hostx64\x64\ml64.exe') -ErrorAction SilentlyContinue | Sort-Object FullName -Desc | Select-Object -First 1 -Exp FullName
if (!($ML -and (Test-Path $ML))) { throw 'ml64 not found (need VS Build Tools)' }
Write-Host ("-- Found ml64: " + $ML)
$LK = Join-Path (Split-Path $ML) 'link.exe'
Write-Host ("-- Found link: " + $LK)

$ASM = Join-Path $OutDir 'engine.asm'
$OBJ = Join-Path $OutDir 'engine.obj'
[uint64]$uTok = 2166136261
foreach ($b in [Text.Encoding]::ASCII.GetBytes("$Seed|engine")) { $uTok = (($uTok -bxor $b) * 16777619) % 4294967296 }
$LNK = Join-Path $OutDir ('t' + $uTok.ToString('x8') + '.lnk')
$DLL = Join-Path $OutDir 'engine.dll'

if ([string]::IsNullOrEmpty($CarrierIsa)) {
    # CarrierIsa must match the ISA used for the roll, both derive from the same seed
    & $GEN --emit-engine $ASM --seed $Seed | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "emit-engine failed ($LASTEXITCODE)" }
    $CarrierIsa = (& $GEN --carrier --seed $Seed).Trim()
    if ([string]::IsNullOrEmpty($CarrierIsa)) { throw 'carrier returned empty' }
} else {
    & $GEN --emit-engine $ASM --seed $Seed --isa $CarrierIsa | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "emit-engine failed ($LASTEXITCODE)" }
}
Write-Host ('-- Configuring done (packerTfm=' + $PackerTfm + ' isa=' + $IsaName + ' carrier=' + $CarrierIsa + ' fresh=' + $(if ($Fresh) { 'ON' } else { 'OFF' }) + ')')
if ($Configure) { exit 0 }
& $ML /nologo /c ("/Fo" + $OBJ) $ASM
if ($LASTEXITCODE -ne 0) { throw "ml64(engine.asm) failed ($LASTEXITCODE)" }
Write-Host '-- Doing link: engine.dll'
& $LK /nologo /dll /noentry /machine:x64 /subsystem:console '/export:PianolaRun,@1,NONAME' /emittoolversioninfo:no /Brepro /nodefaultlib ("/out:" + $LNK) $OBJ
if ($LASTEXITCODE -ne 0) { throw "link(engine image) failed ($LASTEXITCODE)" }
Move-Item $LNK $DLL -Force
Remove-Item $ASM, $OBJ, ($LNK + '.lib'), ($LNK + '.exp') -Force -ErrorAction SilentlyContinue
Write-Host ("-- Built engine: " + $DLL + " isa=" + $IsaName + " carrier=" + $CarrierIsa)