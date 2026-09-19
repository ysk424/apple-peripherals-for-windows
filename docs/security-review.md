# USB/offline fork security review

Review date: 2026-09-20. Upstream app commit:
`5f5cab6cfba12fa4d579b45215e1b92bc0a97165`.

## Scope and conclusion

Reviewed the app/installer source, process launches, native imports, configuration,
diagnostic writes, startup behavior, build/install scripts and package references.
Reviewed networking-related references in the Python predecessor and keyboard
filter sources; neither component is installed by this USB configuration.

No telemetry, exfiltration or automatic update implementation was found in the
resident C# app. Its native interfaces access local HID, keyboard/mouse input,
display brightness and Windows UI. There are no third-party NuGet PackageReference
dependencies. This is a targeted security review, not a proof that all bugs or
malicious behavior are absent, nor a complete memory-safety audit of the drivers.

## Findings and changes

| Finding | Treatment |
| --- | --- |
| Setup could download a driver at installation time, without a content hash | Removed HTTP fallback; require bundled archive matching a fixed SHA-256 |
| Build fetched a fixed URL but did not pin archive contents | Added archive SHA-256 verification before bundling |
| Trackpad checks accepted any trusted Authenticode signer | Require Microsoft signer in setup and exact Microsoft Hardware Compatibility publisher in the standalone installation script; pinned ZIP also covers INF contents |
| Help button opened GitHub in the default browser | Replaced with local explanatory text |
| Opt-in raw logging could include keyboard HID data | Return after keyboard processing; keyboard reports cannot reach the trackpad logger/parser |
| Explicit HID diagnostics retained sample keyboard report bytes | Suppress keyboard sample bytes; counts and Fn state diagnostics remain local |
| Elevated installation could register an elevated task executing a user-writable app | Startup registration always uses LIMITED; setup defaults to asInvoker; local installation was performed unelevated |
| USB driver and app gesture engine could compete | Added use_windows_precision_touchpad to bypass USB trackpad handling in the bridge |
| Battery fallback resolved PowerShell through executable search paths | Use the absolute Windows PowerShell system path |
| Installer output path prefix allowed sibling names | Require a directory separator in repository containment check |
| Publish failures could be missed if an older executable existed | Check dotnet publish exit status |

The SDK is pinned to .NET 10.0.401; both managed projects target net10.0-windows.
The WinForms WFO1000 compatibility error was fixed by marking the runtime-only
battery meter value as not designer-serialized. .NET SDK telemetry was disabled
in the local build environment. Restore sources are explicitly limited to nuget.org.

## Signed Precision Touchpad driver

Source reviewed: `vitoplantamura/MagicTrackpad2ForWindows`, checkout
`68b31c466f4e2ec8905cf7be44580b01705650f3`.

Package: release `v2.0`, `MT2FW11-20260223-MSSigned.zip`.
SHA-256: `2870c0c7982ce6aafc3ff763fec2999423dc4bdbd1a2c0e31ca216f26a75714f`.
This matches the release asset digest returned by GitHub.

AMD64 catalog, USB UMDF DLL and Bluetooth filter SYS all reported Valid signatures
from Microsoft Windows Hardware Compatibility Publisher. The package includes
the actual USB hardware ID 05AC:0265, interface 01. PnP validates the catalog when
installing. Secure Boot/test-signing policy was not changed.

No network implementation was found by targeted source searches. `dumpbin /imports`
on the supplied USB DLL and filter SYS found no WinHTTP, WinINet, Winsock or WSK
imports. The DLL imports local Windows/WDF/runtime APIs. Imported APIs are only
one form of evidence; indirect behavior cannot be excluded by this check alone.
The signed binary was not rebuilt or proven reproducible from the reviewed source.
The separate upstream control-panel executable was not installed or launched.

## Local installation and validation

- Local Release build: zero warnings/errors; existing C# self-tests passed.
- Python parser/gesture tests: 15 passed.
- Both app and setup published locally; installed app includes .NET and Windows
  Desktop runtime 10.0.12 and needs no separately installed runtime.
- USB driver installation returned 0. Device reports
  Apple USB Precision Touchpad Device (User-mode), oem2.inf, problem code 0.
- Startup shortcut and per-user Windows uninstall registration were created.
- Local config enables Windows Precision handling, disables bridge feature-report
  refresh and raw logging, and keeps upstream Mac-style keyboard mappings.
- An enabled outbound Block firewall rule, ApplePeripherals-LocalOnly-Outbound,
  covers the installed MagicTrackpad.exe on all profiles.
- A post-install TCP/UDP endpoint snapshot showed no endpoints owned by the bridge.
  This was a snapshot, not continuous packet capture.

Machine-specific logs, import listings, SDK source metadata and binaries are under
ignored `artifacts/`; no device serials or local diagnostic reports are committed.
Physical scrolling, key feel and repeated KVM switching require user testing.

## Remaining boundaries

Keyboard remapping uses a global low-level hook. While an Apple keyboard is
connected, mappings may also affect another keyboard. Fn/Globe is model-dependent;
the optional keyboard filter and Touch ID support were not installed/validated.
Windows touchpad settings control USB gestures, not the app gesture sliders.

Build-time SDK/NuGet/driver downloads remain necessary. Windows may access
certificate revocation services when verifying signatures. System features invoked
by hotkeys (for example Windows dictation) have their own network/privacy behavior.
The firewall rule covers this app, not Windows, browsers or child PowerShell
processes; the reviewed battery PowerShell script only queries local PnP state.
Local config and install files remain writable by the user, and explicit paths to
network shares can cause Windows file access. This is not a sandbox against a
compromised user account.

Uninstall the app through Windows Installed apps. The separately installed driver
remains available for native scrolling; administrator `pnputil /delete-driver
oem2.inf /uninstall` removes the driver if rollback is needed on this machine.
Remove the named firewall rule with `Remove-NetFirewallRule` from an elevated
PowerShell if also removing the app. Preserve the config file if keeping mappings.

Sources: [upstream app](https://github.com/sheehanmunim/apple-peripherals-for-windows),
[signed driver](https://github.com/vitoplantamura/MagicTrackpad2ForWindows),
[.NET releases](https://dotnet.microsoft.com/en-us/download/dotnet),
[WinForms WFO1000](https://learn.microsoft.com/en-us/dotnet/desktop/winforms/compiler-messages/wfo1000).
