# KrabbzDev-BusyLightz2.0

**One app for your [Embrava Blynclight](https://embrava.com) and [Luxafor Flag](https://luxafor.com) busy lights, together.**

A small Windows tray app that controls USB busy lights directly over USB, so the people around you can see at a glance whether you're free, busy or on a call. Plug in any mix of Blynclights and Luxafor Flags, as many of each as you like, and they all show the same color at the same time. It replaces both Embrava Connect and Luxafor's own app. No vendor accounts needed, and no admin rights to install it just for yourself.

<img src="docs/images/busylightz-window.png" alt="The main window in the Orbit style: a green top with a ring reading Available, From Teams Status, tiles for the connected Blynclight and Luxafor, and a switcher for Busy, Available, On a call and Follow Teams Status" width="440">

📖 **[User guide](docs/README.md)**: every feature and how to use it.

## Why I made this

> The reason I made this app, is I wanted an app to turn my lights red anytime I was on a call. Be it Zoom, Cytracom, Loom, Teams etc. If My MIC is hot, I want the lights red.
>
> — KrabbzDev

## Tested lights

BusyLightz works with, and has been tested on, these two lights:

- **[Embrava Blynclight Plus](https://store.embrava.com/products/blynclight-plus)**
- **[Luxafor Flag 2](https://luxafor.com/product/flag2/)**

> I've built and tested it with these two because they're the ones I have: I use both of them every day, side by side. Other Blynclight and Luxafor models may work too, but I haven't been able to test them.
>
> — KrabbzDev

## ⬇️ [Download the installer (latest version)](https://github.com/krabbzdev/KrabbzDev-BusyLightz2.0/releases/latest/download/KrabbzDev-BusyLightz2.0.msi)

Release notes and older versions: [Releases](https://github.com/krabbzdev/KrabbzDev-BusyLightz2.0/releases)

If BusyLightz saves you a few interruptions, you can [buy me a coffee](https://buymeacoffee.com/krabbzdev).

[![Buy me a coffee](https://img.shields.io/badge/Buy%20me%20a%20coffee-FFDD00?style=for-the-badge&logo=buymeacoffee&logoColor=black)](https://buymeacoffee.com/krabbzdev)

## Features

### Your lights

- **Blynclight and Luxafor Flag together:** every connected light shows the same color, and lights plugged in later are matched within seconds. The window shows a tile for each kind of light that's connected, glowing in its current color.
- **Four modes, one click each:** **Busy** (red), **Available** (green), **On a call** (flashing red and blue, for calls the app can't detect, like a desk phone) and **Follow Teams Status**. Pick them from the window, the tray menu, or anywhere with the hotkeys `Alt+R` (Busy) and `Alt+G` (Available).
- **Override timer:** your pick goes back to Follow Teams Status on its own after 15 minutes, so you never forget one is on. Choose 5 minutes to 4 hours, or turn it off, in Settings. While an override is on, the window shows a countdown and the tray icon gets a small badge.
- **Three window styles:** **Orbit** (the default, shown above), **Color block** and **Fluent list**, each in Light or Dark mode.
- **Light Control:** brightness, how fast On a call flashes, and the color for each status, each shown on your lights as you change it.
- **Party Mode:** cycles quickly through every color on every light, to check they all work and match.

<img src="docs/images/window-styles.png" alt="The three window styles side by side: Orbit on a call in Dark mode, Color block following Teams as Available, and Fluent list set to Busy" width="900">

### Calls

- **Flashes whenever you're on a call:** any app using your microphone (Teams, Zoom, Cytracom, Loom, a browser call and so on) sets the lights flashing red and blue, or steady red if you prefer, then back afterwards. The window and tray icon flash with them.
- **Ignore list:** recorders, noise-cancelling tools and other apps that use the mic without a call can be ignored.
- **Test microphone:** shows live which apps Windows reports using the microphone, for when a call isn't detected.

### Microsoft Teams

Connect once with a browser sign-in to your work account. No client secret is stored, and the sign-in is encrypted to your Windows account.

- **Follow Teams status:** green Available, red Busy or In a call, purple Do not disturb, yellow Away, off when Offline (change the colors in Light Control).
- **Teams matches what you pick:** picking Busy or On a call sets your Teams status to Busy, Available sets it to Available, and going back to Follow Teams Status returns it to normal.
- **Busy in Teams during other calls:** during a Zoom, Cytracom or other non-Teams call, Teams shows you as *Busy, In a call*.
- **Status message while your lights are red:** shows *On a video call* (the default), *In a meeting*, *On a phone call*, *Heads down, please message me*, *Back in a few minutes*, or your own message next to your name in Teams. Your earlier message comes back afterwards.

### Setup and updates

- **Setup checklist** on first run, including setting up the Teams app for your organization.
- **Light or Dark mode**, picked in Settings (Light is the default).
- **Settings window** (⚙ in the app or tray menu): window style, appearance, startup, notifications, hotkeys, override timer, automatic updates, call detection and the Teams options.
- **Notifications** when the lights change.
- **Automatic, silent updates** from this repo's Releases, checked against GitHub's SHA-256 digest before installing, with a card afterwards listing what changed.
- **Install just for you, or for everyone on the PC** in `C:\Program Files\BusyLightz`, with a background updater; IT can deploy it with an RMM script. See [Installing and Updating](docs/Installing-and-Updating.md).
- Starts at sign-in and lives in the system tray, with an icon that matches the lights.
- **Email crash log:** sends the app's log, Windows' crash records and microphone details in one click.

<p>
<img src="docs/images/settings.png" alt="The Settings window: General, Calls and Microsoft Teams cards with on/off switches, and Setup checklist, Help, Light Control and Close buttons" width="330">
<img src="docs/images/light-control.png" alt="The Light Control window: a Disable All Light Functions switch, Brightness and On a call speed sliders, and a color for each status" width="300">
<img src="docs/images/tray-menu.png" alt="The tray menu: a colored dot for each mode, Follow Teams Status, Connect to Microsoft Teams, Settings, Check for updates and Exit" width="230">
</p>

## Install

Run the downloaded `KrabbzDev-BusyLightz2.0.msi`. It installs just for you, with no admin prompt, and starts with Windows. The **Setup checklist** walks you through the rest. The installer isn't code-signed yet, so Windows may show an "unknown publisher" warning: click **More info > Run anyway**.

Updates install themselves silently. Details, IT deployment and uninstalling: [Installing and Updating](docs/Installing-and-Updating.md).

Windows doesn't let installers pin apps to the Start menu. Pin it yourself (right-click the Start menu entry → **Pin to Start**), or push a Start pins policy through Intune/Group Policy on managed machines.

## Guide

| Page | What's in it |
| --- | --- |
| [Installing and Updating](docs/Installing-and-Updating.md) | Download, install, automatic updates, uninstall, deploying to many PCs |
| [Using the Light](docs/Using-the-Light.md) | Supported lights, the window, override timer, tray menu, hotkeys, notifications, Settings |
| [Teams Status](docs/Teams-Status.md) | Connecting to Teams, what each color means, Busy in Teams, status messages |
| [Call Detection](docs/Call-Detection.md) | How the lights know you're on a call, ignoring apps |
| [Setup Checklist](docs/Setup-Checklist.md) | Every step of the checklist explained |
| [Setting Up Teams for Your Organization](docs/Setting-Up-Teams-for-Your-Organization.md) | One-time steps for an IT admin |
| [Troubleshooting and FAQ](docs/Troubleshooting-and-FAQ.md) | Common problems and questions |
| [For Developers](docs/For-Developers.md) | Building, publishing releases, project layout, light protocols |
