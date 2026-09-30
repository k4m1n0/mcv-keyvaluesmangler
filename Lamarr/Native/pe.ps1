# everything that reads a PE image, the modes combine and exit 0 only when every one passed
param(
    [string[]]$Path = @(),
    [switch]$ClearCet,
    [switch]$Recurse,
    [switch]$Sections,
    [string]$Expect = '.text,.rdata',
    [string]$Subsystem = 'Any',
    [switch]$Residue,
    [switch]$AsmAscii,
    [string]$Root = '',
    [string[]]$Token = @('__VM_PASSTHROUGH', 'pianola', 'notenrolle', 'Pianola', 'Notenrolle'),
    # opt in, .NET Release artifacts carry a Debug Directory too
    [switch]$CheckPeDebugDir,
    [switch]$CheckPeFingerprint,
    [switch]$Quiet
)
$ErrorActionPreference = 'Stop'
$bFail = $false
if (!$ClearCet -and !$Sections -and !$Residue) { throw 'pe.ps1: pick a mode (-ClearCet / -Sections / -Residue)' }
$RepoRoot = Split-Path -Parent $PSScriptRoot
if (!$Root) { $Root = $RepoRoot }

function rd_u16 { param($d, $o) return ([int]$d[$o]) -bor (([int]$d[$o + 1]) -shl 8) }
function rd_u32 { param($d, $o) return ([int]$d[$o]) -bor (([int]$d[$o + 1]) -shl 8) -bor (([int]$d[$o + 2]) -shl 16) -bor (([int]$d[$o + 3]) -shl 24) }

# a CET_COMPAT process fails to start without CET support, which aborts the pack
function clear_cet_bit_one([string]$sFile) {
    if (!(Test-Path $sFile)) { return }
    $rg = [IO.File]::ReadAllBytes($sFile)
    if ($rg.Length -lt 512) { return }
    if ([BitConverter]::ToUInt16($rg, 0) -ne 0x5A4D) { return }
    $iPe = [BitConverter]::ToInt32($rg, 0x3C)
    if ($iPe -le 0 -or $iPe + 24 -ge $rg.Length) { return }
    if ([BitConverter]::ToUInt32($rg, $iPe) -ne 0x4550) { return }
    $nSec = [BitConverter]::ToUInt16($rg, $iPe + 6)
    $iOptSz = [BitConverter]::ToUInt16($rg, $iPe + 20)
    $iOpt = $iPe + 24
    $bPe32p = ([BitConverter]::ToUInt16($rg, $iOpt) -eq 0x20B)
    $iDd = $iOpt + $(if ($bPe32p) { 112 } else { 96 })
    $iDbgRva = [BitConverter]::ToUInt32($rg, $iDd + 48)
    $iDbgSz = [BitConverter]::ToUInt32($rg, $iDd + 52)
    if ($iDbgRva -eq 0 -or $iDbgSz -eq 0) { return }
    $iSec = $iOpt + $iOptSz
    $iOff = -1
    for ($i = 0; $i -lt $nSec; $i++) {
        $iS = $iSec + $i * 40
        if ($iS + 40 -gt $rg.Length) { break }
        $uVa = [BitConverter]::ToUInt32($rg, $iS + 12)
        $uRs = [BitConverter]::ToUInt32($rg, $iS + 16)
        $uRaw = [BitConverter]::ToUInt32($rg, $iS + 20)
        if ($iDbgRva -ge $uVa -and $iDbgRva -lt ($uVa + [Math]::Max($uRs, 1))) { $iOff = $uRaw + ($iDbgRva - $uVa); break }
    }
    if ($iOff -lt 0 -or $iOff + $iDbgSz -gt $rg.Length) { return }
    for ($k = 0; $k -lt [int]($iDbgSz / 28); $k++) {
        $iE = $iOff + $k * 28
        if ($iE + 28 -gt $rg.Length) { break }
        if ([BitConverter]::ToUInt32($rg, $iE + 12) -ne 20) { continue }
        $iPtr = [BitConverter]::ToUInt32($rg, $iE + 24)
        if ($iPtr + 2 -gt $rg.Length) { return }
        $uVal = [BitConverter]::ToUInt16($rg, $iPtr)
        if (($uVal -band 1) -eq 0) { return }
        $uNew = $uVal -band (-bnot 1)
        $rg[$iPtr] = [byte]($uNew -band 0xFF)
        $rg[$iPtr + 1] = [byte](($uNew -shr 8) -band 0xFF)
        [IO.File]::WriteAllBytes($sFile, $rg)
        if (!$Quiet) { Write-Host ("-- Cleared CET bit: " + $sFile) }
        return
    }
}

