# Meeting Alerts

A small pop-up shortly before each meeting in your Microsoft 365 calendar, so you don't miss one while you're heads down. It shows the meeting's title, time, place and organizer, and for online meetings (Teams and others) a **Join** button that opens the meeting.

## Turn it on (once)

1. **Connect to Microsoft Teams** first (tray menu, or Settings > Microsoft Teams).
2. Open **Settings > Meeting Alerts** and click **Allow access...**. Microsoft asks in your browser whether BusyLightz may read your calendar. Click **Accept**.
3. That's it. The page says *Calendar access is allowed*, and alerts start about a minute later.

Meeting Alerts is **on by default**, but does nothing until you allow calendar access. When BusyLightz updates to a version that has it, it shows a one-time notice, *New: Meeting Alerts*. Click it to open this page in Settings. It doesn't come back, and isn't shown if you've already allowed calendar access.

**Is it safe?** BusyLightz asks for Microsoft's read-only `Calendars.Read` permission, for your own calendar only. It reads the time, title, place, organizer and join link of upcoming meetings, uses them for the alert, and keeps none of it. It can't change, accept, decline or delete anything. You can take the permission back any time at [myapps.microsoft.com](https://myapps.microsoft.com).

If your organization's IT blocks users from allowing apps themselves, the button says so, and an admin needs to add `Calendars.Read` to the BusyLightz app registration (see [Setting Up Teams for Your Organization](Setting-Up-Teams-for-Your-Organization.md)).

## Today's meetings

Click the BusyLightz icon in the system tray (near the clock) and choose **Today's meetings**, and a small window shows today's calendar.

<img src="images/todays-meetings.png" alt="Illustration of the Today window: the date, then each meeting with its time, title and place, a finished one dimmed, the one on now highlighted, and Join buttons on online meetings" width="420">

- A band at the top shows the product's name, *BusyLightz 2.0*, and *Click here or double-click the tray icon to open the program*. Click it to open the BusyLightz window.
- Each meeting shows its **time**, **title** and **place**. All-day events come first.
- The meeting on **right now** is highlighted and marked *Now*. Finished ones are dimmed.
- Online meetings that haven't finished have a **Join** button.
- **Refresh** reads the calendar again, and **Open my calendar** opens Outlook on the web.
- It closes when you click anywhere else, or press **Esc**.
- A normal click on the tray icon opens the tray menu, the same as a right-click. **Double-click** opens the BusyLightz window.

It uses the same calendar access as the alerts, so if you haven't allowed that yet, the window says so and has a button to the Settings page.

## What the alert shows

- An **icon** at the left: a video camera for online meetings, a calendar for the rest.
- A first line with a **live timer** that ticks every second, like a stopwatch: *Meeting starting in 4:32*. Once the meeting has started it counts up instead: *Meeting started 0:42 ago*.
- The meeting's **title**, its **start and end time**, and its **place** and **organizer** (when it has them).
- **Join** (online meetings only): opens the meeting link in your browser, which hands it to Teams.
- **Snooze 2 min**: shows the alert again in two minutes.
- **Dismiss**: closes it.

The pop-up sits in the bottom right corner of the screen, above other windows, and **doesn't take over the keyboard**, so it won't swallow what you're typing. If several meetings are close together, the alerts stack up. An alert closes by itself 5 minutes after the meeting starts (and never sooner than 5 minutes after it appeared).

Declined and cancelled meetings and all-day events are skipped. Meetings that already started more than 5 minutes ago aren't alerted about (say, when you've only just started BusyLightz).

## Settings

All on the **Meeting Alerts** page of Settings:

| Setting | What it does |
| --- | --- |
| **Alert me before my meetings** | The on/off switch. |
| **Alert me this long before** | 1, 2, 5 (default), 10 or 15 minutes before the meeting starts. |
| **Play a sound** | A short sound with the alert. On by default. It stays quiet while you're on a call. |
| **What the alert says** | The alert's first line. Type your own: `{when}` becomes the live countdown, like *in 4:32* (or *now*) and `{subject}` becomes the meeting's title. The default is `Meeting starting {when}`. **Reset** puts the default back. |
| **Show a test alert** | Shows an example alert, so you can check the wording, sound and place on screen. |

Meeting Alerts keeps working when **Disable Teams Integration** is on, because it reads your calendar and doesn't touch your Teams status. Turning the **Alert me before my meetings** switch off stops it.

## If it doesn't work

- **No alerts at all:** is Settings > Meeting Alerts saying *Calendar access is allowed*? If not, click **Allow access...**. Is **Alert me before my meetings** on? Does your meeting have a start time today, and isn't it declined or all-day?
- **It says Microsoft didn't allow reading your calendar:** click **Allow again...** and accept in the browser. If that fails, your organization's admin may need to approve `Calendars.Read`.
- **Alerts show after the meeting started:** BusyLightz checks the calendar once a minute, so an alert can be up to a minute late. Pick a longer lead time.

---
[Guide home](README.md) · [Installing and Updating](Installing-and-Updating.md) · [Using the Light](Using-the-Light.md) · [Mutesync Button and K2 USBC Key](Mute-Button-and-K2-Key.md) · [Teams Status](Teams-Status.md) · [Meeting Alerts](Meeting-Alerts.md) · [Call Detection](Call-Detection.md) · [Setup Checklist](Setup-Checklist.md) · [Setting Up Teams for Your Organization](Setting-Up-Teams-for-Your-Organization.md) · [Troubleshooting and FAQ](Troubleshooting-and-FAQ.md) · [For Developers](For-Developers.md)
