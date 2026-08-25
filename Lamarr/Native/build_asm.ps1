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

# 1) locate ml64 (vswhere -> VS env -> PATH)
$sMl64 = $null
$sVsWhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $sVsWhere) {
    $sVs = & $sVsWhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null
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

# 2) locate Windows SDK kernel32.lib
$sK32 = $null
$sKitsRoot = "${env:ProgramFiles(x86)}\Windows Kits\10\Lib"
if (Test-Path $sKitsRoot) {
    $sK32 = Get-ChildItem (Join-Path $sKitsRoot '*\um\x64\kernel32.lib') -EA SilentlyContinue |
        Sort-Object FullName -Desc | Select-Object -First 1 -Exp FullName
}
if (!$sK32) { throw 'kernel32.lib not found (Windows SDK needed)' }

# 3) assemble asm -> dll
Write-Host "[build-asm] ml64: $sMl64`n[build-asm] out : $OutDir"

function Build-StubDll([string]$AsmName, [string]$DllName, [switch]$Page, [string]$Exports)
{
    $sAsm = Join-Path $AsmDir $AsmName
    if (!(Test-Path $sAsm)) { throw "asm not found: $sAsm" }
    $sObj = Join-Path $OutDir (($DllName -replace '\.dll$', '') + '.obj')
    $sDll = Join-Path $OutDir $DllName
    & $sMl64 /nologo /c ("/Fo" + $sObj) $sAsm
    if ($LASTEXITCODE -ne 0) { throw "ml64($AsmName) failed ($LASTEXITCODE)" }
    $rgLinkArgs = New-Object System.Collections.Generic.List[string]
    $rgLinkArgs.Add('/nologo'); $rgLinkArgs.Add('/dll'); $rgLinkArgs.Add('/noentry'); $rgLinkArgs.Add('/machine:x64'); $rgLinkArgs.Add('/subsystem:console')
    if ($Page) { $rgLinkArgs.Add('/export:Iamdec'); $rgLinkArgs.Add('/nodefaultlib') }
    if ($Exports) { foreach ($e in $Exports.Split(',')) { if ($e.Trim()) { $rgLinkArgs.Add('/export:' + $e.Trim()) } } }
    $rgLinkArgs.Add('/out:' + $sDll)
    $rgLinkArgs.Add($sObj)
    if ($Page) { $rgLinkArgs.Add($sK32) }
    & $sLink $rgLinkArgs.ToArray()
    if ($LASTEXITCODE -ne 0) { throw "link($DllName) failed ($LASTEXITCODE)" }
    Write-Host "[build-asm] done: $sDll"
}

Build-StubDll 'lamarr_stub.asm' 'lamarr_stub.dll'
if ($Packer -eq 'antheil')
{
    Build-StubDll 'antheil_stub.asm' 'antheil_stub.dll'
    Build-StubDll 'antheil_paged.asm' 'Iamdec.dll' -Page
    Build-StubDll 'antlion_deco.asm' 'z0.dll' -Exports 'z0_init,z0_read,z0_align,z0_size'
}

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

# 4) invoke packer (retry 3x)
if ($Pack)
{
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

    $rgArgs = @('--stub', $sStub, '--input', $InputPath, '--output', $Output)
    if ($Boot)    { $rgArgs += @('--boot', $Boot) }
    if ($Decoder) { $rgArgs += @('--decoder', $Decoder) }
    if ($JitHook) { $rgArgs += @('--jithook', $JitHook) }
    if ($Pheropod) { $rgArgs += @('--pheropod', $Pheropod) }
    if ($Mode)    { $rgArgs += @('--mode', $Mode) }

    $sLog = Join-Path $env:TEMP 'lamarr_pack_retry.log'
    $iCode = 1
    for ($i = 0; $i -lt 3; $i++) {
        & $sPackerExe @rgArgs *> $sLog
        $iCode = $LASTEXITCODE
        if ($iCode -eq 0) { Write-Host "[build-asm] pack ok"; break }
        Write-Host "[build-asm] packer exit=$iCode, retry $($i+1)/3..."
        if (Test-Path $sLog) { Get-Content $sLog | Select-Object -Last 6 }
        Start-Sleep -Seconds 1
    }
    if ($iCode -ne 0) { exit 1 }
}
