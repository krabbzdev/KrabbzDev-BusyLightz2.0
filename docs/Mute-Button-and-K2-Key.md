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

Open **Settings > Mutesync Button**. Each change shows on the ring for a few seconds as you make it.

| Setting | What it does |
| --- | --- |
| **Mute and unmute my mic with a Mutesync Button** | Turns mutesync support on or off. On by default; it does nothing until a button is plugged in. The status underneath says *Connected on COM3* (or another port), *Not found* or *In use by another app*. |
| **Ring brightness** | 5% to 100%. The default, 25%, matches the MuteMe app. |
| **Muted** / **Live (not muted)** | The ring's colors. Red and green by default; pick from the same colors as your lights. |
| **Only light the ring while an app is using my mic** | Keeps the ring dark except during calls, so it isn't glowing all day. Off by default. |

Plugging in and unplugging the button, and every mute and unmute, are noted in the app log.

## K2 USBC Key

The K2's two buttons each run an action, and so does pressing **both together**. Pressing both counts once: it doesn't also run the left or right action.

Out of the box the K2's buttons are a left and a right mouse click. BusyLightz catches those clicks before Windows acts on them, so a press only runs your action and doesn't click wherever the pointer is. Clicks from your real mouse aren't affected. While BusyLightz isn't running, the K2 clicks like a mouse again.

### What each button can do

Pick an action for **Left button**, **Right button** and **Both together** in **Settings > K2 USBC Key**:

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

### Your email address

**My email address** is what *Type my email address* types. Until you set it, it's the example address `YourEmailHere@spacex.com`. The [Setup Checklist](Setup-Checklist.md#7-your-email-address) asks for it, or change it any time in **Settings > K2 USBC Key**.

### F13 and F14

If you do set up the K2 with its own software, set its buttons to **F13** (left) and **F14** (right). BusyLightz understands those keys too, and almost no other program uses them.

---
[Guide home](README.md) · [Installing and Updating](Installing-and-Updating.md) · [Using the Light](Using-the-Light.md) · [Mutesync Button and K2 USBC Key](Mute-Button-and-K2-Key.md) · [Teams Status](Teams-Status.md) · [Call Detection](Call-Detection.md) · [Setup Checklist](Setup-Checklist.md) · [Setting Up Teams for Your Organization](Setting-Up-Teams-for-Your-Organization.md) · [Troubleshooting and FAQ](Troubleshooting-and-FAQ.md) · [For Developers](For-Developers.md)
