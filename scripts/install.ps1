param(
    [string]$InstallDir = "$env:LOCALAPPDATA\ApplePeripheralsForWindows",
    [string]$TaskName = "ApplePeripheralsBridge",
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Runtime = "win-x64",
    [switch]$InstallPrecisionTrackpadDriver
)

$ErrorActionPreference = "Stop"
$RepoRoot = Split-Path -Parent $PSScriptRoot
$ProjectPath = Join-Path $RepoRoot "src\MagicTrackpad.App\MagicTrackpad.App.csproj"
$AppDir = Join-Path $InstallDir "app"
$ConfigPath = Join-Path $HOME ".magictrackpad-bridge.json"
$StartMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Apple Peripherals for Windows"
$LegacyStartMenuDir = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs\Magic Trackpad Bridge"
$StartupDir = [Environment]::GetFolderPath("Startup")
$StartupShortcutPath = Join-Path $StartupDir "Apple Peripherals.lnk"
$LegacyStartupShortcutPath = Join-Path $StartupDir "Magic Trackpad Bridge.lnk"
$LegacyInstallDir = Join-Path $env:LOCALAPPDATA "MagicTrackpadBridge"

function Stop-MagicTrackpadProcesses {
    Get-CimInstance Win32_Process |
        Where-Object {
            ($_.Name -in @("MagicTrackpad.exe", "python.exe", "pythonw.exe")) -and
            ($_.CommandLine -like "*ApplePeripheralsForWindows*" -or $_.CommandLine -like "*MagicTrackpadBridge*" -or $_.CommandLine -like "*magictrackpad_bridge*")
        } |
        ForEach-Object {
            try {
                Stop-Process -Id $_.ProcessId -Force -ErrorAction Stop
            }
            catch {
                Write-Warning "Could not stop process $($_.ProcessId): $($_.Exception.Message)"
            }
        }
}

function Test-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    return $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)
}

if (!(Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 10 SDK is required to build and install MagicTrackpad.exe. Install Microsoft.DotNet.SDK.10, then run this script again."
}

Stop-MagicTrackpadProcesses

foreach ($task in @("MagicTrackpadBridge", $TaskName) | Select-Object -Unique) {
    if (Get-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue) {
        Stop-ScheduledTask -TaskName $task -ErrorAction SilentlyContinue
        Unregister-ScheduledTask -TaskName $task -Confirm:$false
    }
}

foreach ($legacyPath in @($LegacyStartMenuDir, $LegacyStartupShortcutPath, $LegacyInstallDir)) {
    if ($legacyPath -ne $InstallDir -and (Test-Path $legacyPath)) {
        Remove-Item -LiteralPath $legacyPath -Recurse -Force
    }
}

New-Item -ItemType Directory -Force -Path $AppDir | Out-Null
dotnet publish $ProjectPath -c Release -r $Runtime --self-contained true -p:PublishSingleFile=false -o $AppDir

if ($LASTEXITCODE -ne 0) { throw "App publish failed: $LASTEXITCODE" }

$ExePath = Join-Path $AppDir "MagicTrackpad.exe"
if (!(Test-Path $ExePath)) {
    throw "Publish completed but MagicTrackpad.exe was not found at $ExePath"
}

if (!(Test-Path $ConfigPath)) {
    $ConfigProcess = Start-Process -WindowStyle Hidden -FilePath $ExePath -ArgumentList "--write-config --config `"$ConfigPath`"" -Wait -PassThru
    if ($ConfigProcess.ExitCode -ne 0) {
        throw "Could not create default config at $ConfigPath"
    }
}
else {
    $ConfigProcess = Start-Process -WindowStyle Hidden -FilePath $ExePath -ArgumentList "--migrate-config --config `"$ConfigPath`"" -Wait -PassThru
    if ($ConfigProcess.ExitCode -ne 0) {
        throw "Could not migrate config at $ConfigPath"
    }
}

New-Item -ItemType Directory -Force -Path $StartMenuDir | Out-Null
$Shell = New-Object -ComObject WScript.Shell

$SettingsShortcut = $Shell.CreateShortcut((Join-Path $StartMenuDir "Apple Peripherals Settings.lnk"))
$SettingsShortcut.TargetPath = $ExePath
$SettingsShortcut.Arguments = "--settings --config `"$ConfigPath`""
$SettingsShortcut.WorkingDirectory = $AppDir
$SettingsShortcut.Description = "Configure Apple keyboard and trackpad support on Windows"
$SettingsShortcut.Save()

$RunShortcut = $Shell.CreateShortcut((Join-Path $StartMenuDir "Run Apple Peripherals Bridge.lnk"))
$RunShortcut.TargetPath = $ExePath
$RunShortcut.Arguments = "--bridge --config `"$ConfigPath`""
$RunShortcut.WorkingDirectory = $AppDir
$RunShortcut.Description = "Start Apple keyboard and trackpad support on Windows"
$RunShortcut.Save()

$TaskInstalled = $false
try {
    $Action = New-ScheduledTaskAction -Execute $ExePath -Argument "--bridge --config `"$ConfigPath`"" -WorkingDirectory $AppDir
    $Trigger = New-ScheduledTaskTrigger -AtLogOn
    $RunLevel = "Limited"
    $Principal = New-ScheduledTaskPrincipal -UserId $env:USERNAME -LogonType Interactive -RunLevel $RunLevel
    $Settings = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries -RestartCount 10 -RestartInterval (New-TimeSpan -Minutes 1) -MultipleInstances IgnoreNew -ExecutionTimeLimit (New-TimeSpan -Seconds 0)
    Register-ScheduledTask -TaskName $TaskName -Action $Action -Trigger $Trigger -Principal $Principal -Settings $Settings -Force | Out-Null
    Start-ScheduledTask -TaskName $TaskName
    $TaskInstalled = $true
}
catch {
    $StartupShortcut = $Shell.CreateShortcut($StartupShortcutPath)
    $StartupShortcut.TargetPath = $ExePath
    $StartupShortcut.Arguments = "--bridge --config `"$ConfigPath`""
    $StartupShortcut.WorkingDirectory = $AppDir
    $StartupShortcut.Description = "Start Apple keyboard and trackpad support at sign in"
    $StartupShortcut.Save()
    Start-Process -WindowStyle Hidden -FilePath $ExePath -ArgumentList "--bridge --config `"$ConfigPath`"" -WorkingDirectory $AppDir
    Write-Warning "Scheduled task was not registered; installed Startup shortcut and started the bridge for this session. $($_.Exception.Message)"
}

if ($TaskInstalled) {
    if (Test-Path $StartupShortcutPath) {
        Remove-Item -LiteralPath $StartupShortcutPath -Force
    }
    Write-Host "Installed Apple Peripherals scheduled task: $TaskName"
}
else {
    Write-Host "Startup shortcut: $StartupShortcutPath"
}

Write-Host "Settings app shortcut: $StartMenuDir\Apple Peripherals Settings.lnk"
Write-Host "Installed app: $ExePath"
Write-Host "Config: $ConfigPath"

if ($InstallPrecisionTrackpadDriver) {
    & (Join-Path $PSScriptRoot "install-trackpad-driver.ps1")
}
