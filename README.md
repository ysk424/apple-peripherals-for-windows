# Apple Peripherals for Windows

## USB / offline .NET 10 fork

This fork targets .NET 10 LTS (SDK 10.0.401, runtime 10.0.12 at review time).
See [the security review](docs/security-review.md) for findings and limitations.

For USB Magic Trackpad + Magic Keyboard:

1. Build with `scripts/build-installer.ps1 -AppOnly`. This build step downloads the
   pinned, Microsoft-signed Precision Touchpad driver and verifies its SHA-256.
2. Run the resulting setup with `/install /quiet` as your ordinary user.
3. From an Administrator PowerShell, run `scripts/install-trackpad-driver.ps1`.
   This step uses the local ZIP only; it never downloads a driver.
4. In `%USERPROFILE%\.magictrackpad-bridge.json`, set
   `use_windows_precision_touchpad` to `true` and `enable_multitouch_on_start` to
   `false`. Keep `log_raw_reports` set to `false`.

Windows Settings > Bluetooth & devices > Touchpad controls USB scrolling and
gestures. The app's gesture sliders apply to its own raw HID gesture engine,
which is bypassed for USB trackpads in this configuration. Keyboard remapping
continues through the local bridge: Command maps to Ctrl, Control to Win, and
Option to Alt. KVM reconnection uses normal Windows Plug and Play.

The installer runs without elevation by default. Driver installation is a
separate elevated operation. Startup always uses ordinary user privileges.
No optional keyboard filter is installed by the USB procedure above; Fn/Globe
and Touch ID behavior are not guaranteed. There is no runtime downloader or
browser-opening help link in this fork. Builds still need Microsoft/NuGet/GitHub
downloads. The original upstream feature description follows.

Native Windows settings app, background bridge, and driver installer for Apple Magic Trackpad and Magic Keyboard support over Bluetooth.

Windows can pair Apple peripherals as Bluetooth HID devices, but many of the useful Mac-style behaviors are missing. This repo provides one C#/.NET Windows app that enables Magic Trackpad multitouch mode where user-mode HID access is available, reads reports through Raw Input plus direct HID collection readers, and applies the trackpad gestures and keyboard remaps you configure. For full Windows Precision Touchpad behavior, install the signed Precision driver with the driver installer below.

## Native App

- Built with C# on .NET 10 and WinForms.
- Installs `MagicTrackpad.exe` as one per-user background bridge for keyboard and trackpad support.
- Uses a light device-studio settings UI with separate Magic Trackpad and Magic Keyboard pages.
- Adds Start Menu shortcuts for settings and manual bridge launch.
- Registers a per-user scheduled task when Windows allows it, and falls back to a Startup shortcut when task registration is blocked.
- Keeps Bluetooth multitouch mode refreshed after reconnects and wake events.
- Adds a direct HID report reader and diagnostics command for validating whether Windows exposes raw touch reports to user mode.
- Reloads saved settings while the bridge is running.
- Detects Apple Magic Keyboard devices and applies the configured keyboard remaps while one is connected.

## Controls

The settings app lets you configure:

Keyboard:

- Command, Control, Option, and Caps Lock remaps.
- Mac-like F1 through F12 icon-row defaults: brightness, Mission Control, Spotlight/Search, Dictation, Notification Center, media, mute, and volume.
- Fn/Globe action mapping through the optional signed Magic Keyboard filter driver, plus F13 through F19 keybinds and a full F-key mapping dialog.
- Whether keyboard remaps are active only while an Apple keyboard is connected.

Trackpad:

- Pointer movement, sensitivity, and X/Y direction.
- Two-finger vertical and horizontal scrolling.
- Natural or traditional scroll direction.
- Two-finger Smart Zoom, rotate, and page back/forward swipes.
- Tap-to-click and one-/two-/three-finger tap actions.
- Physical click and multi-finger physical click actions.
- Pinch-to-zoom modifier and sensitivity.
- Three- and four-finger swipe keybinds for Windows desktops, task view, desktop reveal, App Expose-style actions, or any hotkey.
- Four/five-finger pinch/spread actions for Launchpad/Start and Show Desktop equivalents, plus four-finger tap for Notification Center.
- Swipe thresholds, raw report logging, and reconnect refresh interval.

Hardware/Windows limits:

- If Windows binds the Magic Trackpad only as a mouse, the app can detect the device but cannot read protected multitouch contacts from the system-owned collection. Install the Precision Touchpad driver below for full multitouch behavior.
- Force Touch pressure and haptic feedback depend on the Apple HID report stream and Windows Bluetooth stack. The bridge preserves pressure values where reports expose them, but Windows does not provide macOS's haptic feedback engine through the basic mouse stack.
- Display brightness uses Windows monitor brightness APIs. It works on monitors/drivers that expose brightness control and is ignored by hardware that refuses software brightness changes.

