# Mutesync Button and K2 USBC Key

BusyLightz can drive two kinds of desk button as well as your lights: a **Mutesync Button** and a **K2 USBC Key** (a two-button macro key). Neither needs its own software. Both show up in the window's **Active Devices** panel, lit when plugged in and greyed out as *Disconnected* when not.

## Tested devices

| Device | Notes |
| --- | --- |
| **Mutesync Button** | Tested with KrabbzDev's own Mutesync Button, which plugs in by USB. The MuteMe app isn't needed. |
| **K2 USBC Key** | Tested with the **Motospeed K2 2 Button RGB Keyboard**. Its own setup software isn't needed. |

## Mutesync Button

Press the button to **mute or unmute your microphone in Windows**, for every app at once: Teams, Zoom, Cytracom and anything else. The light ring shows where you stand:

| Ring | Meaning |
| --- | --- |
| 🔴 Red | Your mic is muted |
| 🟢 Green | Your mic is live |
| ⚫ Off | No microphone, BusyLightz is closed, or the ring is set to light only during calls |

If you mute some other way (from Windows' sound settings, for example) the ring follows within half a second.

**Close the MuteMe app first.** Only one program can use the button at a time. Quit MuteMe, and remove it from startup or uninstall it so it doesn't take the button back after a restart. While MuteMe has it, Settings shows *In use by another app (close the MuteMe app)*.

**Teams' own mute button doesn't change.** BusyLightz mutes the microphone in Windows, so Teams shows its own notice that your mic is muted by the system, but its mute icon stays the same.

### Mutesync Button settings

Open **Settings > Mutesync Button**. At the top, a picture of the button shows its ring in the color your real one is showing right now, and follows it as you mute and unmute. Each change below shows on the ring for a few seconds as you make it.

| Setting | What it does |
| --- | --- |
| **Use the Mutesync button** | On by default: pressing the button mutes and unmutes your mic, and its ring shows which. Turn it off and BusyLightz lets go of the button: pressing it won't mute your mic and its ring goes dark, so you can use the MuteMe app instead. The ring settings are greyed out while it's off. The status beside the picture says *Connected on COM3* (or another port), *Not found*, *In use by another app* or *Turned off*. |
| **Ring brightness** | 5% to 100%. The default, 25%, matches the MuteMe app. |
| **Muted** / **Live (not muted)** | The ring's colors. Red and green by default; click a dot to pick from the same colors as your lights. |
| **Only light the ring while an app is using my mic** | Keeps the ring dark except during calls, so it isn't glowing all day. Off by default. |

Plugging in and unplugging the button, and every mute and unmute, are noted in the app log.

## K2 USBC Key

The K2's two buttons each run an action, and so does pressing **both together**. Pressing both counts once: it doesn't also run the left or right action.

Out of the box the K2's buttons are a left and a right mouse click. BusyLightz catches those clicks before Windows acts on them, so a press only runs your action and doesn't click wherever the pointer is. Clicks from your real mouse aren't affected. While BusyLightz isn't running, the K2 clicks like a mouse again.

### What each button can do

**Use the K2 key** (on by default) lets BusyLightz run the actions you set for the K2's buttons. Turn it off and BusyLightz leaves the K2 alone: no actions run, and its buttons are ordinary left and right mouse clicks again. The actions below are greyed out while it's off.

Pick an action for **Left button**, **Right button** and **Both together** in **Settings > K2 USBC Key**. At the top of that page, a picture of the K2 lights up the key you press on the real one (both keys for Both together), so you can check it's working.

**Test your buttons:** at the bottom of that page, a list shows your last five presses, newest first, each with the time, which button (*Left button*, *Right button* or *Both buttons*) and the action it runs. The list stays put, so you can press a button, look away and come back. If it says the K2 isn't plugged in, or that **Use the K2 key** is off, presses can't be picked up until that's fixed.

| Action | What it does |
| --- | --- |
| **Do nothing** | Nothing. |
| **Type my email address** | Types your email address (below) wherever you're typing. **The default for Both together.** |
| **Type text:** | Types the text you enter in the box beside it. |
| **Press keys:** | Presses a shortcut you enter, e.g. `Ctrl+Shift+M` (Teams' mute) or `Win+D`. |
| **Mute / unmute my mic** | Mutes or unmutes your microphone in Windows, like the Mutesync Button. **The default for Left button.** |
| **Switch Busy / Available** | Switches your lights between Busy and Available. **The default for Right button.** |
| **Busy (red)**, **Available (green)**, **On a call**, **Follow Teams Status**, **Party Mode** | Sets that mode, like the buttons in the window. |
| **Open:** | Opens a program, file or web address you enter, e.g. `https://teams.microsoft.com`. |

### If your two-button key isn't found

BusyLightz recognizes the K2 by its hardware ID (USB `VID_04D9&PID_A2D0`). Some two-button keys that look the same report a different ID, so the page says *Not plugged in* even though the key is.

1. Go to **Settings > K2 USBC Key** and click **My key isn't found...**.
2. Press the buttons on your key. Each device that sends a button press or a key appears in the list with its hardware ID and what it sent (*Left button*, *Right button*, or a key name). Your normal mouse and keyboard show up too: ignore them.
3. Select the row that appeared when you pressed your key and click **Use this key**.
4. Close the window and press a button. **Test your buttons** on the same page should list it, and the K2 tile in the window lights up.

**Copy details** puts the list on the clipboard to paste into an email, and **Forget added keys** goes back to recognizing only the original K2. A key that sends ordinary letters or numbers instead of mouse clicks (or F13 and F14) can't be used yet: copy the details and send them with the key's make and model.

### Your email address

**My email address** is what *Type my email address* types. Until you set it, it's the example address `YourEmailHere@spacex.com`. The [Setup Checklist](Setup-Checklist.md#7-your-email-address) asks for it, or change it any time in **Settings > K2 USBC Key**.

### F13 and F14

If you do set up the K2 with its own software, set its buttons to **F13** (left) and **F14** (right). BusyLightz understands those keys too, and almost no other program uses them.

---
[Guide home](README.md) · [Installing and Updating](Installing-and-Updating.md) · [Using the Light](Using-the-Light.md) · [Mutesync Button and K2 USBC Key](Mute-Button-and-K2-Key.md) · [Teams Status](Teams-Status.md) · [Meeting Alerts](Meeting-Alerts.md) · [Call Detection](Call-Detection.md) · [Setup Checklist](Setup-Checklist.md) · [Setting Up Teams for Your Organization](Setting-Up-Teams-for-Your-Organization.md) · [Troubleshooting and FAQ](Troubleshooting-and-FAQ.md) · [For Developers](For-Developers.md)
