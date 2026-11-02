# Call Detection

**Whenever any app is using your microphone, the light flashes red and blue quickly**, so people can tell at a glance that you're on a call rather than just busy. When the app releases the microphone (you hang up or leave the meeting), the light goes back to what it was showing before.

Prefer a steady red during calls? Open **Settings** (⚙ in the window, or the tray menu) and untick **Flash red and blue instead of steady red**.

This covers Teams, Zoom, Webex, Google Meet or any call in a browser, and softphones like Cytracom. No sign-ins or API keys are involved.

## How it works

Windows keeps a record of which apps use the microphone (it's what shows the microphone icon in the taskbar). The app checks that record about once a second. When an app has the microphone open, the light starts flashing and the window says which app, e.g. **On a call (Zoom)**. The tray icon and the window's color swatch show plain red during a call.

- **Mute doesn't count as hanging up.** Most calling apps keep the microphone open while you're muted, so the light keeps flashing.
- **If you pick a color during a call,** your choice wins until that call ends.
- **When connected to Teams,** calls in other apps also set Teams to *Busy, In a call*. See [Teams Status](Teams-Status.md#busy-in-teams-during-other-calls).
- **An app only shows up after it has used the microphone at least once** on that PC.

## Ignoring apps

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
[Guide home](README.md) · [Installing and Updating](Installing-and-Updating.md) · [Using the Light](Using-the-Light.md) · [Teams Status](Teams-Status.md) · [Call Detection](Call-Detection.md) · [Setup Checklist](Setup-Checklist.md) · [Setting Up Teams for Your Organization](Setting-Up-Teams-for-Your-Organization.md) · [Troubleshooting and FAQ](Troubleshooting-and-FAQ.md) · [For Developers](For-Developers.md)