# antheil artifacts carry the shell .text and the bundle .rdata, native ones .stub and .lamapp
function test_pe_sections([string]$sPath) {
    $VSWHERE = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
    if (!(Test-Path $VSWHERE)) { throw "vswhere not found: $VSWHERE" }
    $MSVC_ROOT = & $VSWHERE -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null
    if (!$MSVC_ROOT) { throw 'no MSVC installation found' }
    $DUMPBIN = Get-ChildItem (Join-Path $MSVC_ROOT 'VC\Tools\MSVC\*\bin\Hostx64\x64\dumpbin.exe') -EA SilentlyContinue | Select-Object -First 1
    if (!$DUMPBIN) { throw "dumpbin not found under $MSVC_ROOT" }
    if (!(Test-Path $sPath)) { throw "artifact not found: $sPath" }
    $rgOut = @(& $DUMPBIN.FullName /headers $sPath 2>&1)
    $rgSection = @()
    $sSubsystem = ''
    $bInSection = $false
    foreach ($sLine in $rgOut) {
        if ($sLine -match '^SECTION HEADER') { $bInSection = $true; continue }
        if ($bInSection -and $sLine -match '^\s*([^ ]+)\s+name\s*$') { $rgSection += $Matches[1]; $bInSection = $false; continue }
        if ($sLine -match 'subsystem\s+\((.*)\)') { $sSubsystem = $Matches[1]; continue }
    }
    $rgWant = @($script:Expect.Split(',') | ForEach-Object { $_.Trim() } | Where-Object { $_.Length -gt 0 })
    $rgBad = @()
    if ($rgSection.Count -ne $rgWant.Count) { $rgBad += ('section count {0} != {1}' -f $rgSection.Count, $rgWant.Count) }
    foreach ($sWant in $rgWant) { if ($rgSection -notcontains $sWant) { $rgBad += ('section missing: ' + $sWant) } }
    if ($script:Subsystem -ne 'Any' -and $sSubsystem -notmatch $script:Subsystem) { $rgBad += ('subsystem {0} != {1}' -f $sSubsystem, $script:Subsystem) }
    if (!$Quiet) {
        Write-Host ('[pe] {0}' -f $sPath)
        Write-Host ('[pe] sections={0} subsystem={1}' -f ($rgSection -join ','), $sSubsystem)
    }
    foreach ($s in $rgBad) { Write-Host ('[pe] FAIL  ' + $s) }
    if ($rgBad.Count -gt 0) { Write-Host '[pe] sections: FAIL'; return $false }
    if (!$Quiet) { Write-Host '[pe] sections: PASS' }
    return $true
}

function test_token_bytes {
    param([byte[]]$Data, [string]$Tok)
    $t = [System.Text.Encoding]::ASCII.GetBytes($Tok)
    $lim = $Data.Length - $t.Length
    if ($lim -lt 0) { return -1 }
    for ($i = 0; $i -le $lim; $i++) {
        $ok = $true
        for ($j = 0; $j -lt $t.Length; $j++) { if ($Data[$i + $j] -ne $t[$j]) { $ok = $false; break } }
        if ($ok) { return $i }
    }
    return -1
}

function has_pe_debug_directory {
    param([byte[]]$d)
    if ($d.Length -lt 0x80) { return $false }
    if ($d[0] -ne 0x4D -or $d[1] -ne 0x5A) { return $false }
    $pe = rd_u32 -d $d -o 0x3C
    if ($pe + 0x18 -ge $d.Length) { return $false }
    if ($d[$pe] -ne 0x50 -or $d[$pe + 1] -ne 0x45) { return $false }
    $opt = $pe + 24
    $magic = rd_u16 -d $d -o $opt
    $ddOff = if ($magic -eq 0x20B) { $opt + 112 } else { $opt + 96 }
    $entry = $ddOff + 6 * 8
    if ($entry + 8 -gt $d.Length) { return $false }
    $size = rd_u32 -d $d -o ($entry + 4)
    return $size -gt 0
}

