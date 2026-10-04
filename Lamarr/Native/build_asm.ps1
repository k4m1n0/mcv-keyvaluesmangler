param(
    [string]$AsmDir = '',
    [Parameter(Mandatory)][string]$OutDir,
    [ValidateSet('antheil','native')][string]$Packer = 'antheil',
    [switch]$Pack,
    [string]$PublishDir,
    [string]$InputPath,
    [string]$Output,
    [string]$Boot,
    [string]$Shim,
    [string]$Decoder,
    [string]$JitHook,
    [string]$Pheropod,
    [string]$Engine,
    [string]$Mode,
    [string]$AppName = '',
    [string]$PackerTfm = '',
    [switch]$Configure,
    [switch]$Fresh,
    [string]$EncryptDeps = '',
    [string]$NoCompressDeps = '',
    [string]$MethodDict = '',
    [string]$RtcVersion = '',
    [string]$PayloadMajor = '',
    [string]$RtcRollForward = '',
    [string]$RtcTiered = '',
    [string]$Escrow = '',
    [string]$EscrowPubKey = '',
    [string]$EscrowKey = '',
    [string]$Key = '',
    [string]$KeyFile = '',
    [string]$SeedHex = '',
    [string]$Replay = '',
    [switch]$EscrowDiag,
    # dev-only code between ;%triage:begin and ;%triage:end, stripped from release builds
    [switch]$TriageKnobs,
    [switch]$SkipAsm,
    [switch]$NoMorph,
    [switch]$SlotMorph,
    [switch]$ForceAsm,
    [string]$SlotSeed = '5EED0001',
    [switch]$Pristine,
    [switch]$SkipResidue
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
if (-not $TriageKnobs -and $env:LAMARR_TRIAGE_KNOBS -eq '1') { $TriageKnobs = $true }
$ASM_DIR = $AsmDir.Trim().TrimEnd('\', '"')
$OUT_DIR = $OutDir.Trim().TrimEnd('\', '"')
if (!$ASM_DIR) { $ASM_DIR = $PSScriptRoot }
New-Item -ItemType Directory -Force -Path $OUT_DIR | Out-Null
$RC_EXE = $null
foreach ($d in @(Get-ChildItem (Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin') -Directory -ErrorAction SilentlyContinue | Sort-Object Name -Descending)) {
    $sRc = Join-Path $d.FullName 'x64\rc.exe'
    if (Test-Path $sRc) { $RC_EXE = $sRc; break }
}
if (!$RC_EXE) { $c = Get-Command rc.exe -ErrorAction SilentlyContinue; if ($c) { $RC_EXE = $c.Source } }
$EMPTY_RES = Join-Path $OUT_DIR 'empty.res'
if ($RC_EXE) {
    $sEmptyRc = Join-Path $OUT_DIR 'empty.rc'
    Set-Content -NoNewline -Encoding Ascii -Path $sEmptyRc -Value ''
    & $RC_EXE /nologo /fo $EMPTY_RES $sEmptyRc | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "rc(empty.res) failed ($LASTEXITCODE)" }
}

$bKeyArgGiven = [bool]$Key
if ($KeyFile -and $bKeyArgGiven) { throw 'build_asm.ps1: -Key and -KeyFile are mutually exclusive (pass exactly one)' }
$bKeyFallback = $false
if ($KeyFile -and -not $Key) {
    $rgKeyLn = @(Get-Content -Path $KeyFile -ErrorAction SilentlyContinue)
    $Key = @($rgKeyLn | Where-Object { $_ -match '^\s*[0-9A-Fa-f]{64}\s*$' })[0]
    if (-not $Key) {
        $mKey = [regex]::Match(($rgKeyLn -join "`n"), '(?i)(?<![0-9A-F])[0-9A-F]{64}(?![0-9A-F])')
        if ($mKey.Success) { $Key = $mKey.Value }
    }
    if (-not $Key) { throw ('build_asm.ps1: -KeyFile has no 64-hex key (LAMARR-KEY tagged or bare): ' + $KeyFile) }
    $Key = $Key.Trim()
}
if ($Key -and $SeedHex) { throw 'build_asm.ps1: -Key/-KeyFile and -SeedHex are mutually exclusive (K = identity/sealing, S = shape only)' }
if ($Replay -and $Key) { throw 'build_asm.ps1: -Replay must not be combined with -Key/-KeyFile (a reproduction involves no K)' }
if ($SeedHex -and $SeedHex -notmatch '^[0-9A-Fa-f]{16}$') { throw 'build_asm.ps1: -SeedHex must be 16 hex chars (the escrow vmSeed)' }
if (-not $SeedHex -and ($Replay -or $Key)) {
    if ($Replay -and -not $EscrowKey) { throw 'build_asm.ps1: -Replay needs -EscrowKey <escrow private key file> (see Lamarr_VM_Design_M1.md section 16)' }
    if (!$PackerTfm) {
        $sProjPk = Join-Path $PSScriptRoot '..\Packer\Packer.csproj'
        if (Test-Path $sProjPk) { $PackerTfm = (get_msbuild_prop $sProjPk @('PackerTfm')).PackerTfm }
        if (!$PackerTfm) {
            $rgGenPk = @(Get-ChildItem -Path (Join-Path $PSScriptRoot '..\Packer\bin\Release') -Filter 'LamarrPacker.exe' -Recurse -File -ErrorAction SilentlyContinue)
            if ($rgGenPk.Count -eq 1) { $PackerTfm = Split-Path (Split-Path $rgGenPk[0].FullName -Parent) -Leaf }
        }
        if (!$PackerTfm) { throw 'shape seed: packer TFM unresolved, pass -PackerTfm <tfm>' }
    }
    $sGenPk = Join-Path $PSScriptRoot "..\Packer\bin\Release\$PackerTfm\LamarrPacker.exe"
    if (!(Test-Path $sGenPk)) { throw ('shape seed: packer not built: ' + $sGenPk) }
    if ($Replay) {
        $sSeedRaw = ''
        for ($iTry = 0; $iTry -lt 3 -and [string]::IsNullOrEmpty($sSeedRaw); $iTry++) {
            $sSeedRaw = (& $sGenPk '--internal' 'shape' '--of-escrow' $Replay '--escrow-priv' $EscrowKey 2>$null | Out-String)
            if ($LASTEXITCODE -ne 0) { $sSeedRaw = ''; Start-Sleep -Milliseconds 400 }
        }
        $rgSeedLn = @(($sSeedRaw -split "`r?`n") | Where-Object { $_ -and -not $_.TrimStart().StartsWith('[') })
        $SeedHex = ''
        if ($rgSeedLn.Count -gt 0) { $SeedHex = $rgSeedLn[0].Trim() }
        if ([string]::IsNullOrEmpty($SeedHex)) { throw ('-Replay: replay-seed failed for ' + $Replay) }
        if ($SeedHex -notmatch '^[0-9A-Fa-f]{16}$') { throw ('-Replay: packer output is not a clean 16-hex seed line (got ''{0}''): see Lamarr_VM_Design_M1.md section 17.7 example 4' -f $SeedHex) }
        Write-Host "-- Replay: shape seed S=$SeedHex extracted from the escrow (K not used anywhere)"
    }
    else {
        $sSeedRaw = ''
        for ($iTry = 0; $iTry -lt 3 -and [string]::IsNullOrEmpty($sSeedRaw); $iTry++) {
            $sSeedRaw = (& $sGenPk '--internal' 'shape' '--of-key' $Key 2>$null | Out-String)
            if ($LASTEXITCODE -ne 0) { $sSeedRaw = ''; Start-Sleep -Milliseconds 400 }
        }
        $rgSeedLn = @(($sSeedRaw -split "`r?`n") | Where-Object { $_ -and -not $_.TrimStart().StartsWith('[') })
        $SeedHex = ''
        if ($rgSeedLn.Count -gt 0) { $SeedHex = $rgSeedLn[0].Trim() }
        if ([string]::IsNullOrEmpty($SeedHex)) { throw 'seed-of-key failed' }
        if ($SeedHex -notmatch '^[0-9A-Fa-f]{16}$') { throw ('shape seed from K is not a clean 16-hex line (got ''{0}''): see Lamarr_VM_Design_M1.md section 17.7 example 4' -f $SeedHex) }
        Write-Host "-- Shape seed S=$SeedHex derived from K (the same string the replay side reads out of the escrow)"
    }
}
if ($SeedHex) { $ShapeTok = $SeedHex }
else { $bKeyFallback = $true; $Key = '5EED0001000000005EED0001000000005EED0001000000005EED000100000000'; $ShapeTok = $Key; Write-Host "-- Key/shape seed not given: the fixed non-secret value is used for the morph/name *shape* only"; Write-Host "--   -> --key is NOT passed, so the packer generates a random K (not reproducible; with -Escrow, replayable only via that escrow)" }
$MORPH_BASE = [int64]7
foreach ($ch in $ShapeTok.ToCharArray()) { $MORPH_BASE = (($MORPH_BASE * 131) + [int][char]$ch) % 2147483647 }
function morph_seed([string]$tag) {
    $h = $MORPH_BASE
    foreach ($ch in $tag.ToCharArray()) { $h = (($h * 131) + [int][char]$ch) % 2147483647 }
    return [int]($h + 1)
}

# names must derive from the seed or two runs differ byte for byte
function seed_token([string]$tag) {
    [uint64]$h = 2166136261
    foreach ($b in [Text.Encoding]::ASCII.GetBytes("$ShapeTok|$tag")) { $h = (($h -bxor $b) * 16777619) % 4294967296 }
    return $h.ToString('x8')
}

# seed free token, used when a target is built pristine so its bytes never depend on the run seed
function fixed_token([string]$tag) {
    [uint64]$h = 2166136261
    foreach ($b in [Text.Encoding]::ASCII.GetBytes($tag)) { $h = (($h -bxor $b) * 16777619) % 4294967296 }
    return $h.ToString('x8')
}

# newest match of a wildcard path, the MSVC and SDK layouts carry a version component
function find_latest([string]$sGlob) {
    return Get-ChildItem $sGlob -EA SilentlyContinue | Sort-Object FullName -Desc | Select-Object -First 1 -Exp FullName
}

# anchor syntax in asm sources: ;%poly:opseq ;%poly:alt ;%poly:end ;%poly:junk
function invoke_morph {
    param(
        [Parameter(Mandatory)][string]$AsmPath,
        [Parameter(Mandatory)][string]$OutPath,
        [int]$Seed = -1
    )
    if ($Seed -lt 0) { $Seed = 0x5EED0001 }
    $rng = [System.Random]::new($Seed)
    Write-Host "-- Doing morph: seed=$Seed out=$OutPath"
    $JUNK_POOL = @(
        'nop'
        'mov r8, r8'
        'mov r9, r9'
        'mov r10, r10'
        'xchg r11, r11'
        'lea r12, [r12*1+0]'
        'lea r13, [r13*1+0]'
        'lea r14, [r14*1+0]'
        'db 66h,0Fh,1Fh,44h,00h,00h'
        'db 0Fh,1Fh,44h,00h,00h'
        'db 0Fh,1Fh,84h,00h,00h,00h,00h,00h'
        'db 66h,2Eh,0Fh,1Fh,84h,00h,00h,00h,00h,00h'
    )
    $lines = @(Get-Content -Path $AsmPath -Encoding ASCII)
    # the slot directory is located by this tag comment, deleting it silently breaks slot regeneration
    $bSlotDir = $false
    $bSlotTag = $false
    foreach ($ln in $lines) {
        if ($ln -match 'z0_slot_dir\s*:') { $bSlotDir = $true }
        if ($ln.Trim() -eq '; ##5A17C0DE##') { $bSlotTag = $true }
    }
    if ($bSlotDir -and -not $bSlotTag) { throw ($AsmPath + ' carries a slot directory without its ##5A17C0DE## tag line') }
    $out = New-Object System.Collections.Generic.List[string]
    $i = 0
    while ($i -lt $lines.Count) {
        $t = $lines[$i].Trim()
        if ($t.StartsWith(';%poly:')) {
            if ($t -eq ';%poly:junk') {
                $nJunk = $rng.Next(0, 6)
                for ($j = 0; $j -lt $nJunk; $j++) { $out.Add('    ' + $JUNK_POOL[$rng.Next(0, $JUNK_POOL.Count)]) }
                $i++
                while ($i -lt $lines.Count) {
                    $tl = $lines[$i].Trim()
                    if ($tl -eq ';%poly:end') { $i++; break }
                    $out.Add($lines[$i]); $i++
                }
                continue
            }
            $i++
            $cur = New-Object System.Collections.Generic.List[string]
            $variants = New-Object System.Collections.Generic.List[object]
            while ($i -lt $lines.Count) {
                $tl = $lines[$i].Trim()
                if ($tl -eq ';%poly:end') { $variants.Add($cur); $i++; break }
                if ($tl -eq ';%poly:alt') { $variants.Add($cur); $cur = New-Object System.Collections.Generic.List[string]; $i++; continue }
                $cur.Add($lines[$i]); $i++
            }
            if ($variants.Count -eq 0) { throw 'morph: empty variant block' }
            $iPick = $rng.Next(0, $variants.Count)
            if ($env:LAMARR_MORPH_TRACE -eq '1') { Write-Host ("-- Morph block@" + $i + " pick=" + $iPick + "/" + $variants.Count) }
            $sel = $variants[$iPick]
            foreach ($sl in $sel) { $out.Add($sl) }
            continue
        }
        $out.Add($lines[$i])
        $i++
    }
    Set-Content -Path $OutPath -Value $out.ToArray() -Encoding ASCII
    Write-Host ("-- Wrote " + $OutPath + " lines=" + $out.Count)
}

$ML64_EXE = $null
$VSWHERE = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $VSWHERE) {
    $MSVC_ROOT = & $VSWHERE -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null
    if ($MSVC_ROOT) {
        $ML64_EXE = find_latest (Join-Path $MSVC_ROOT 'VC\Tools\MSVC\*\bin\Hostx64\x64\ml64.exe')
    }
}
if (!$ML64_EXE -and $env:VCToolsInstallDir) {
    $c = Join-Path $env:VCToolsInstallDir 'bin\Hostx64\x64\ml64.exe'
    if (Test-Path $c) { $ML64_EXE = $c }
}
if (!$ML64_EXE) {
    $c = Get-Command ml64 -EA SilentlyContinue
    if ($c) { $ML64_EXE = $c.Source }
}
if (!$ML64_EXE) {
    throw 'MSVC toolchain not found (need ml64/link). Install VS Build Tools or build from an "x64 Native Tools" prompt.'
}

$LINK_EXE = Join-Path (Split-Path $ML64_EXE) 'link.exe'

if ($Pack -or $Configure) {
    if (!$PackerTfm) {
        $sPackerProj = Join-Path $PSScriptRoot '..\Packer\Packer.csproj'
        if (Test-Path $sPackerProj) { $PackerTfm = (get_msbuild_prop $sPackerProj @('PackerTfm')).PackerTfm }
        if (!$PackerTfm -and -not $Fresh) {
            $rgGen = @(Get-ChildItem -Path (Join-Path $PSScriptRoot '..\Packer\bin\Release') -Filter 'LamarrPacker.exe' -Recurse -File -ErrorAction SilentlyContinue)
            if ($rgGen.Count -eq 1) { $PackerTfm = Split-Path (Split-Path $rgGen[0].FullName -Parent) -Leaf }
        }
        if (!$PackerTfm) { throw 'PackerTfm unresolved: pass -PackerTfm or build Lamarr\Packer\Packer.csproj first' }
    }
    if (!$AppName) {
        if ($InputPath) { $AppName = [IO.Path]::GetFileNameWithoutExtension($InputPath) }
        elseif ($PublishDir -and -not $Fresh) {
            $rgExe = @(Get-ChildItem -Path $PublishDir -Filter '*.exe' -File -ErrorAction SilentlyContinue | Where-Object { $_.Name -notlike '*_packed.exe' })
            if ($rgExe.Count -eq 1) { $AppName = [IO.Path]::GetFileNameWithoutExtension($rgExe[0].Name) }
        }
        if (!$AppName) {
            $sPayload = get_payload_project (Split-Path -Parent (Split-Path -Parent $PSScriptRoot))
            $AppName = (get_msbuild_prop $sPayload @('AssemblyName')).AssemblyName
        }
        if (!$AppName) { throw 'AppName unresolved: pass -AppName or -InputPath' }
    }
    Write-Host ('-- Configuring done (app=' + $AppName + ' packerTfm=' + $PackerTfm + ' fresh=' + $(if ($Fresh) { 'ON' } else { 'OFF' }) + ')')
    if ($Configure) {
        $sRepoRoot = Split-Path -Parent (Split-Path -Parent $PSScriptRoot)
        $sPayloadProj = get_payload_project $sRepoRoot
        $sCfgArgs = @('--workdir', $sRepoRoot, '--configure', '--target', $sPayloadProj, '--mode', $Packer)
        if ($Fresh) { $sCfgArgs += '--fresh' }
        & (Join-Path $PSScriptRoot "..\Packer\bin\Release\$PackerTfm\LamarrPacker.exe") @sCfgArgs
        exit $LASTEXITCODE
    }
}
if (!(Test-Path $LINK_EXE)) { throw "link.exe not found next to ml64: $LINK_EXE" }

$K32_LIB = $null
$KITS_ROOT = "${env:ProgramFiles(x86)}\Windows Kits\10\Lib"
if (Test-Path $KITS_ROOT) {
    $K32_LIB = find_latest (Join-Path $KITS_ROOT '*\um\x64\kernel32.lib')
}
if (!$K32_LIB) { throw 'kernel32.lib not found (Windows SDK needed)' }

Write-Host "-- Found ml64: $ML64_EXE`n-- OutDir: $OUT_DIR"

function build_stub_dll([string]$AsmName, [string]$DllName, [switch]$Page, [string]$Exports, [string]$RenameExport, [switch]$RenameExports, [switch]$Nodefaultlib, [string[]]$ExportRaw = @(), [string]$TempTag = '', [switch]$Variant, [string]$Res = '', [switch]$Pristine)
{
    $ASM_PATH = Join-Path $ASM_DIR $AsmName
    if (!(Test-Path $ASM_PATH)) { throw "asm not found: $ASM_PATH" }
    $sBase = $DllName -replace '\.dll$', ''
    $OBJ_FILE = Join-Path $OUT_DIR ($sBase + '.obj')
    $DLL_FILE = Join-Path $OUT_DIR $DllName
    $rgTmp = @()
    $SRC_FILE = $ASM_PATH
    $bPristine = ($NoMorph -or $Packer -ne 'antheil' -or $Pristine)
    if ($bPristine -and $Variant) { Write-Host "-- pristine target: no per-seed morph, seed-free output name" }
    if ($Variant -and -not $bPristine) {
        $SRC_FILE = Join-Path $OUT_DIR ($sBase + '_var.asm')
        invoke_morph -AsmPath $ASM_PATH -OutPath $SRC_FILE -Seed (morph_seed $DllName)
    }
    elseif ($Variant) {
        Write-Host "-- Doing link: $DllName (pristine sources, no per-seed variant)"
    }
    $LINK_EXPORT = 'Iamdec'
    $RENAME_LIST = @()
    if ($RenameExport) { $RENAME_LIST = @($RenameExport) }
    elseif ($Exports) { $RENAME_LIST = @($Exports.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_ }) }
    $TXT = Get-Content -Raw $SRC_FILE
    if ($RenameExport -or ($Exports -and $RenameExports)) {
        $NEW_LIST = @()
        foreach ($sym in $RENAME_LIST) {
            $NEW = 'x' + (seed_token("rename:$sym"))
            $TXT = $TXT -replace [regex]::Escape($sym), $NEW
            if ($sym -eq $RenameExport) { $LINK_EXPORT = $NEW }
            $NEW_LIST += $NEW
        }
        $RENAME_LIST = $NEW_LIST
    }
    if ($TriageKnobs) {
        $TXT = [regex]::Replace($TXT, '(?m)^;%triage:(begin|end)[ \t]*\r?\n', '')
    }
    else {
        $TXT = [regex]::Replace($TXT, '(?ms)^;%triage:begin[ \t]*\r?\n.*?^;%triage:end[ \t]*\r?\n', '')
    }
    $REL_SRC = Join-Path $OUT_DIR ($sBase + '_rel.asm')
    Set-Content -NoNewline -Encoding Ascii -Path $REL_SRC -Value $TXT
    if ($SRC_FILE -ne $ASM_PATH) { $rgTmp += $SRC_FILE }
    $rgTmp += $REL_SRC
    $SRC_FILE = $REL_SRC
    & $ML64_EXE /nologo /c ("/Fo" + $OBJ_FILE) $SRC_FILE
    if ($LASTEXITCODE -ne 0) { throw "ml64($AsmName) failed ($LASTEXITCODE)" }
    # no PE timestamp, two links of one seed match byte for byte
    $LINK_ARGS = [System.Collections.Generic.List[string]]@('/nologo', '/dll', '/noentry', '/machine:x64',
        '/subsystem:console', '/emittoolversioninfo:no', '/Brepro')
    if ($Page) { $LINK_ARGS.Add('/export:' + $LINK_EXPORT) }
    if ($Page -or $Nodefaultlib) { $LINK_ARGS.Add('/nodefaultlib') }
    if ($Exports) {
        # ordinals follow the export list, the caller picks the compressor by ordinal
        for ($i = 0; $i -lt $RENAME_LIST.Count; $i++) { $LINK_ARGS.Add('/export:' + $RENAME_LIST[$i] + ',@' + ($i + 1)) }
    }
    foreach ($e in $ExportRaw) { $LINK_ARGS.Add('/export:' + $e) }
    # /Brepro folds the output name into the digest, so the temp tag is fixed per target
    $sTag = if ($TempTag) { $TempTag } else { "dll:$DllName" }
    # pristine targets must not depend on the run seed, so their output name stays seed free too
    $TMP_TOKEN = if ($bPristine) { fixed_token $sTag } else { seed_token $sTag }
    $RAND_DLL = Join-Path $OUT_DIR ('t' + $TMP_TOKEN + '.dll')
    $LINK_ARGS.Add('/out:' + $RAND_DLL)
    if ($Res) { $LINK_ARGS.Add($Res) }
    $LINK_ARGS.Add($OBJ_FILE)
    if ($Page) { $LINK_ARGS.Add($K32_LIB) }
    & $LINK_EXE $LINK_ARGS.ToArray()
    if ($LASTEXITCODE -ne 0) { throw "link($DllName) failed ($LASTEXITCODE)" }
    Move-Item -Force $RAND_DLL $DLL_FILE
    foreach ($f in $rgTmp) { Remove-Item $f -Force -ErrorAction SilentlyContinue }
    Write-Host ("-- Built " + $DllName + ": " + $DLL_FILE)
}


if (-not $SkipAsm)
{
build_stub_dll 'lamarr_stub.asm' 'lamarr_stub.dll' -Variant -Pristine
if ($Packer -eq 'antheil')
{
    build_stub_dll 'antheil_stub.asm' 'antheil_stub.dll' -Variant
    build_stub_dll 'antheil_paged.asm' 'Iamdec.dll' -Page -RenameExport Iamdec -Variant
    build_stub_dll 'antlion_deco.asm' 'z0.dll' -Exports 'z0_init,z0_read,z0_align,z0_size,z0_deflate' -RenameExports -Variant
    # @1 returns the handover table, @2 arms the hook, names must match what the boot decodes
    build_stub_dll 'jithook.asm' 'jithook.dll' -ExportRaw @('JitHookTable,@1,NONAME', 'InstallJitHook,@2,NONAME') -Nodefaultlib -TempTag 'jit' -Variant
}
}


if ($Pack)
{
    $ErrorActionPreference = 'Continue'
    $PACKER_EXE = Join-Path $PSScriptRoot "..\Packer\bin\Release\$PackerTfm\LamarrPacker.exe"
    if (!(Test-Path $PACKER_EXE)) { throw "packer not found: $PACKER_EXE (build it with -p:PackerTfm=$PackerTfm or pass -PackerTfm matching the TFM it was built for)" }

    $bPackTri = $false
    $sVerTxt = (& $PACKER_EXE '--version' 2>$null | Out-String)
    if ($LASTEXITCODE -eq 0) {
        foreach ($sLn in ($sVerTxt -split "`n")) { if ($sLn -match '^\s*triage\s*:\s*([01])') { $bPackTri = ($Matches[1] -eq '1') } }
        if ($TriageKnobs -and -not $bPackTri) {
            throw 'packer is a RELEASE build (triage=0) but -TriageKnobs was requested: the triage knobs would be silently ignored. Rebuild with -p:LamarrAntheil=1 -p:LamarrTriageKnobs=1 (see Lamarr_VM_Design_M1.md section 18.7-2)'
        }
        if (-not $TriageKnobs -and $bPackTri) {
            Write-Host '[triage] WARNING the packer is a TRIAGE build (triage=1): this artifact is not a release-shaped artifact'
        }
    }
    else { Write-Host ('[triage] WARNING cannot read the packer triage state (--version rc=' + $LASTEXITCODE + '), skipping the pairing check') }

    if (!$SkipAsm -and !$ForceAsm) {
        $rgOut = @('lamarr_stub.dll', 'antheil_stub.dll', 'Iamdec.dll', 'z0.dll', 'jithook.dll') |
            ForEach-Object { Join-Path $OUT_DIR $_ } | Where-Object { Test-Path $_ }
        if ($rgOut.Count -gt 0) {
            $sNewest = (Get-ChildItem -Path $ASM_DIR -Filter '*.asm' -File | Sort-Object LastWriteTime -Descending | Select-Object -First 1).LastWriteTime
            $sOldest = ($rgOut | ForEach-Object { (Get-Item $_).LastWriteTime } | Sort-Object | Select-Object -First 1)
            if ($sOldest -gt $sNewest) { $SkipAsm = $true; Write-Host '-- Reusing the existing asm output, pass -ForceAsm to rebuild' }
        }
    }

    if ($SlotMorph) {
        # equal length slot rewrite through the packer, same implementation the pack path uses
        foreach ($sDll in @('Iamdec.dll', 'jithook.dll', 'lamarr_stub.dll', 'antheil_stub.dll')) {
            $sPath = Join-Path $OUT_DIR $sDll
            if (!(Test-Path $sPath)) { continue }
            Write-Host ("-- Slot morph: " + $sDll)
            if ($SeedHex) { & $PACKER_EXE '--internal' 'slot-morph' $sPath '--shape' $ShapeTok '--tag' $sDll '--check' }
            else { & $PACKER_EXE '--internal' 'slot-morph' $sPath '--of-key' $Key '--tag' $sDll '--check' }
            if ($LASTEXITCODE -ne 0) { throw ("slot morph failed on " + $sDll) }
        }
    }

    $STUB_DLL = Join-Path $OUT_DIR ($(if ($Packer -eq 'antheil') { 'antheil_stub.dll' } else { 'lamarr_stub.dll' }))
    if (!$InputPath -and !$PublishDir) { throw '-InputPath (or -PublishDir) required when -Pack is used' }
    if (!$InputPath) { $InputPath = Join-Path $PublishDir "$AppName.exe" }
    if (!$Output) {
        if ($PublishDir) { $Output = Join-Path $PublishDir "${AppName}_packed.exe" }
        else { throw '-Output required when -Pack is used without -PublishDir' }
    }

    $PACK_ARGS = @($(if ($Replay) { '--replay' } else { '--pack' }), '--stub', $STUB_DLL, '--input', $InputPath, '--output', $Output)
    if ($EncryptDeps) { $PACK_ARGS += @('--encrypt-deps', $EncryptDeps) }
    if (!$MethodDict) { $m = Join-Path $ASM_DIR 'boot_method_names.txt'; if (Test-Path $m) { $MethodDict = $m } }
    Write-Host "-- Method dict: $MethodDict"
    if ($MethodDict) { $PACK_ARGS += @('--method-dict', $MethodDict) }
    if ($NoCompressDeps) { $PACK_ARGS += @('--no-compress-deps', $NoCompressDeps) }
    if ($Boot)    { $PACK_ARGS += @('--boot', $Boot) }
    if ($Shim)    { $PACK_ARGS += @('--shim', $Shim) }
    if ($Decoder) { $PACK_ARGS += @('--decoder', $Decoder) }
    if ($JitHook) { $PACK_ARGS += @('--jithook', $JitHook) }
    if ($Pheropod) { $PACK_ARGS += @('--pheropod', $Pheropod) }
    if ($Engine) { $PACK_ARGS += @('--engine', $Engine) }
    if ($Replay) { $PACK_ARGS += @('--escrow', $Replay); if ($EscrowKey) { $PACK_ARGS += @('--escrow-priv', $EscrowKey) } }
    elseif ($KeyFile) { $PACK_ARGS += @('--key-file', $KeyFile) }
    elseif ($Key -and -not $bKeyFallback) { $PACK_ARGS += @('--key', $Key) }
    if ($Mode)    { $PACK_ARGS += @('--mode', $Mode) }
    if ($RtcVersion) { $PACK_ARGS += @('--rtc-version', $RtcVersion) }
    if ($PayloadMajor) { $PACK_ARGS += @('--payload-major', $PayloadMajor) }
    if ($RtcRollForward) { $PACK_ARGS += @('--rtc-rollforward', $RtcRollForward) }
    if ($RtcTiered) { $PACK_ARGS += @('--rtc-tiered', $RtcTiered) }
    if ($Escrow) { if ($Replay) { $PACK_ARGS += @('--escrow-out', $Escrow) } else { $PACK_ARGS += @('--escrow', $Escrow) } }
    if ($EscrowPubKey) { $PACK_ARGS += @('--escrow-pub', $EscrowPubKey) }
    if ($EscrowDiag) { $PACK_ARGS += @('--diag') }

    $RETRY_LOG = Join-Path $env:TEMP 'lamarr_pack_retry.log'
    $EXIT_CODE = 1
    # retry the packer, its output goes to %TEMP%\lamarr_pack_retry.log
    # MSBuild Exec force me
    for ($i = 0; $i -lt 3; $i++) {
        & $PACKER_EXE @PACK_ARGS *> $RETRY_LOG
        $EXIT_CODE = $LASTEXITCODE
        if ($EXIT_CODE -eq 0) { Write-Host "-- Packed: $Output"; break }
        Write-Host "-- Packer exit=$EXIT_CODE (retry $($i+1)/3)"
        if (Test-Path $RETRY_LOG) { Get-Content $RETRY_LOG | Select-Object -Last 6 }
        Start-Sleep -Seconds 1
    }
    if ($EXIT_CODE -ne 0) { exit 1 }

    Write-Host "-- Generating done ($AppName)"
    if (-not $SkipResidue -and (Test-Path $Output)) {
        & (Join-Path $PSScriptRoot 'pe.ps1') -Residue -Path $Output -CheckPeFingerprint -CheckPeDebugDir -AsmAscii -Quiet
        if ($LASTEXITCODE -ne 0) { throw "residue gate FAILED on $Output (see pe.ps1 output); use -SkipResidue to override" }
    }
}
