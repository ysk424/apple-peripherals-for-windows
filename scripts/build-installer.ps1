param(
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [string]$Version = "",
    [string]$OutputDir = "",
    [string]$KeyboardDriverZip = "",
    [switch]$RequireKeyboardDriver,
    [switch]$AppOnly
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ArtifactsRoot = if ([string]::IsNullOrWhiteSpace($OutputDir)) {
    Join-Path $RepoRoot "artifacts\installer"
}
else {
    if ([IO.Path]::IsPathRooted($OutputDir)) { $OutputDir } else { Join-Path $RepoRoot $OutputDir }
}

$ResolvedRepo = [IO.Path]::GetFullPath($RepoRoot)
$ResolvedArtifacts = [IO.Path]::GetFullPath($ArtifactsRoot)
if (!$ResolvedArtifacts.StartsWith($ResolvedRepo.TrimEnd([IO.Path]::DirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar, [StringComparison]::OrdinalIgnoreCase)) {
    throw "OutputDir must be inside the repository."
}

$AppProject = Join-Path $RepoRoot "src\MagicTrackpad.App\MagicTrackpad.App.csproj"
$SetupProject = Join-Path $RepoRoot "src\ApplePeripherals.Setup\ApplePeripherals.Setup.csproj"
$PayloadDir = Join-Path $ArtifactsRoot "payload"
$AppPayloadDir = Join-Path $PayloadDir "app"
$SetupOutDir = Join-Path $ArtifactsRoot "setup"
$PayloadZip = Join-Path $ArtifactsRoot "ApplePeripheralsPayload.zip"
$DriverZip = Join-Path $ArtifactsRoot "MagicTrackpad2ForWindows-MSSigned.zip"
$DriverPackageUrl = "https://github.com/vitoplantamura/MagicTrackpad2ForWindows/releases/download/v2.0/MT2FW11-20260223-MSSigned.zip"
$InstallerName = "ApplePeripheralsSetup-$Runtime.exe"
$InstallerPath = Join-Path $ArtifactsRoot $InstallerName
$KeyboardDriverZipPath = if ([string]::IsNullOrWhiteSpace($KeyboardDriverZip)) {
    ""
}
elseif ([IO.Path]::IsPathRooted($KeyboardDriverZip)) {
    $KeyboardDriverZip
}
else {
    Join-Path $RepoRoot $KeyboardDriverZip
}

$versionArgs = @()
if (![string]::IsNullOrWhiteSpace($Version)) {
    $informationalVersion = $Version -replace '^[vV]', ''
    $assemblyVersion = ($informationalVersion -split "-", 2)[0]
    $versionArgs += "-p:Version=$informationalVersion"
    $versionArgs += "-p:AssemblyVersion=$assemblyVersion"
    $versionArgs += "-p:FileVersion=$assemblyVersion"
    $versionArgs += "-p:InformationalVersion=$informationalVersion"
}

if ($AppOnly -and (![string]::IsNullOrWhiteSpace($KeyboardDriverZipPath) -or $RequireKeyboardDriver)) {
    throw "Do not pass -KeyboardDriverZip or -RequireKeyboardDriver with -AppOnly. App-only builds are for development/testing and do not bundle the Magic Keyboard driver."
}
if (!$AppOnly -and [string]::IsNullOrWhiteSpace($KeyboardDriverZipPath)) {
    throw "A full installer must bundle a Microsoft-signed Magic Keyboard driver. Pass -KeyboardDriverZip with an installer-ready signed package, or pass -AppOnly for a development installer that only bundles the app and signed trackpad driver."
}
if (![string]::IsNullOrWhiteSpace($KeyboardDriverZipPath)) {
    $KeyboardDriverZipPath = [IO.Path]::GetFullPath($KeyboardDriverZipPath)
    if (!(Test-Path $KeyboardDriverZipPath)) {
        throw "KeyboardDriverZip was provided but was not found: $KeyboardDriverZipPath"
    }

    $ResolvedArtifactsWithSeparator = $ResolvedArtifacts.TrimEnd([IO.Path]::DirectorySeparatorChar, [IO.Path]::AltDirectorySeparatorChar) + [IO.Path]::DirectorySeparatorChar
    if ($KeyboardDriverZipPath.StartsWith($ResolvedArtifactsWithSeparator, [StringComparison]::OrdinalIgnoreCase)) {
        throw "KeyboardDriverZip must not be inside OutputDir because the output directory is recreated during installer builds."
    }

    Write-Host "Verifying Microsoft-signed Magic Keyboard driver package..."
    & (Join-Path $PSScriptRoot "verify-keyboard-driver-package.ps1") -ZipPath $KeyboardDriverZipPath -Runtime $Runtime -RequireMicrosoftSignature
    if ($LASTEXITCODE -ne 0) {
        throw "Keyboard driver package verification failed with exit code $LASTEXITCODE."
    }
}

if (Test-Path $ArtifactsRoot) {
    Remove-Item -LiteralPath $ArtifactsRoot -Recurse -Force
}

New-Item -ItemType Directory -Force -Path $AppPayloadDir | Out-Null
New-Item -ItemType Directory -Force -Path $SetupOutDir | Out-Null

Write-Host "Downloading signed Precision Touchpad driver package..."
Invoke-WebRequest -Uri $DriverPackageUrl -OutFile $DriverZip
$ExpectedDriverHash = "2870C0C7982CE6AAFC3FF763FEC2999423DC4BDBD1A2C0E31CA216F26A75714F"
if ((Get-FileHash -LiteralPath $DriverZip -Algorithm SHA256).Hash -ne $ExpectedDriverHash) {
    throw "Trackpad driver SHA-256 mismatch."
}

Write-Host "Publishing app payload..."
dotnet publish $AppProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=false `
    $versionArgs `
    -o $AppPayloadDir

if ($LASTEXITCODE -ne 0) { throw "App publish failed: $LASTEXITCODE" }

if (!(Test-Path (Join-Path $AppPayloadDir "MagicTrackpad.exe"))) {
    throw "MagicTrackpad.exe was not published to the app payload."
}

Write-Host "Compressing app payload..."
Compress-Archive -Path (Join-Path $AppPayloadDir "*") -DestinationPath $PayloadZip -Force

Write-Host "Publishing setup executable..."
dotnet publish $SetupProject `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:EnableCompressionInSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:PayloadZip="$PayloadZip" `
    -p:DriverZip="$DriverZip" `
    -p:KeyboardDriverZip="$KeyboardDriverZipPath" `
    $versionArgs `
    -o $SetupOutDir

if ($LASTEXITCODE -ne 0) { throw "Setup publish failed: $LASTEXITCODE" }

$BuiltInstaller = Join-Path $SetupOutDir "ApplePeripheralsSetup.exe"
if (!(Test-Path $BuiltInstaller)) {
    throw "ApplePeripheralsSetup.exe was not created."
}

Copy-Item -LiteralPath $BuiltInstaller -Destination $InstallerPath -Force

Write-Host "Installer created: $InstallerPath"