## Build And Test

Requirements:

- Windows 10/11
- .NET 10 SDK

From the repo root:

```powershell
dotnet build .\ApplePeripheralsForWindows.sln -c Release
dotnet run --project .\src\MagicTrackpad.App\MagicTrackpad.App.csproj -c Release -- --self-test
```

The repo also keeps the original parser tests as extra coverage:

```powershell
python -m unittest discover -s tests
```

## Run From Source

```powershell
dotnet run --project .\src\MagicTrackpad.App\MagicTrackpad.App.csproj -c Release -- --enable
dotnet run --project .\src\MagicTrackpad.App\MagicTrackpad.App.csproj -c Release -- --bridge
dotnet run --project .\src\MagicTrackpad.App\MagicTrackpad.App.csproj -c Release -- --settings
```

Short bridge smoke test:

```powershell
.\scripts\run.ps1 -DryRun -Seconds 5
```

HID diagnostics:

```powershell
dotnet run --project .\src\MagicTrackpad.App\MagicTrackpad.App.csproj -c Release -- --diagnose-hid --seconds 10 --diagnostics-path .\hid-diagnostics.json
```

Magic Keyboard Globe/Fn driver health check:

```powershell
$statusPath = "$env:TEMP\apple-keyboard-filter-status.txt"
Start-Process -FilePath "$env:LOCALAPPDATA\ApplePeripheralsForWindows\app\MagicTrackpad.exe" -ArgumentList @("--keyboard-filter-status", "--output", $statusPath) -Wait
Get-Content $statusPath
```

From the repo source tree:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\check-keyboard-filter-driver.ps1
```

## Install

Download `ApplePeripheralsSetup-win-x64.exe` from the latest GitHub release and run it. The setup app is self-contained: it bundles the app runtime, settings app, background bridge, and Microsoft-signed Magic Trackpad Precision Touchpad driver package. It starts the bridge for the current session, creates Start Menu shortcuts, and registers Apple Peripherals for Windows in Windows Apps / Control Panel for uninstall.

Run setup as Administrator only when explicitly installing bundled drivers. A restart may be required after driver installation.

Installed files are written to:

```text
%LOCALAPPDATA%\ApplePeripheralsForWindows
```

The app creates or reuses this config file:

```text
%USERPROFILE%\.magictrackpad-bridge.json
```

Open settings after install from the Start Menu.

Uninstall from Windows:

```text
Settings > Apps > Installed apps > Apple Peripherals for Windows > Uninstall
```

or:

```text
Control Panel > Programs > Programs and Features > Apple Peripherals for Windows
```

## Build Installer

From the repo root:

```powershell
.\scripts\build-installer.ps1 -Runtime win-x64 -KeyboardDriverZip .\artifacts\keyboard-filter\AppleKeyboardFilterDriver-signed.zip -RequireKeyboardDriver
```

The setup executable is created at:

```text
artifacts\installer\ApplePeripheralsSetup-win-x64.exe
```

Build the ARM64 installer with:

```powershell
.\scripts\build-installer.ps1 -Runtime win-arm64 -KeyboardDriverZip .\artifacts\keyboard-filter\AppleKeyboardFilterDriver-signed.zip -RequireKeyboardDriver
```

A public installer is one `.exe` that bundles the app payload, the Microsoft-signed Magic Trackpad Precision Touchpad driver, and the Microsoft-signed Apple Keyboard Filter driver. The keyboard driver ZIP must be the installer-ready package produced after Microsoft driver signing:

```powershell
.\scripts\package-signed-keyboard-driver.ps1 -SignedPackage .\path\from-partner-center.cab -RequireMicrosoftSignature
```

For app/UI development only, build an installer that omits the keyboard filter driver with:

```powershell
.\scripts\build-installer.ps1 -AppOnly
```

Do not publish app-only installers as end-user releases because Globe/Fn remapping needs the bundled signed keyboard filter driver.

## Manual Developer Install

Use PowerShell from the repo root when testing from source:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1
```

The script publishes the native app to:

```text
%LOCALAPPDATA%\ApplePeripheralsForWindows\app\MagicTrackpad.exe
```

It creates or reuses this config file:

```text
%USERPROFILE%\.magictrackpad-bridge.json
```

Open settings after install from the Start Menu, or run:

```powershell
.\scripts\settings.ps1 -Installed
```

Install the Microsoft-signed Magic Trackpad Precision Touchpad driver from an elevated PowerShell window:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-trackpad-driver.ps1
```

Or install the app and driver together from an elevated PowerShell window:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install.ps1 -InstallPrecisionTrackpadDriver
```

