# Call Detection

**Whenever any app is using your microphone, the light flashes red and blue**, so people can tell at a glance that you're on a call rather than just busy. When the app releases the microphone (you hang up or leave the meeting), the light goes back to what it was showing before.

Prefer a steady red during calls? Open **Settings** (⚙ in the window, or the tray menu) and switch off **Flash red and blue instead of steady red**.

This covers Teams, Zoom, Webex, Google Meet or any call in a browser, and softphones like Cytracom. No sign-ins or API keys are involved.

## How it works

Windows keeps a record of which apps use the microphone (it's what shows the microphone icon in the taskbar). The app checks that record about once a second. On some PCs that record misses an app even while Windows Settings shows it using the microphone, so the app also asks Windows audio which apps are recording right now. Either one counts. When an app has the microphone open, the light starts flashing and the window says which app, e.g. **On a call (Zoom)**. The tray icon and the window flash the same two colors as the lights during a call, at your On a call speed but never faster than twice a second (fast flashing over a large area can trigger seizures).

- **A short burst isn't a call.** An app has to keep the microphone in use for **10 seconds** (the default) before it counts as a call, so dictation and voice typing (Wispr Flow, Windows voice typing, a voice search) don't flip your lights or your Teams status each time you speak. Change it in **Settings > Calls > Count it as a call after the mic is in use for**: *Right away*, 5, 10, 15 or 30 seconds. Once you're on a call, hanging up shows straight away, and another app taking the microphone over counts at once. The wait applies to the lights, your Teams status and the tray icon alike.
- **Mute doesn't count as hanging up.** Most calling apps keep the microphone open while you're muted, so the light keeps flashing.
- **If you pick a color during a call,** your choice wins until that call ends. Going back to **Follow Teams Status** during a call (by hand, or when an override runs out) brings the call colors back.
- **When connected to Teams,** calls in other apps also set Teams to *Busy, In a call*. See [Teams Status](Teams-Status.md#busy-in-teams-during-other-calls).

## Ignoring apps

Ignored apps are matched by the name of their program (for example `Wispr Flow.exe`), not by the folder it's in. Apps like Wispr Flow, Slack and Discord install every update into a new folder, and they stay ignored after each one. Ticking an app ignores every version of it, and ignoring a program by name also ignores another program with the same file name in a different folder.

Some apps use the microphone without being on a call, for example:

- voice typing or dictation,
- screen recorders such as Loom,
- noise-cancelling tools such as Krisp or NVIDIA Broadcast, which keep the microphone open all the time and would keep the light red.

To ignore them:

1. Open **Settings** and click **Ignore apps...** next to *Turn lights red when my mic is in use*. (It's also in **Test microphone...**.)
2. Tick the apps that shouldn't turn the light red. The list shows every app Windows has seen use the microphone on this PC, and when each last used it.
3. Click **Save**.

Ignored apps also don't set Teams to Busy.

## Test microphone

If a call isn't turning the lights red, open **Settings > Test microphone...** (also in the Setup Checklist) and start a call in that app. The window refreshes every second and shows:

- **Whether Windows lets apps use the microphone.** If *Microphone access* or *Let desktop apps access your microphone* is off, it says so, with a button to open the right Windows settings page. While one is off, Windows doesn't report the call.
- **Every app Windows has seen use the microphone**, with *In use now* for apps using it right now, *(ignored)* for apps on your ignore list, and the path Windows recorded it under.
- **What the app makes of it**, e.g. *Cytracom is using the microphone, so the app sees a call right now.*

If the calling app never shows *In use now* during a call, Windows isn't reporting it, so the fix is on the Windows side. Common causes: a microphone privacy switch is off, the app runs as administrator, or the call is really in a browser (it shows as Chrome or Edge) or in a remote desktop or Citrix session.

## Testing the light

- **Test the light** in the Setup Checklist shows the call color (flashing, or steady red) for 3 seconds.
- Or open the Windows Sound Recorder, or test your microphone in Teams or Zoom settings. The light should flash while the microphone is open.

---
[Guide home](README.md) · [Installing and Updating](Installing-and-Updating.md) · [Using the Light](Using-the-Light.md) · [Mutesync Button and K2 USBC Key](Mute-Button-and-K2-Key.md) · [Teams Status](Teams-Status.md) · [Meeting Alerts](Meeting-Alerts.md) · [Call Detection](Call-Detection.md) · [Setup Checklist](Setup-Checklist.md) · [Setting Up Teams for Your Organization](Setting-Up-Teams-for-Your-Organization.md) · [Troubleshooting and FAQ](Troubleshooting-and-FAQ.md) · [For Developers](For-Developers.md)
