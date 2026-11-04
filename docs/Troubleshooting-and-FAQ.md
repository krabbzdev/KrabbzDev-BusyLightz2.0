# Troubleshooting and FAQ

## The light doesn't change at all

- Check the window: **Light not found** means no light is detected, and the *Lights:* line shows which ones are. Unplug the light and plug it back in, or try another USB port. The app finds it again within a couple of seconds.
- **Try a different cable.** Charge-only cables power the light (it may glow when plugged in) but Windows can't see it. Use the cable that came with the light, or one that can transfer files to a phone.
- Is the app running? Look for its colored dot in the system tray. If it's not there, start it from the Start menu.
- Click **Test the light** in the [Setup Checklist](Setup-Checklist.md).

## One of several lights stays dark or doesn't match

The app sends every color to every light it can see, so a light that stays dark has usually dropped off USB. Check the *Lights:* line in the window. If that light isn't counted, reseat its plug or swap the cable. It's matched automatically as soon as it reconnects.

## The light keeps changing back on its own

Embrava Connect is probably still running and fighting the app. Quit it (Setup Checklist > **Quit it**) and uninstall it from **Settings > Apps**. If you're not connected to Teams yet, see [Teams Status](Teams-Status.md#without-connecting-to-teams).

## The light stays red when I'm not on a call

An app is holding your microphone open. The window says which one: *On a call (AppName)*.

- If it's a call app you have open, close the call or the app.
- If it's a tool that always keeps the mic open (noise cancelling, dictation, a recorder), add it to the [ignore list](Call-Detection.md#ignoring-apps).

## The light didn't go red on a call

