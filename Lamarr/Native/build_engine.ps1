param(
    [string]$OutDir = "$env:TEMP",
    [string]$Key = '',
    [string]$KeyFile = '',
    [string]$SeedHex = '',
    [string]$Replay = '',
    [string]$EscrowKey = '',
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

if ($KeyFile -and -not $Key) {
    $rgKeyLn = @(Get-Content -Path $KeyFile -ErrorAction SilentlyContinue)
    $Key = @($rgKeyLn | Where-Object { $_ -match '^\s*[0-9A-Fa-f]{64}\s*$' })[0]
    if (-not $Key) {
        $mKey = [regex]::Match(($rgKeyLn -join "`n"), '(?i)(?<![0-9A-F])[0-9A-F]{64}(?![0-9A-F])')
        if ($mKey.Success) { $Key = $mKey.Value }
    }
    if (-not $Key) { throw ('build_engine.ps1: -KeyFile has no 64-hex key (LAMARR-KEY tagged or bare): ' + $KeyFile) }
    $Key = $Key.Trim()
}
if ($Key -and ($SeedHex -or $Replay)) { throw 'build_engine.ps1: -Key/-KeyFile and -SeedHex/-Replay are mutually exclusive (K = identity/sealing, S = shape only)' }
if ($SeedHex -and $SeedHex -notmatch '^[0-9A-Fa-f]{16}$') { throw 'build_engine.ps1: -SeedHex must be 16 hex chars (the escrow vmSeed)' }
function invoke_packer([string]$sExe, [string[]]$rgArg) {
    for ($i = 0; $i -lt 3; $i++) {
        $s = (& $sExe @rgArg 2>$null | Out-String)
        if ($LASTEXITCODE -eq 0) { return $s }
        Write-Host ('[retry] ' + $sExe + ' rc=' + $LASTEXITCODE + ' attempt=' + ($i + 1) + '/3 (transient lock?)')
        Start-Sleep -Milliseconds 400
    }
    return ''
}
if (-not $SeedHex) {
    if ($Replay) {
        if (!$EscrowKey) { throw 'build_engine.ps1: -Replay needs -EscrowKey <escrow private key file> (see Lamarr_VM_Design_M1.md section 16)' }
        $sSeedRaw = invoke_packer $GEN @('--internal', 'shape', '--of-escrow', $Replay, '--escrow-priv', $EscrowKey)
        $rgSeedLn = @(($sSeedRaw -split "`r?`n") | Where-Object { $_ -and -not $_.TrimStart().StartsWith('[') })
        $SeedHex = ''
        if ($rgSeedLn.Count -gt 0) { $SeedHex = $rgSeedLn[0].Trim() }
        if ([string]::IsNullOrEmpty($SeedHex)) { throw ('replay-seed failed for ' + $Replay) }
        Write-Host "-- Replay: shape seed S=$SeedHex extracted from the escrow (K not used)"
    }
    elseif ($Key) {
        $sSeedRaw = invoke_packer $GEN @('--internal', 'shape', '--of-key', $Key)
        $rgSeedLn = @(($sSeedRaw -split "`r?`n") | Where-Object { $_ -and -not $_.TrimStart().StartsWith('[') })
        $SeedHex = ''
        if ($rgSeedLn.Count -gt 0) { $SeedHex = $rgSeedLn[0].Trim() }
        if ([string]::IsNullOrEmpty($SeedHex)) { throw 'seed-of-key failed' }
        Write-Host "-- Shape seed S=$SeedHex derived from K (the same string the replay side reads out of the escrow)"
    }
}
$SHAPE_ARG = @('--shape', $SeedHex)
function get_verb_value([string[]]$rgOut) {
    $rg = @((($rgOut -join "`n") -split "`r?`n") | Where-Object { $_ -and $_.Trim().Length -gt 0 -and -not $_.TrimStart().StartsWith('[') })
    if ($rg.Count -eq 0) { return '' }
    return $rg[0].Trim()
}
if ([string]::IsNullOrEmpty($IsaName)) {
    if ([string]::IsNullOrEmpty($SeedHex)) { throw 'build_engine.ps1 needs -Key <64hex>/-KeyFile <file> (a build) or -SeedHex <16hex>/-Replay <escrow> (shape only)' }
    $IsaName = get_verb_value (invoke_packer $GEN (@('--internal', 'pick-isa') + $SHAPE_ARG + @('--role', 'real')))
    if ([string]::IsNullOrEmpty($IsaName)) { throw 'pick-isa returned empty' }
    Write-Host "-- ISA: $IsaName (shape-derived)"
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
$ShapeTok = $SeedHex
[uint64]$uTok = 2166136261
foreach ($b in [Text.Encoding]::ASCII.GetBytes("$ShapeTok|engine")) { $uTok = (($uTok -bxor $b) * 16777619) % 4294967296 }
$LNK = Join-Path $OutDir ('t' + $uTok.ToString('x8') + '.lnk')
$DLL = Join-Path $OutDir 'engine.dll'

if ([string]::IsNullOrEmpty($CarrierIsa)) {
    Remove-Item $ASM -Force -ErrorAction SilentlyContinue
    $null = invoke_packer $GEN (@('--internal', 'emit-engine', '--out', $ASM) + $SHAPE_ARG)
    if (-not (Test-Path $ASM)) { throw "emit-engine produced no output ($ASM)" }
    $CarrierIsa = get_verb_value (invoke_packer $GEN (@('--internal', 'carrier') + $SHAPE_ARG))
    if ([string]::IsNullOrEmpty($CarrierIsa)) { throw 'carrier returned empty' }
} else {
    Remove-Item $ASM -Force -ErrorAction SilentlyContinue
    $null = invoke_packer $GEN (@('--internal', 'emit-engine', '--out', $ASM) + $SHAPE_ARG + @('--isa', $CarrierIsa))
    if (-not (Test-Path $ASM)) { throw "emit-engine produced no output ($ASM)" }
}
Write-Host ('-- Configuring done (packerTfm=' + $PackerTfm + ' isa=' + $IsaName + ' carrier=' + $CarrierIsa + ' fresh=' + $(if ($Fresh) { 'ON' } else { 'OFF' }) + ')')
if ($Configure) { exit 0 }
& $ML /nologo /c ("/Fo" + $OBJ) $ASM 2>$null
if ($LASTEXITCODE -ne 0) { throw "ml64(engine.asm) failed ($LASTEXITCODE)" }
Write-Host '-- Doing link: engine.dll'
& $LK /nologo /dll /noentry /machine:x64 /subsystem:console '/export:PianolaRun,@1,NONAME' /emittoolversioninfo:no /Brepro /nodefaultlib ("/out:" + $LNK) $OBJ 2>$null
if ($LASTEXITCODE -ne 0) { throw "link(engine image) failed ($LASTEXITCODE)" }
Move-Item $LNK $DLL -Force
Remove-Item $ASM, $OBJ, ($LNK + '.lib'), ($LNK + '.exp') -Force -ErrorAction SilentlyContinue
Write-Host ("-- Built engine: " + $DLL + " isa=" + $IsaName + " carrier=" + $CarrierIsa)