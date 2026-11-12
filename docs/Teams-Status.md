# Teams Status

The app can show your **Microsoft Teams status** on the light and set Teams to **Busy** while you're on calls in other apps.

## Connect to Teams (once)

1. Right-click the tray icon > **Connect to Microsoft Teams** (or click **Connect** in the [Setup Checklist](Setup-Checklist.md)).
2. Your browser opens the normal Microsoft sign-in page. Sign in with your **work account**, including MFA if asked.
3. When the browser says *"Sign-in finished. You can close this tab."*, close it. A notification confirms *"Connected to Teams as you@company.com"*.

You stay connected from then on, including after restarts and updates. To disconnect, click **Connect to Microsoft Teams** again (it's checked while connected).

**What the app can do with this sign-in:** read and set *your own* Teams presence, and nothing else. It can't read your mail, chats, calendar or files. The sign-in is stored encrypted, so only your Windows account can use it.

If **Connect** fails because Microsoft says the application wasn't found, your organization hasn't set the app up yet. See [Setting Up Teams for Your Organization](Setting-Up-Teams-for-Your-Organization.md).

## Follow Teams Status

**Follow Teams Status** is the default for new installs; you can also choose it anytime in the window or the tray menu. The app checks your Teams status every 20 seconds and sets the light:

| Teams status | Light |
| --- | --- |
| Available | 🟢 Green |
| Busy, In a call, In a meeting, Presenting | 🔴 Red |
| Do not disturb | 🟣 Purple |
| Away, Be right back | 🟡 Yellow |
| Offline | ⚫ Off |

The window shows the status, e.g. *Teams: Do not disturb*. These are the default colors; change them in [Light Control](Using-the-Light.md#light-control).

**Busy and Available override Teams.** Picking Busy or Available (or Alt+R / Alt+G) holds that color until the [override time](Using-the-Light.md#override-timer) runs out (15 minutes by default), or until you click **Follow Teams Status** again.

## Busy in Teams during other calls

While you're connected, when another app uses your microphone (a Cytracom phone call, Zoom, Webex and so on), the app sets your Teams status to **Busy, "In a call"**, so coworkers see it in Teams too. When the call ends, it clears that status and Teams goes back to normal.

- Teams calls and meetings aren't changed; Teams already shows those itself.
- If the app or PC stops mid-call, Microsoft drops the Busy status on its own within 5 minutes.
- Apps on your [ignore list](Call-Detection.md#ignoring-apps) don't set Busy.

## Teams matches what you pick

While you're connected, picking **Busy** or **On a call** (in the window, the tray menu or with Alt+R) also sets your Teams status to **Busy**, and picking **Available** (Alt+G) sets it to **Available**, just as if you'd picked them in Teams. Available wins even if a meeting on your calendar would otherwise show you as Busy, but not while the app sees you on a call. Going back to **Follow Teams Status**, or picking Party Mode or Disable Lights, puts Teams back to automatic. So does the [override timer](Using-the-Light.md#override-timer) running out.

- On by default. Turn it off with **Match my Teams status when I pick Busy, Available or On a call** in [Settings](Using-the-Light.md#settings).
- **Do Not Disturb instead of Busy:** the app sets *Do not disturb* wherever it would have set *Busy*: when you pick Busy or On a call, and during calls in other apps. It's on by default. Do Not Disturb also silences Teams notifications, except from your priority contacts, so if you'd rather show *Busy*, untick **Use Do Not Disturb instead of Busy** in Settings > Microsoft Teams. This includes your own Teams calls, which would otherwise show *In a call*.
- If the app or PC stops, Teams goes back to automatic on its own within an hour. The app renews the status every 30 minutes while you stay on it.
- If you'd picked a status in Teams yourself (say, Do not disturb), it's reset to automatic afterwards.

## Status message while your lights are red

With **Set my Teams status message while my lights are red** on in [Settings](Using-the-Light.md#settings), coworkers see your chosen message next to your name in Teams whenever your lights are red: when you pick **Busy** or **On a call**, or when the app [detects a call](Call-Detection.md). It's on by default, showing *On a video call*; pick another message there, or switch it off.

- **Messages:** *On a video call*, *In a meeting*, *On a phone call*, *Heads down, please message me*, *Back in a few minutes*, or **Custom message...** to type your own (up to 280 characters).
- **" - responses may be delayed" is added** to the end of every message, so *On a video call* shows in Teams as *On a video call - responses may be delayed*. It's added to your own custom message too (unless it already ends that way), and a long custom message is shortened a little to make room, because Teams allows 280 characters in all.
- **When the lights stop being red,** the message is taken away. If you had your own status message before, it's put back.
- **If you change the message in Teams** while the lights are red, the app leaves your change alone afterwards.
- **If the app or PC stops,** Microsoft removes the message on its own within an hour. The app renews it every 30 minutes while the lights stay red.
- It needs Teams connected (above). It uses the same sign-in and permission, so there's nothing extra to approve.

## Turning Teams off

To stop BusyLightz talking to Teams without signing out, tick **Disable Teams Integration** in the tray menu, or in **Settings > Microsoft Teams**.

- The app stops reading your Teams status, setting **Busy** during other calls, and setting a status message. Anything it had set is put back, and your own status message returns.
- Your lights and call detection keep working. Follow Teams Status shows *Teams integration disabled* instead of a status.
- Your Teams sign-in is kept, so unticking it picks up again within a few seconds.
- It's remembered after a restart.

The lights have the matching switch: **Disable USB Light Integration** (see [Using the Light](Using-the-Light.md#the-tray-menu-right-click-the-tray-icon)).

## Without connecting to Teams

You can still use the light without connecting. **Follow Teams Status** then hands the light to **Embrava Connect**, if it's installed, which shows your Teams status its own way. Embrava Connect only drives Blynclights, so any Luxafor lights keep their last color in this mode. Once you're connected, Embrava Connect isn't needed. Quit and uninstall it so the two apps don't fight over the light (the [Setup Checklist](Setup-Checklist.md) helps).

If you go back to **Follow Teams Status** without being connected, and Embrava Connect isn't installed either, the app shows a notification: *"Not connected to Microsoft Teams, so your Teams status isn't showing on the light."* It says what to do next: open the Setup Checklist if your organization's Teams app isn't set up yet, or use **Connect to Microsoft Teams** in the tray menu.

---
[Guide home](README.md) · [Installing and Updating](Installing-and-Updating.md) · [Using the Light](Using-the-Light.md) · [Mutesync Button and K2 USBC Key](Mute-Button-and-K2-Key.md) · [Teams Status](Teams-Status.md) · [Call Detection](Call-Detection.md) · [Setup Checklist](Setup-Checklist.md) · [Setting Up Teams for Your Organization](Setting-Up-Teams-for-Your-Organization.md) · [Troubleshooting and FAQ](Troubleshooting-and-FAQ.md) · [For Developers](For-Developers.md)