Start a call, then open **Settings > Test microphone...**. It shows live what Windows reports about the microphone, which is all the app can see. See [Test microphone](Call-Detection.md#test-microphone).

- The calling app must use the microphone. Calls through a desk phone aren't detected.
- Check the app isn't on the [ignore list](Call-Detection.md#ignoring-apps).
- Check Windows lets apps use the microphone: Windows Settings > Privacy & security > Microphone, with **Microphone access** and **Let desktop apps access your microphone** on. Test microphone says if either is off.
- A call app running as administrator, in a browser (it shows as Chrome or Edge), or in a remote desktop or Citrix session may not show up as itself, or at all.

## Follow Teams Status doesn't follow my Teams status

- Are you connected? Tray menu > **Connect to Microsoft Teams** should be checked.
- Changes can take up to 20 seconds to show.
- Did you pick Busy or Available since? Those override Teams until the override time runs out. Click **Follow Teams Status** to go back now.

## "Connect to Microsoft Teams" fails

- **Microsoft says the application wasn't found** (in the browser): your organization hasn't set the app up. Ask IT; see [Setting Up Teams for Your Organization](Setting-Up-Teams-for-Your-Organization.md).
- **"Sign-in timed out":** the sign-in wasn't finished in the browser within 5 minutes. Try again.
- **You need admin approval:** IT needs to grant admin consent on the app registration.

## Alt+R or Alt+G doesn't work

Another program already uses that key combination; the app tells you when it starts. Use the window or tray menu instead.

## The app didn't update

- Click **Check for updates** in the window or tray menu. It says *"You're on the latest version"* if there's nothing newer.
- Updates only arrive once they're published on the [Releases](https://github.com/krabbzdev/KrabbzDev-BusyLightz2.0/releases) page.
- Update logs are in `%LOCALAPPDATA%\Krabbz9999 Blynclight\updates`.
- **Installed for everyone on the PC** (`C:\Program Files\BusyLightz`): updates come from the **BusyLightz Updater** task at startup and every 4 hours, not from the app. **Check for updates** only says whether a newer version is out. The updater's log is `C:\Program Files\BusyLightz Updates\updater.log`.

## If the app freezes

Version 2.24.0 fixed the most likely cause: a light that stopped answering over USB could freeze the whole app. Writes to the lights now happen in the background and give up after a second, so a misbehaving light can't hold the app up. Update with tray menu > **Check for updates**.

If it still freezes, wait a minute before forcing it closed, then start it again and send the log (below). The log notes when the app stopped responding, and at the next start, when it was last seen running.

## If the app crashes

The app writes any unexpected error to its log, with the version and Windows version, and keeps running when it can. To send the log:

1. Start the app again (Start menu), then click **Email crash log...** in Settings or the tray menu.
2. A new email to clove@showtechsolutions.com opens with the log attached. Add what you were doing when it crashed, and send it.

With classic Outlook and most desktop mail apps, the log is attached for you. With the new Outlook or webmail, the folder with the log file opens next to the email: drag the file into the email.

The log is `%LOCALAPPDATA%\Krabbz9999 Blynclight\app.log`. It holds error messages, such as Teams or light errors, each time the app started and closed, any time it stopped responding, and sometimes the text of a Teams status message you set yourself. It never holds passwords or sign-in tokens. You can open the attached file and read it before sending.

The attached file also includes:

- **Windows crash records** for the app from the last 30 days. These catch crashes the app can't log itself.
- **Updates downloaded**, with dates. Installing an update closes and reopens the app, which can look like a crash.
- **Microphone details**: Windows' microphone switches and the apps it has seen use the mic, for when calls aren't detected.

## Windows says the installer is from an unknown publisher

The installer isn't code-signed yet. Click **More info > Run anyway**.

## Too many notifications

Turn them off in **Settings** (⚙ in the window, or tray menu > **Settings...**).

## Is my data safe?

- The app only reads and sets **your own Teams presence**. It can't see mail, chats, files or calendars.
- Your Teams sign-in is stored encrypted, usable only by your Windows account. **Connect to Microsoft Teams** (when checked) signs out and deletes it.
- Call detection only asks Windows *which apps* are using the microphone, never any audio.
- The only internet connections are to Microsoft (Teams status) and GitHub (updates).

## The Mutesync Button doesn't mute, or its ring stays dark

- Check **Settings > Mutesync Button**. *In use by another app* means the MuteMe app has the button: quit it, and remove it from startup or uninstall it. *Not found* means Windows doesn't see the button: unplug it and plug it back in.
- Make sure **Mute and unmute my mic with a Mutesync Button** is on.
- If **Only light the ring while an app is using my mic** is on, the ring is dark between calls. That's expected.
- The button mutes your mic in Windows. Teams' own mute icon doesn't change, but Teams shows that your mic is muted by the system.

## The K2 USBC Key doesn't do anything, or it clicks

- Check **Settings > K2 USBC Key** says *Connected*, and that each button has an action other than **Do nothing**.
- While BusyLightz isn't running, the K2's buttons are ordinary left and right mouse clicks. Once BusyLightz is running, its clicks are cancelled.
- *Type my email address* and *Type text* type into whatever has the keyboard focus. Click into the box you want first.

## How do I send an idea or report a problem?

Use **Send feedback...** in the tray menu or at the bottom of Settings. It opens a filled-in issue on the project's GitHub page, for you to submit. See [Send feedback](Using-the-Light.md#send-feedback).

## Can I pin it to the Start menu or taskbar?

Yes, but you have to do it yourself (Windows doesn't let installers do it): right-click the Start menu entry > **Pin to Start** or **Pin to taskbar**.

---
[Guide home](README.md) · [Installing and Updating](Installing-and-Updating.md) · [Using the Light](Using-the-Light.md) · [Mutesync Button and K2 USBC Key](Mute-Button-and-K2-Key.md) · [Teams Status](Teams-Status.md) · [Call Detection](Call-Detection.md) · [Setup Checklist](Setup-Checklist.md) · [Setting Up Teams for Your Organization](Setting-Up-Teams-for-Your-Organization.md) · [Troubleshooting and FAQ](Troubleshooting-and-FAQ.md) · [For Developers](For-Developers.md)
