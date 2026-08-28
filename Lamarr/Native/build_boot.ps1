param(
    [string]$BootName = 'WeaponDamageCalc.Boot',
    [string]$BootTfm = 'net8.0-windows',
    [string]$BootCsproj = '',
    [string]$BootDll = '',
    [switch]$Build,
    [switch]$NoUpdate
)
$ErrorActionPreference = 'Stop'
$ROOT_DIR = Split-Path -Parent $MyInvocation.MyCommand.Path
if (-not $BootCsproj) { $BootCsproj = Join-Path $ROOT_DIR 'BootAntheil\Boot.csproj' }
if (-not $BootDll) { $BootDll = Join-Path $ROOT_DIR "BootAntheil\bin\Release\$BootTfm\$BootName.dll" }

function build_boot {
    Write-Host "[build-boot] STATUS dotnet build BootAntheil"
    & dotnet build $BootCsproj -c Release -p:"BootName=$BootName" -p:"BootTfm=$BootTfm" -v q | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "dotnet build Boot failed ($LASTEXITCODE)" }
    if (-not (Test-Path $BootDll)) { throw "BootAntheil.dll not found after build: $BootDll" }
}

function collect_method_hashes {
    $HASH_DIR = Join-Path $env:TEMP 'boot_build_hash'
    New-Item -ItemType Directory -Force -Path $HASH_DIR | Out-Null
    $HASH_CSPROJ = '<Project Sdk="Microsoft.NET.Sdk">' + "`n" +
    '  <PropertyGroup>' + "`n" +
    '    <OutputType>Exe</OutputType>' + "`n" +
    '    <TargetFramework>net8.0</TargetFramework>' + "`n" +
    '    <ImplicitUsings>enable</ImplicitUsings>' + "`n" +
    '  </PropertyGroup>' + "`n" +
    '</Project>'
    $HASH_SRC = 'using System.Reflection;' + "`n" +
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
    [System.IO.File]::WriteAllText((Join-Path $HASH_DIR 'bh.csproj'), $HASH_CSPROJ)
    [System.IO.File]::WriteAllText((Join-Path $HASH_DIR 'Program.cs'), $HASH_SRC)

    $HASH_OUT = & dotnet run --project (Join-Path $HASH_DIR 'bh.csproj') -- $BootDll 2>$null
    if ($LASTEXITCODE -ne 0) { throw 'hash extractor failed' }
    $HASH_MAP = @{}
    foreach ($LINE in $HASH_OUT) {
        if ($LINE -match '^(AD|X1|X3|X6):0x([0-9A-F]{8})$') { $HASH_MAP[$matches[1]] = $matches[2] }
    }
    foreach ($NAME in @('AD', 'X1', 'X3', 'X6')) {
        if (-not $HASH_MAP.ContainsKey($NAME)) { throw "hash not extracted for $NAME" }
        Write-Host ("{0}: 0x{1}" -f $NAME, $HASH_MAP[$NAME])
    }
    return $HASH_MAP
}

function patch_selfcheck([hashtable]$HASH_MAP) {
    $SRC_FILE = Join-Path $ROOT_DIR 'BootAntheil\Program.cs'
    $CONTENT = [System.IO.File]::ReadAllText($SRC_FILE)
    $script:idx = 0
    $list = @('AD', 'X1', 'X3', 'X6', 'AD', 'X1', 'X3', 'X6')
    $pattern = '(MethodHash\([^)]*\) != )(\(uHs \^ 0x[0-9A-Fa-f]{8}u\))'
    $evaluator = {
        param($match)
        if ($script:idx -ge $list.Count) { return $match.Value }
        $uH = [Convert]::ToUInt64($HASH_MAP[$list[$script:idx]], 16)
        $uSeed = [int64]0x13579BDF
        $uR2 = ($uSeed -bxor ([int64]$uH)) -band 0xFFFFFFFFL
        $NEW = '(uHs ^ 0x{0:X8}u)' -f ([uint64]$uR2)
        $script:idx++
        return $match.Groups[1].Value + $NEW
    }
    $PATCHED = [regex]::Replace($CONTENT, $pattern, $evaluator)
    if ($PATCHED -ne $CONTENT) {
        [System.IO.File]::WriteAllText($SRC_FILE, $PATCHED)
        return $true
    }
    return $false
}

if ($Build) { build_boot }

for ($iRound = 1; $iRound -le 3; $iRound++) {
    $HASH_MAP = collect_method_hashes
    if ($NoUpdate) { return }
    $bChanged = patch_selfcheck $HASH_MAP
    if (-not $bChanged) {
        Write-Host 'hash verified (SelfCheck matches BootAntheil.dll)'
        return
    }
    if ($Build) {
        Write-Host "[build-boot] STATUS SelfCheck updated (round $iRound), rebuilding..."
        build_boot
    } else {
        Write-Host 'SelfCheck patched - rerun with -Build to rebuild and verify'
        return
    }
}
throw 'hash convergence failed after 3 rounds'
