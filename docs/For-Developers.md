# For Developers

## Requirements

- Windows with .NET Framework 4.x (built in). The C# compiler that ships with Windows is used, so Visual Studio isn't needed.
- The **WiX 3.14** toolset: download `wix314-binaries.zip` from the [WiX 3.14.1 release](https://github.com/wixtoolset/wix3/releases/tag/wix3141rtm) and extract it to `tools\wix`.
- For publishing: [GitHub CLI](https://cli.github.com/) (`winget install GitHub.cli`), signed in with `gh auth login`.

## Build

```powershell
.\build.ps1 -Version 2.8.1
```

Two MSIs go to `dist\`: `KrabbzDev-BusyLightz2.0-<version>.msi` (installs just for the user) and `BusyLightz-Setup-<version>.msi` (installs for everyone in `C:\Program Files\BusyLightz`). **Use a new version number for every build you install or publish**, so installs and automatic updates replace the previous version cleanly.

| Parameter | Default | Purpose |
| --- | --- | --- |
| `-Version` | required | Version shown in the app and used by the installer and updater |
| `-UpdateRepo` | `krabbzdev/KrabbzDev-BusyLightz2.0` | GitHub repo the app checks for updates; empty disables updates |
| `-GraphClientId`, `-GraphTenantId` | ShowTech's registration | Built-in Teams app registration; pass empty strings to build without one |
| `-Publish` | off | Also publish GitHub release `v<Version>` (the per-user MSI) and `v<Version>-allusers` (the all-users MSI) |
| `-Notes` | none | Release notes as Markdown list items; the app shows them in its *Updated to version x* card |

## Pull request check and auto-merge

Every pull request into `main` is compiled by the workflow `.github/workflows/pr-check.yml` (check name: **Compile the app**), on a Windows machine in the cloud. It takes about a minute and builds only; nothing is published. A pull request that doesn't compile shows a red cross.

To have GitHub merge a pull request by itself once that check is green, switch on these two settings (repo admin only, once):

1. **Settings > General**: tick **Allow auto-merge**.
2. **Settings > Branches > Add branch ruleset** (or *Add classic branch protection rule*) for `main`: tick **Require status checks to pass** and add **Compile the app**.

Then press **Enable auto-merge** on a pull request. It merges when the check passes, and never if it fails. Without the required check in step 2, auto-merge has nothing to wait for and merges at once. Merging doesn't release anything: publish from **Actions > Build and release > Run workflow** when you want to.

## Build and release from GitHub (no Windows PC needed)

The workflow `.github/workflows/release.yml` does the same as `build.ps1`, on a Windows machine in the cloud.

1. On GitHub, open **Actions > Build and release > Run workflow**.
2. Pick the **main** branch, type the version (e.g. `2.32.3`), and optionally release notes (separate items with `\n`).
3. Leave **Publish** off to build only: the installers are kept as a download on the run page for 14 days, so you can install and test them first. Turn it on to publish the releases, and installed copies then update themselves.

Pushing a tag like `v2.32.3` also builds and publishes it, as long as the tagged commit is already on `main`. Publishing only ever runs from `main`.

It uses WiX 3.14 (already on the runner, downloaded if not) and GitHub's built-in token, so there are no secrets to set up. The first run needs **Settings > Actions > General > Workflow permissions** set to *Read and write* if the repo's default is read-only.

## Publish a release (from your own PC)

```powershell
.\build.ps1 -Version 2.8.1 -Publish -Notes "- Fixed this`n- Added that"
```

Write the notes for people using the app: the update card shows about the first 200 characters, and clicking it opens the release page.

This creates release `v2.8.1` with the per-user installer attached as `KrabbzDev-BusyLightz2.0.msi`, a fixed name so the README's *latest/download* link always works. Copies named `Krabbz9999-BusyLightz2.0.msi` and `Krabbz9999-Blynclight-App-Control.msi` keep links from before the rename working.

It also creates release `v2.8.1-allusers` with the all-users installer as `BusyLightz-Setup.msi`, **never marked latest**. Copies installed just for one user install the last `.msi` in the latest release, so they must never see the all-users one. The all-users background updater and `scripts/Install-BusyLightz.ps1` find it by tag.

The per-user exe (`Krabbz9999 Blynclight App Control.exe`), its install folder, the settings folder and the registry key keep their original names on purpose: installed copies relaunch themselves by exe path after updating, so renaming them would leave updated PCs not running. Per-user copies pick up a release within 6 hours, or right away with **Check for updates**; all-users copies within 4 hours (the BusyLightz Updater task).

GitHub records a SHA-256 digest for each upload. The app, the background updater and the RMM script refuse any download that doesn't match it.

## Project layout

| Path | What it is |
| --- | --- |
| `src/BlyncCore.cs` | USB/HID discovery, Blynclight and Luxafor report formats, light states, color wheel |
| `src/LightController.cs` | Saved state, Party Mode cycling, call flashing, brightness and colors, call and Teams overrides, previews, replug detection. Every light write happens on its own thread |
| `src/TrayApp.cs` | Program entry, logging and freeze watchdog, tray icon, hotkeys, notifications, wiring |
| `src/TrayMenu.cs` | Tray menu drawing (Windows 11 style) |
| `src/ControlForm.cs` | Main window, its three styles, and the shared colors and fonts (`Theme`) |
| `src/DeviceTiles.cs` | The Active Devices tiles (lights, Mutesync Button, K2 USBC Key) and the beta sticker |
| `src/ModernControls.cs` | On/off switch and rounded buttons |
| `src/SetupForm.cs` | Setup checklist (including the email step), Teams app dialog, ignore-apps dialog |
| `src/SettingsForm.cs`, `src/AppSettings.cs` | Settings (its pages, including Light Control) and the stored settings (HKCU), including the sign-in startup entry |
| `src/CallDetector.cs`, `src/AudioSessions.cs` | Call detection from Windows' microphone usage record and from Windows audio; ignore list |
| `src/MicTestForm.cs` | Test microphone window |
| `src/TeamsPresence.cs` | Browser sign-in (auth code + PKCE), encrypted token, Graph presence read/set |
| `src/TeamsAppConfig.cs` | Which Entra app registration to use; PC tenant detection |
| `src/EmbravaConnect.cs` | Detects, quits and restarts Embrava Connect |
| `src/Updater.cs` | Checks GitHub Releases, verifies and silently installs updates |
| `src/AllUsers.cs` | The all-users install: hand-over from per-user copies, the SYSTEM background updater, Install for everyone |
| `src/CrashReport.cs` | Email crash log |
| `src/MuteSyncButton.cs` | Mutesync Button: finds it, mutes and unmutes the Windows mic (`MicMute`), drives its ring |
| `src/K2Key.cs` | K2 USBC Key: reads its presses through raw input, cancels its own mouse clicks with a low-level mouse hook (on its own thread), and runs the macros (`K2Actions`) |
| `src/FeedbackForm.cs` | Send feedback: opens a filled-in GitHub issue |
| `LICENSE` | The BusyLightz Beer-Ware License |
| `installer/Product.wxs` | Per-user MSI (WiX 3) |
| `installer/ProductAllUsers.wxs` | All-users MSI (Program Files), with the BusyLightz Updater scheduled task |
| `scripts/Install-BusyLightz.ps1` | RMM install script: gets, verifies and installs the newest all-users MSI |
| `scripts/New-EntraAppRegistration.ps1` | Creates or updates the Entra app registration |
| `scripts/Blynclight.ps1`, `scripts/BlynclightCore.ps1` | Stand-alone PowerShell command-line control (any color, dim, flash speed) |
| `assets/app.ico` | App icon (generated by `build.ps1` if missing) |

## Light protocols

Both lights are USB HID devices. Each command is one 9-byte output report.

**Embrava Blynclight** (vendor ID `2C0D`, any product):

```
[0x00 report id, Red, Blue, Green, light flags, sound flags, volume, 0xFF, 0x22]
```

Light flags: bit 0 = off, bit 1 = dim, bit 2 = flash, bits 3-5 = flash speed.

**Luxafor Flag** (`04D8:F372`):

```
[0x00 report id, 0x01 static color, 0xFF all LEDs, Red, Green, Blue, 0x00, 0x00, 0x00]
```

Off is color 0,0,0. `BlyncDevice.FindLights()` finds both kinds, and `LightController` sends each light the same color in its own format.

## Button protocols

**Mutesync Button** (`10C4:EA60`, a Silicon Labs CP210x serial bridge that reports itself as *MuteSync Button*): a serial port at 9600 baud, 8N1. Only a device whose USB description says *MuteSync Button* is opened; lots of other gadgets use the same chip.

- The button sends `0x33` when pressed and `0x34` when let go.
- The ring takes 13 bytes: `0x41`, then a red, green, blue triple (0-255) for each of its 4 LEDs. All zeros is off.
- The ring goes dark unless its color is sent again every few seconds, so `MuteSyncButton` resends it every 2 seconds.

**K2 USBC Key** (`04D9:A2D0`, a Holtek chip that reports itself as a gaming mouse): out of the box its buttons are a left and a right mouse click. `K2Key` registers for raw mouse and keyboard input to see which device sent each click, and a `WH_MOUSE_LL` hook cancels clicks whose raw input came from the K2, matched within 250 ms. Both run only while a K2 is plugged in and Disable K2 Functions is off, so a PC without one never has the hook. Keys F13 and F14 from the K2 are taken as its left and right buttons.

---
[Guide home](README.md) · [Installing and Updating](Installing-and-Updating.md) · [Using the Light](Using-the-Light.md) · [Mutesync Button and K2 USBC Key](Mute-Button-and-K2-Key.md) · [Teams Status](Teams-Status.md) · [Call Detection](Call-Detection.md) · [Setup Checklist](Setup-Checklist.md) · [Setting Up Teams for Your Organization](Setting-Up-Teams-for-Your-Organization.md) · [Troubleshooting and FAQ](Troubleshooting-and-FAQ.md) · [For Developers](For-Developers.md)