The driver installer consumes the local, SHA-256-pinned MagicTrackpad2ForWindows package, requires Microsoft Authenticode signatures, selects AMD64 or ARM64, and installs `AmtPtpDevice.inf` with `pnputil`. Reconnect the trackpad or reboot if Windows keeps the old mouse binding loaded.

## Magic Keyboard Globe/Fn Driver

The Magic Keyboard Globe/Fn key is hidden in a vendor-specific HID report byte that Windows usually keeps inside the system keyboard stack. The app includes source for a HID lower-filter driver that converts that hidden bit into F23, letting the bridge map Globe/Fn to Control while the real Apple Control key can still map to the Windows key.

Build and package the keyboard filter driver with:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-keyboard-filter-driver.ps1 -Platform x64
```

For no-cost local testing, install the latest unsigned CI-built driver with Windows test-signing mode:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-keyboard-filter-driver-dev.ps1 -DownloadLatestArtifact -SkipBuild -EnableTestSigning -Elevate
```

If the script enables test-signing, reboot Windows and run the same command again. The first run changes the boot setting; the second run signs and installs the Apple Keyboard Filter driver after test-signing is active.
If Windows reports that the setting is protected by Secure Boot policy, disable Secure Boot in UEFI/BIOS first, reboot Windows, and rerun the command.

Install a signed package from an elevated PowerShell window:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-keyboard-filter-driver.ps1
```

Check whether Windows actually bound the filter to the keyboard:

```powershell
$statusPath = "$env:TEMP\apple-keyboard-filter-status.txt"
$process = Start-Process -FilePath "$env:LOCALAPPDATA\ApplePeripheralsForWindows\app\MagicTrackpad.exe" -ArgumentList @("--keyboard-filter-status", "--require-ready", "--output", $statusPath) -Wait -PassThru
Get-Content $statusPath
$process.ExitCode
```

Or from the repo source tree:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\check-keyboard-filter-driver.ps1 -RequireReady
```

After the health check is ready, run the physical Globe/Fn probe and press the Globe/Fn key while it is waiting:

```powershell
$probePath = "$env:TEMP\apple-globe-probe.json"
$process = Start-Process -FilePath "$env:LOCALAPPDATA\ApplePeripheralsForWindows\app\MagicTrackpad.exe" -ArgumentList @("--test-globe", "--seconds", "8", "--json", "--output", $probePath) -Wait -PassThru
Get-Content $probePath
$process.ExitCode
```

The settings app also has a `Live Globe/Fn` test row on the Magic Keyboard page.

For local driver development only, use the test-signing helper from elevated PowerShell:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-keyboard-filter-driver-dev.ps1 -EnableTestSigning
```

If this PC does not have Visual Studio Build Tools and WDK installed, point the helper at an existing unsigned CI package instead:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-keyboard-filter-driver-dev.ps1 -DriverDir .\artifacts\keyboard-filter\AMD64 -SkipBuild -EnableTestSigning -Elevate
```

Or have the helper download the latest successful CI driver artifact for this CPU architecture first:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-keyboard-filter-driver-dev.ps1 -DownloadLatestArtifact -SkipBuild -EnableTestSigning -Elevate
```

Check the download path without changing Windows driver or boot settings:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-keyboard-filter-driver-dev.ps1 -DownloadLatestArtifact -DownloadOnly
```

That helper creates a local test code-signing certificate, trusts it for this machine, signs a temporary copy of the local driver catalog, and installs the filter. If it has to enable Windows test-signing mode, it stops after changing the boot setting; reboot Windows and run the same command again so it can sign and install the filter after test-signing is active. It is not a public-user install path.
If Secure Boot is enabled, Windows blocks test-signing mode; disable Secure Boot in UEFI/BIOS before using this no-cost path.

The keyboard-filter installer tries to restart detected Magic Keyboard driver targets after adding the package. If Windows still reports the filter is not active, reboot or reconnect the keyboard, then rerun the health check and live Globe/Fn test.

Public installers must bundle a trusted signed keyboard driver package. See [docs/keyboard-filter-driver.md](docs/keyboard-filter-driver.md) and [docs/release.md](docs/release.md).

Manual script uninstall:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\uninstall.ps1
```

## System Design

See [docs/architecture.md](docs/architecture.md). The app is split into configuration, HID device access, Raw Input runtime, keyboard remapping, gesture translation, input injection, WinForms UI, and self-test layers.

## License And Attribution

Licensed under GPL-2.0-or-later. The Apple report parser behavior is based on the GPL Linux `hid-magicmouse` driver, and the keyboard lower-filter approach adapts MIT-licensed WinAppleKey behavior; see [docs/references.md](docs/references.md) and [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md).
