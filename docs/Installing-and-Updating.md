# Installing and Updating

## Install

1. **[Download the installer](https://github.com/krabbzdev/KrabbzDev-BusyLightz2.0/releases/latest/download/KrabbzDev-BusyLightz2.0.msi)**. The link always gets the newest version.
2. Run `KrabbzDev-BusyLightz2.0.msi`.
   - **No admin rights needed.** It installs just for you, to `%LOCALAPPDATA%\Programs\Krabbz9999 Blynclight App Control`.
   - **SmartScreen warning:** the installer isn't code-signed yet, so Windows may say *"Windows protected your PC"* or *"unknown publisher"*. Click **More info > Run anyway**.
3. The app opens, along with the **[Setup checklist](Setup-Checklist.md)**.

After installing:

- The app **starts automatically when you sign in to Windows**, with its window open and an icon in the system tray.
- There's a **Start menu** entry called *KrabbzDev-BusyLightz2.0*.
- **Pin it to Start yourself** if you like: right-click the Start menu entry > **Pin to Start**. Windows doesn't allow installers to pin apps.

## Updates

The app keeps itself up to date. (Installed for everyone on the PC, a background updater does this instead: see [below](#installing-for-everyone-on-a-pc-program-files).)

- It checks for a new version **1 minute after it starts, then every 6 hours**.
- To check right now, click **Check for updates** in the window (bottom line) or in the tray menu.
- When there's a new version, the app:
  1. downloads it from this project's [Releases](https://github.com/krabbzdev/KrabbzDev-BusyLightz2.0/releases) page,
  2. checks the download against GitHub's SHA-256 fingerprint and refuses anything that doesn't match,
  3. installs it silently in the background (no installer window, no admin prompt),
  4. reopens itself and shows an *"Updated to version x"* card listing what changed. Click it for the full release notes.

The same card shows after you install a newer version by hand.

The version you're running, and when it was last updated, is shown at the bottom of the window and of Settings, and in the tray menu.

## Uninstall

**Settings > Apps > Installed apps > KrabbzDev-BusyLightz2.0 > Uninstall.** No admin rights needed.

Your settings stay in `%LOCALAPPDATA%\Krabbz9999 Blynclight` and the registry key `HKEY_CURRENT_USER\Software\Krabbz9999\Blynclight App Control`. Delete them too if you want a clean slate.

## Installing for everyone on a PC (Program Files)

From version 2.27.0 there's also an all-users installer, **BusyLightz-Setup.msi**, which installs to `C:\Program Files\BusyLightz\BusyLightz.exe` for every user on the PC. It needs admin rights. It's published in its own release next to each version, tagged e.g. `v2.27.0-allusers` on the [releases page](https://github.com/krabbzdev/KrabbzDev-BusyLightz2.0/releases).

- **IT / RMM:** run [`scripts/Install-BusyLightz.ps1`](../scripts/Install-BusyLightz.ps1) as SYSTEM. It always gets the newest version: it finds it on GitHub, downloads its `BusyLightz-Setup.msi`, checks the SHA-256 GitHub recorded, and installs it silently. It does nothing if the newest version is already installed, so it's safe to run on a schedule. Exit code 0 means installed or already up to date, and 3010 means installed but Windows wants a restart.

  ```
  powershell -NoProfile -ExecutionPolicy Bypass -File Install-BusyLightz.ps1
  ```

  Add `-TeamsClientId <client ID> -TeamsTenantId <tenant ID>` to pre-set your organization's Teams app for everyone. Or install a downloaded copy directly: `msiexec /i BusyLightz-Setup.msi /qn` (with `TEAMSCLIENTID=... TEAMSTENANTID=...`).
- **From the app:** an admin can click **Install for everyone** in Settings (shown in copies installed just for one user). Windows asks for admin approval.

What happens:

- **Copies installed just for one user move over on their own.** Within a few seconds (or at their next start) they open the Program Files copy and uninstall themselves. Settings, the Teams sign-in and the log carry over, since they're kept per user.
- **It starts at every user's sign-in**, for users who have **Start with Windows** on in Settings.
- **Updates install in the background.** A **BusyLightz Updater** scheduled task runs as SYSTEM at startup and every 4 hours, downloads the newest all-users installer from GitHub, checks its SHA-256 against what GitHub recorded, and installs it silently. Each user's copy closes while it installs and reopens afterwards. Its log is `C:\Program Files\BusyLightz Updates\updater.log`.
- **Uninstall** from Settings > Apps > Installed apps > BusyLightz (needs admin). It removes the updater task too.

## Deploying to many PCs (IT)

The installer below is a standard per-user MSI, so you can push it with your RMM tool or Intune. **Run it in the user's context, not as SYSTEM**, because it installs into the user's profile. To install for everyone on the PC instead, use the all-users installer above.

```
msiexec /i KrabbzDev-BusyLightz2.0.msi /qn
```

To pre-set your organization's Teams app (see [Setting Up Teams for Your Organization](Setting-Up-Teams-for-Your-Organization.md)), add the two IDs:

```
msiexec /i KrabbzDev-BusyLightz2.0.msi /qn TEAMSCLIENTID=<Application (client) ID> TEAMSTENANTID=<Directory (tenant) ID>
```

The IDs are kept through later automatic updates.

## Where the app keeps things

| What | Where |
| --- | --- |
| The app | `%LOCALAPPDATA%\Programs\Krabbz9999 Blynclight App Control`, or `C:\Program Files\BusyLightz` when installed for everyone |
| Your chosen color | `%LOCALAPPDATA%\Krabbz9999 Blynclight\state.txt` |
| Teams sign-in (encrypted to your Windows account) | `%LOCALAPPDATA%\Krabbz9999 Blynclight\teams-presence.dat` |
| Error log, if anything went wrong | `%LOCALAPPDATA%\Krabbz9999 Blynclight\app.log` |
| Downloaded updates and update logs | `%LOCALAPPDATA%\Krabbz9999 Blynclight\updates`, or `C:\Program Files\BusyLightz Updates` (with the background updater's `updater.log`) when installed for everyone |
| Settings (ignored apps, notifications, colors, Teams app IDs) | `HKEY_CURRENT_USER\Software\Krabbz9999\Blynclight App Control` |
| Teams app IDs set for everyone by the all-users installer | `HKEY_LOCAL_MACHINE\Software\Krabbz9999\Blynclight App Control` |

---
[Guide home](README.md) · [Installing and Updating](Installing-and-Updating.md) · [Using the Light](Using-the-Light.md) · [Mutesync Button and K2 USBC Key](Mute-Button-and-K2-Key.md) · [Teams Status](Teams-Status.md) · [Call Detection](Call-Detection.md) · [Setup Checklist](Setup-Checklist.md) · [Setting Up Teams for Your Organization](Setting-Up-Teams-for-Your-Organization.md) · [Troubleshooting and FAQ](Troubleshooting-and-FAQ.md) · [For Developers](For-Developers.md)
