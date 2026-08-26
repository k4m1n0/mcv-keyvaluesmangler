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
    [string]$Mode,
    [string]$AppName = 'WeaponDamageCalc',
    [string]$PackerTfm = 'net8.0-windows'
)
$ErrorActionPreference = 'Stop'
$ASM_DIR, $OUT_DIR = $AsmDir, $OutDir | ForEach-Object { $_.Trim().TrimEnd('\', '"') }
New-Item -ItemType Directory -Force -Path $OutDir | Out-Null

$ML64_EXE = $null
$VSWHERE = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
if (Test-Path $VSWHERE) {
    $MSVC_ROOT = & $VSWHERE -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath 2>$null
    if ($MSVC_ROOT) {
        $ML64_EXE = Get-ChildItem (Join-Path $MSVC_ROOT 'VC\Tools\MSVC\*\bin\Hostx64\x64\ml64.exe') -EA SilentlyContinue |
            Sort-Object FullName -Desc | Select-Object -First 1 -Exp FullName
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
if (!(Test-Path $LINK_EXE)) { throw "link.exe not found next to ml64: $LINK_EXE" }

$K32_LIB = $null
$KITS_ROOT = "${env:ProgramFiles(x86)}\Windows Kits\10\Lib"
if (Test-Path $KITS_ROOT) {
    $K32_LIB = Get-ChildItem (Join-Path $KITS_ROOT '*\um\x64\kernel32.lib') -EA SilentlyContinue |
        Sort-Object FullName -Desc | Select-Object -First 1 -Exp FullName
}
if (!$K32_LIB) { throw 'kernel32.lib not found (Windows SDK needed)' }

Write-Host "[build-asm] STATUS ml64 = $ML64_EXE`n[build-asm] out : $OUT_DIR"

function build_stub_dll([string]$AsmName, [string]$DllName, [switch]$Page, [string]$Exports)
{
    $ASM_PATH = Join-Path $ASM_DIR $AsmName
    if (!(Test-Path $ASM_PATH)) { throw "asm not found: $ASM_PATH" }
    $OBJ_FILE = Join-Path $OUT_DIR (($DllName -replace '\.dll$', '') + '.obj')
    $DLL_FILE = Join-Path $OUT_DIR $DllName
    & $ML64_EXE /nologo /c ("/Fo" + $OBJ_FILE) $ASM_PATH
    if ($LASTEXITCODE -ne 0) { throw "ml64($AsmName) failed ($LASTEXITCODE)" }
    $LINK_ARGS = New-Object System.Collections.Generic.List[string]
    $LINK_ARGS.Add('/nologo'); $LINK_ARGS.Add('/dll'); $LINK_ARGS.Add('/noentry'); $LINK_ARGS.Add('/machine:x64'); $LINK_ARGS.Add('/subsystem:console')
    if ($Page) { $LINK_ARGS.Add('/export:Iamdec'); $LINK_ARGS.Add('/nodefaultlib') }
    if ($Exports) { foreach ($e in $Exports.Split(',')) { if ($e.Trim()) { $LINK_ARGS.Add('/export:' + $e.Trim()) } } }
    $LINK_ARGS.Add('/out:' + $DLL_FILE)
    $LINK_ARGS.Add($OBJ_FILE)
    if ($Page) { $LINK_ARGS.Add($K32_LIB) }
    & $LINK_EXE $LINK_ARGS.ToArray()
    if ($LASTEXITCODE -ne 0) { throw "link($DllName) failed ($LASTEXITCODE)" }
    Write-Host "[build-asm] STATUS built: $DLL_FILE"
}

build_stub_dll 'lamarr_stub.asm' 'lamarr_stub.dll'
if ($Packer -eq 'antheil')
{
    build_stub_dll 'antheil_stub.asm' 'antheil_stub.dll'
    build_stub_dll 'antheil_paged.asm' 'Iamdec.dll' -Page
    build_stub_dll 'antlion_deco.asm' 'z0.dll' -Exports 'z0_init,z0_read,z0_align,z0_size'
}

if ($Packer -eq 'antheil')
{
    $JIT_ASM = Join-Path $ASM_DIR 'jithook.asm'
    if (!(Test-Path $JIT_ASM)) { throw "asm not found: $JIT_ASM" }
    $JIT_OBJ = Join-Path $OUT_DIR 'jithook.obj'
    $JIT_DLL = Join-Path $OUT_DIR 'jithook.dll'
    & $ML64_EXE /nologo /c ("/Fo" + $JIT_OBJ) $JIT_ASM
    if ($LASTEXITCODE -ne 0) { throw "ml64(jithook.asm) failed ($LASTEXITCODE)" }
    & $LINK_EXE /nologo /dll /noentry /machine:x64 /subsystem:console `
        /export:InstallJitHook /export:SetJitHookKey /export:AddPayloadSig /export:GetJitHookDecryptCount `
        /nodefaultlib ("/out:" + $JIT_DLL) $JIT_OBJ
    if ($LASTEXITCODE -ne 0) { throw "link(jithook.dll) failed ($LASTEXITCODE)" }
    Write-Host "[build-asm] STATUS built: $JIT_DLL"
}

if ($Pack)
{
    $ErrorActionPreference = 'Continue'
    $PACKER_EXE = Join-Path $ASM_DIR "bin\Release\$PackerTfm\LamarrNativePack.exe"
    if (!(Test-Path $PACKER_EXE)) { throw "packer not found: $PACKER_EXE" }

    $STUB_DLL = Join-Path $OUT_DIR ($(if ($Packer -eq 'antheil') { 'antheil_stub.dll' } else { 'lamarr_stub.dll' }))
    if (!$InputPath -and !$PublishDir) { throw '-InputPath (or -PublishDir) required when -Pack is used' }
    if (!$InputPath) { $InputPath = Join-Path $PublishDir "$AppName.exe" }
    if (!$Output) {
        if ($PublishDir) { $Output = Join-Path $PublishDir "${AppName}_packed.exe" }
        else { throw '-Output required when -Pack is used without -PublishDir' }
    }

    $PACK_ARGS = @('--stub', $STUB_DLL, '--input', $InputPath, '--output', $Output)
    if ($Boot)    { $PACK_ARGS += @('--boot', $Boot) }
    if ($Decoder) { $PACK_ARGS += @('--decoder', $Decoder) }
    if ($JitHook) { $PACK_ARGS += @('--jithook', $JitHook) }
    if ($Pheropod) { $PACK_ARGS += @('--pheropod', $Pheropod) }
    if ($Mode)    { $PACK_ARGS += @('--mode', $Mode) }

    $RETRY_LOG = Join-Path $env:TEMP 'lamarr_pack_retry.log'
    $EXIT_CODE = 1
    # MSBuild Exec force me
    for ($i = 0; $i -lt 3; $i++) {
        & $PACKER_EXE @PACK_ARGS *> $RETRY_LOG
        $EXIT_CODE = $LASTEXITCODE
        if ($EXIT_CODE -eq 0) { Write-Host "[build-asm] STATUS pack ok"; break }
        Write-Host "[build-asm] STATUS packer exit=$EXIT_CODE, retry $($i+1)/3..."
        if (Test-Path $RETRY_LOG) { Get-Content $RETRY_LOG | Select-Object -Last 6 }
        Start-Sleep -Seconds 1
    }
    if ($EXIT_CODE -ne 0) { exit 1 }
}
