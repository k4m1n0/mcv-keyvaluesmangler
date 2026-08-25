param(
    [string]$BootName = 'WeaponDamageCalc.Boot',
    [string]$BootTfm = 'net8.0-windows',
    [string]$BootCsproj = '',
    [string]$BootDll = '',
    [switch]$Build,
    [switch]$NoUpdate
)
$ErrorActionPreference = 'Stop'
$sRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $BootCsproj) { $BootCsproj = Join-Path $sRoot 'BootAntheil\Boot.csproj' }
if (-not $BootDll) { $BootDll = Join-Path $sRoot "BootAntheil\bin\Release\$BootTfm\$BootName.dll" }

# 1) build BootAntheil
function Invoke-BootBuild {
    Write-Host "[build-boot] dotnet build BootAntheil"
    & dotnet build $BootCsproj -c Release -p:"BootName=$BootName" -p:"BootTfm=$BootTfm" -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet build Boot failed ($LASTEXITCODE)" }
    if (-not (Test-Path $BootDll)) { throw "BootAntheil.dll not found after build: $BootDll" }
}

# 2) extract method-body hashes (AD/X1/X3/X6, FNV-1a variant)
function Get-BootHashes {
    $sWork = Join-Path $env:TEMP 'boot_build_hash'
    New-Item -ItemType Directory -Force -Path $sWork | Out-Null
    $sCsproj = '<Project Sdk="Microsoft.NET.Sdk">' + "`n" +
    '  <PropertyGroup>' + "`n" +
    '    <OutputType>Exe</OutputType>' + "`n" +
    '    <TargetFramework>net8.0</TargetFramework>' + "`n" +
    '    <ImplicitUsings>enable</ImplicitUsings>' + "`n" +
    '  </PropertyGroup>' + "`n" +
    '</Project>'
    $sCsrc = 'using System.Reflection;' + "`n" +
    'static class MH {' + "`n" +
    '    const BindingFlags BF = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;' + "`n" +
    '    static uint Fnv(byte[] d, uint k0, uint k1) { uint h = k0; foreach (byte x in d) { h ^= x; h *= k1; } return h; }' + "`n" +
    '    static void Main(string[] args) {' + "`n" +
    '        var asm = Assembly.LoadFrom(args[0]);' + "`n" +
    '        var t = asm.GetType("A0.P");' + "`n" +
    '        uint k0 = (uint)t.GetField("uLK0A", BF).GetValue(null) ^ (uint)t.GetField("uLK0B", BF).GetValue(null);' + "`n" +
    '        uint k1 = (uint)t.GetField("uLK1A", BF).GetValue(null) ^ (uint)t.GetField("uLK1B", BF).GetValue(null);' + "`n" +
    '        foreach (string n in new[] { "AD", "X1", "X3", "X6" }) {' + "`n" +
    '            var mi = t.GetMethod(n, BF);' + "`n" +
    '            byte[] il = mi.GetMethodBody().GetILAsByteArray();' + "`n" +
    '            Console.WriteLine(n + ":0x" + Fnv(il, k0, k1).ToString("X8"));' + "`n" +
    '        }' + "`n" +
    '    }' + "`n" +
    '}'
    [System.IO.File]::WriteAllText((Join-Path $sWork 'bh.csproj'), $sCsproj)
    [System.IO.File]::WriteAllText((Join-Path $sWork 'Program.cs'), $sCsrc)

    $sOut = & dotnet run --project (Join-Path $sWork 'bh.csproj') -- $BootDll 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'hash extractor failed' }
    $hashes = @{}
    foreach ($sLine in $sOut) {
        if ($sLine -match '^(AD|X1|X3|X6):0x([0-9A-F]{8})$') { $hashes[$matches[1]] = $matches[2] }
    }
    foreach ($sM in @('AD', 'X1', 'X3', 'X6')) {
        if (-not $hashes.ContainsKey($sM)) { throw "hash not extracted for $sM" }
        Write-Host ("{0}: 0x{1}" -f $sM, $hashes[$sM])
    }
    return $hashes
}

# 3) patch SelfCheck expectations (uHs xor-pair, runtime-computed)
function Update-SelfCheck([hashtable]$hashes) {
    $src = Join-Path $sRoot 'BootAntheil\Program.cs'
    $sContent = [System.IO.File]::ReadAllText($src)
    $script:idx = 0
    $list = @('AD', 'X1', 'X3', 'X6')
    $pattern = '(MethodHash\([^)]*\) != )(\(uHs \^ 0x[0-9A-Fa-f]{8}u\))'
    $evaluator = {
        param($match)
        if ($script:idx -ge $list.Count) { return $match.Value }
        $uH = [Convert]::ToUInt64($hashes[$list[$script:idx]], 16)
        $uSeed = [int64]0x13579BDF
        $uR2 = ($uSeed -bxor ([int64]$uH)) -band 0xFFFFFFFFL
        $sNew = '(uHs ^ 0x{0:X8}u)' -f ([uint64]$uR2)
        $script:idx++
        return $match.Groups[1].Value + $sNew
    }
    $sPatched = [regex]::Replace($sContent, $pattern, $evaluator)
    if ($sPatched -ne $sContent) {
        [System.IO.File]::WriteAllText($src, $sPatched)
        return $true
    }
    return $false
}

if ($Build) { Invoke-BootBuild }

# 4) convergence loop: patch hash -> rebuild -> re-verify (max 3)
for ($iRound = 1; $iRound -le 3; $iRound++) {
    $hashes = Get-BootHashes
    if ($NoUpdate) { return }
    $bChanged = Update-SelfCheck $hashes
    if (-not $bChanged) {
        Write-Host 'hash verified (SelfCheck matches BootAntheil.dll)'
        return
    }
    if ($Build) {
        Write-Host "[build-boot] SelfCheck updated (round $iRound), rebuilding..."
        Invoke-BootBuild
    } else {
        Write-Host 'SelfCheck patched - rerun with -Build to rebuild and verify'
        return
    }
}
throw 'hash convergence failed after 3 rounds'