function has_pe_rich {
    param([byte[]]$d)
    if ($d.Length -lt 0x80) { return $false }
    if ($d[0] -ne 0x4D -or $d[1] -ne 0x5A) { return $false }
    $pe = rd_u32 -d $d -o 0x3C
    if ($pe -le 0 -or $pe + 0x18 -gt $d.Length) { return $false }
    for ($i = 0x40; $i -le $pe - 4; $i++) {
        if ($d[$i] -eq 0x52 -and $d[$i + 1] -eq 0x69 -and $d[$i + 2] -eq 0x63 -and $d[$i + 3] -eq 0x68) { return $true }
    }
    return $false
}

function get_pe_timestamp {
    param([byte[]]$d)
    if ($d.Length -lt 0x80) { return 0 }
    if ($d[0] -ne 0x4D -or $d[1] -ne 0x5A) { return 0 }
    $pe = rd_u32 -d $d -o 0x3C
    if ($pe -le 0 -or $pe + 8 -gt $d.Length) { return 0 }
    $v = rd_u32 -d $d -o ($pe + 8)
    if ($v -lt 0) { $v = $v + 4294967296 }
    return $v
}

function test_residue([string[]]$rgPath) {
    $any = $false
    foreach ($p in $rgPath) {
        $full = (Resolve-Path $p).Path
        $rgB = [System.IO.File]::ReadAllBytes($full)
        $hits = @()
        if ($CheckPeDebugDir -and (has_pe_debug_directory -d $rgB)) { $hits += 'PE debug directory present (-CheckPeDebugDir)' }
        if ($CheckPeFingerprint) {
            if (has_pe_rich -d $rgB) { $hits += 'MSVC Rich header present' }
            $ts = get_pe_timestamp -d $rgB
            $now = [DateTimeOffset]::UtcNow.ToUnixTimeSeconds()
            if ($ts -ne 0 -and $ts -ge ($now - 86400) -and $ts -le ($now + 86400)) { $hits += ("PE time stamp looks like a real build time ({0})" -f $ts) }
        }
        foreach ($tk in $Token) {
            $at = test_token_bytes -Data $rgB -Tok $tk
            if ($at -ge 0) { $hits += ("token {0} @0x{1:X}" -f $tk, $at) }
        }
        if ($hits.Count -gt 0) {
            $any = $true
            Write-Output "[pe] residue FAIL  $full"
            foreach ($h in $hits) { Write-Output "    -> $h" }
        }
        elseif (!$Quiet) { Write-Output "[pe] residue CLEAN $full" }
    }
    return (-not $any)
}

# a stray code page byte in a source string would be assembled as is and ship wrong bytes
function test_asm_ascii([string]$sRoot) {
    $bad = 0
    foreach ($f in (Get-ChildItem -Path $sRoot -Recurse -File -Include *.asm, *.ps1 |
            Where-Object { $_.FullName -notmatch '\\obj\\|\\bin\\' })) {
        $b = [System.IO.File]::ReadAllBytes($f.FullName)
        for ($i = 0; $i -lt $b.Length; $i++) {
            if ($b[$i] -gt 0x7F) {
                Write-Output ("[pe] FAIL  {0} non ascii at offset {1}" -f $f.FullName, $i)
                $bad++
                break
            }
        }
    }
    if ($bad -gt 0) { Write-Output '[pe] ascii: FAIL'; return $false }
    if (!$Quiet) { Write-Output '[pe] ascii: clean (asm/ps1)' }
    return $true
}

if ($ClearCet) {
    if ($Path.Count -eq 0) { throw '-ClearCet needs -Path' }
    foreach ($p in $Path) {
        if ($Recurse -and (Test-Path $p -PathType Container)) {
            foreach ($f in (Get-ChildItem $p -Recurse -File -Include *.exe, *.dll -EA SilentlyContinue)) { clear_cet_bit_one $f.FullName }
        }
        else { clear_cet_bit_one $p }
    }
}
if ($Sections) {
    if ($Path.Count -eq 0) { throw '-Sections needs -Path' }
    foreach ($p in $Path) { if (-not (test_pe_sections $p)) { $bFail = $true } }
}
if ($Residue) {
    if ($Path.Count -gt 0) { if (-not (test_residue $Path)) { $bFail = $true } }
    if ($AsmAscii) { if (-not (test_asm_ascii $Root)) { $bFail = $true } }
    elseif ($Path.Count -eq 0) { throw '-Residue needs -Path (or -AsmAscii)' }
}
if ($bFail) { exit 1 }
exit 0
