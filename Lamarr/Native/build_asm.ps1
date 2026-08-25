# build_asm.ps1 - unified assembly build + optional pack (replaces build_stub/build_jithook/pack_retry)
param(
    [Parameter(Mandatory)][string]$AsmDir,
    [Parameter(Mandatory)][string]$OutDir,
    [ValidateSet('antheil','native')][string]$Packer = 'antheil',
    [switch]$Pack,
    [string]$PublishDir,
    [string]$InputPath,
    [string]$Output,
    [string]$Boot,
    [string]$Decoder,
    [string]$JitHook,
    [string]$Pheropod,
    [string]$Mode
)
$ErrorActionPreference = 'Stop'
$AsmDir, $OutDir = $AsmDir, $OutDir | ForEach-Object { $_.Trim().TrimEnd('\', '"') }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

# --- locate MSVC toolchain (shared by stub + jithook builds) ---
$sMl64 = $null
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $vswhere) {
    $sVs = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null
    if ($sVs) {
        $sMl64 = Get-ChildItem (Join-Path $sVs 'VC\Tools\MSVC\*\bin\Hostx64\x64\ml64.exe') -EA SilentlyContinue |
            Sort-Object FullName -Desc | Select-Object -First 1 -Exp FullName
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

$sK32 = $null
$sKitsRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\Lib"
if (Test-Path $sKitsRoot) {
    $sK32 = Get-ChildItem (Join-Path $sKitsRoot '*\um\x64\kernel32.lib') -EA SilentlyContinue |
        Sort-Object FullName -Desc | Select-Object -First 1 -Exp FullName
}
if (!$sK32) { throw 'kernel32.lib not found (Windows SDK needed)' }

Write-Host "[build-asm] ml64: $sMl64`n[build-asm] out : $OutDir"

function Build-StubDll([string]$AsmName, [string]$DllName, [switch]$Page, [string]$Exports)
{
    $sAsm = Join-Path $AsmDir $AsmName
    if (!(Test-Path $sAsm)) { throw "asm not found: $sAsm" }
    $sObj = Join-Path $OutDir (($DllName -replace '\.dll$', '') + '.obj')
    $sDll = Join-Path $OutDir $DllName
    & $sMl64 /nologo /c ("/Fo" + $sObj) $sAsm
    if ($LASTEXITCODE -ne 0) { throw "ml64($AsmName) failed ($LASTEXITCODE)" }
    $args = New-Object System.Collections.Generic.List[string]
    $args.Add('/nologo'); $args.Add('/dll'); $args.Add('/noentry'); $args.Add('/machine:x64'); $args.Add('/subsystem:console')
    if ($Page) { $args.Add('/export:Iamdec'); $args.Add('/nodefaultlib') }
    if ($Exports) { foreach ($e in $Exports.Split(',')) { if ($e.Trim()) { $args.Add('/export:' + $e.Trim()) } } }
    $args.Add('/out:' + $sDll)
    $args.Add($sObj)
    if ($Page) { $args.Add($sK32) }
    & $sLink $args.ToArray()
    if ($LASTEXITCODE -ne 0) { throw "link($DllName) failed ($LASTEXITCODE)" }
    Write-Host "[build-asm] done: $sDll"
}

# --- stub dlls (antheil needs all 4; native only the lamarr stub) ---
Build-StubDll 'lamarr_stub.asm' 'lamarr_stub.dll'
if ($Packer -eq 'antheil')
{
    Build-StubDll 'antheil_stub.asm' 'antheil_stub.dll'
    Build-StubDll 'antheil_paged.asm' 'Iamdec.dll' -Page
    Build-StubDll 'antlion_deco.asm' 'z0.dll' -Exports 'z0_init,z0_read,z0_align,z0_size'
}

# --- jithook dll (antheil only) ---
if ($Packer -eq 'antheil')
{
    $sJitAsm = Join-Path $AsmDir 'jithook.asm'
    if (!(Test-Path $sJitAsm)) { throw "asm not found: $sJitAsm" }
    $sJitObj = Join-Path $OutDir 'jithook.obj'
    $sJitDll = Join-Path $OutDir 'jithook.dll'
    & $sMl64 /nologo /c ("/Fo" + $sJitObj) $sJitAsm
    if ($LASTEXITCODE -ne 0) { throw "ml64(jithook.asm) failed ($LASTEXITCODE)" }
    & $sLink /nologo /dll /noentry /machine:x64 /subsystem:console `
        /export:InstallJitHook /export:SetJitHookKey /export:AddPayloadSig /export:GetJitHookDecryptCount `
        /nodefaultlib ("/out:" + $sJitDll) $sJitObj
    if ($LASTEXITCODE -ne 0) { throw "link(jithook.dll) failed ($LASTEXITCODE)" }
    Write-Host "[build-asm] done: $sJitDll"
}

# --- optional pack step (with retry) ---
if ($Pack)
{
    # native stderr lines become NativeCommandError; must not abort the retry loop
    $ErrorActionPreference = 'Continue'
    $sPackerExe = Join-Path $AsmDir 'bin\Release\net8.0-windows\LamarrNativePack.exe'
    if (!(Test-Path $sPackerExe)) { throw "packer not found: $sPackerExe" }

    $sStub = Join-Path $OutDir ($(if ($Packer -eq 'antheil') { 'antheil_stub.dll' } else { 'lamarr_stub.dll' }))
    if (!$InputPath -and !$PublishDir) { throw '-InputPath (or -PublishDir) required when -Pack is used' }
    if (!$InputPath) { $InputPath = Join-Path $PublishDir 'WeaponDamageCalc.exe' }
    if (!$Output) {
        if ($PublishDir) { $Output = Join-Path $PublishDir 'WeaponDamageCalc_packed.exe' }
        else { throw '-Output required when -Pack is used without -PublishDir' }
    }

    $argsList = @('--stub', $sStub, '--input', $InputPath, '--output', $Output)
    if ($Boot)    { $argsList += @('--boot', $Boot) }
    if ($Decoder) { $argsList += @('--decoder', $Decoder) }
    if ($JitHook) { $argsList += @('--jithook', $JitHook) }
    if ($Pheropod) { $argsList += @('--pheropod', $Pheropod) }
    if ($Mode)    { $argsList += @('--mode', $Mode) }

    $log = Join-Path $env:TEMP 'lamarr_pack_retry.log'
    $code = 1
    for ($i = 0; $i -lt 3; $i++) {
        & $sPackerExe @argsList *> $log
        $code = $LASTEXITCODE
        if ($code -eq 0) { Write-Host "[build-asm] pack ok"; break }
        Write-Host "[build-asm] packer exit=$code, retry $($i+1)/3..."
        if (Test-Path $log) { Get-Content $log | Select-Object -Last 6 }
        Start-Sleep -Seconds 1
    }
    if ($code -ne 0) { exit 1 }
}
