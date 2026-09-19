param(
    [string]$PackagePath = (Join-Path $PSScriptRoot '..\artifacts\installer\MagicTrackpad2ForWindows-MSSigned.zip'),
    [string]$CacheDir = "$env:LOCALAPPDATA\ApplePeripheralsForWindows\drivers",
    [switch]$VerifyOnly
)

$ErrorActionPreference = 'Stop'
$expectedHash = '2870C0C7982CE6AAFC3FF763FEC2999423DC4BDBD1A2C0E31CA216F26A75714F'
if (!(Test-Path -LiteralPath $PackagePath -PathType Leaf)) {
    throw 'Build the installer first or pass -PackagePath with the pinned v2.0 signed ZIP. No download is performed during installation.'
}
if ((Get-FileHash -LiteralPath $PackagePath -Algorithm SHA256).Hash -ne $expectedHash) {
    throw 'Trackpad driver package SHA-256 mismatch.'
}
$architecture = switch ([Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString()) {
    'X64' { 'AMD64' }
    'Arm64' { 'ARM64' }
    default { throw 'Only x64 and ARM64 Windows are supported.' }
}
$extractRoot = Join-Path ([IO.Path]::GetFullPath($CacheDir)) ('verified-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Force -Path $extractRoot | Out-Null
Expand-Archive -LiteralPath $PackagePath -DestinationPath $extractRoot
$driverDir = Join-Path $extractRoot "MT2FW11-20260223-MSSigned\$architecture"
$infPath = Join-Path $driverDir 'AmtPtpDevice.inf'
if (!(Test-Path -LiteralPath $infPath)) { throw 'Required driver INF is missing.' }
foreach ($name in @('amtptpdevice.cat', 'AmtPtpDeviceUsbUm.dll', 'AmtPtpHidFilter.sys')) {
    $path = Join-Path $driverDir $name
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'CN=Microsoft Windows Hardware Compatibility Publisher,') {
        throw "Required Microsoft signature is invalid: $name"
    }
    Write-Output "Verified Microsoft signature: $name"
}
if ($VerifyOnly) { return }
$principal = [Security.Principal.WindowsPrincipal]::new([Security.Principal.WindowsIdentity]::GetCurrent())
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Driver installation requires Administrator access. Run this script elevated.'
}
# PnP validates catalog membership and binds only matching device IDs. No test signing.
$systemDirectory = if ([Environment]::Is64BitOperatingSystem -and ![Environment]::Is64BitProcess) {
    Join-Path $env:SystemRoot 'Sysnative'
} else {
    Join-Path $env:SystemRoot 'System32'
}
& (Join-Path $systemDirectory 'pnputil.exe') /add-driver $infPath /install
$driverExit = $LASTEXITCODE
if ($driverExit -notin @(0, 3010)) { throw "pnputil failed: $driverExit" }
Write-Output "Driver installation exit code: $driverExit"
if ($driverExit -eq 3010) { Write-Warning 'Windows requires a restart to finish driver installation.' }
