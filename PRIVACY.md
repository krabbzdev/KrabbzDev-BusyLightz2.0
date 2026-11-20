# Privacy

BusyLightz runs on your PC and talks to your lights, your Microsoft 365 account (only when you connect it) and GitHub (for updates). It has no accounts of its own, no analytics and no tracking. This page says what it reads, what it keeps and where, and where it connects.

This is a plain description of how the app behaves, not legal advice. If you use it at work, check with your own legal or compliance contact before turning on features that involve other people's meetings (see Transcripts below).

## What it reads

| What | Why | Needs |
| --- | --- | --- |
| **Which apps are using your microphone** (not any audio) | To turn the light red during a call | Nothing; it asks Windows which apps hold the microphone |
| **Your own Teams status** (presence), and setting it | To show it on the light, and to show Busy during calls | Connecting to Microsoft Teams (**Settings > Microsoft Teams**) |
| **Your own calendar**: times, titles, places, organizers and join links of your meetings | Meeting Alerts and the My Schedule Today panel | **Allow access** on the Meeting Alerts page |
| **Transcripts Teams made of Teams meetings you were in** | To save a copy for you | Off by default. **Allow access** on the Transcripts page, plus approval from an administrator of your Microsoft 365 |

It can't read your mail, chats or files.

## What it keeps, and where

- **Settings:** in your Windows user's registry, under `HKEY_CURRENT_USER\Software\Krabbz9999\Blynclight App Control`.
- **Your Microsoft sign-in:** a token file in `%LOCALAPPDATA%\Krabbz9999 Blynclight`, encrypted so only your Windows account can open it. **Disconnect** in **Settings > Microsoft Teams** deletes it.
- **Calendar details:** held in memory while the app runs. They aren't written to disk.
- **Transcripts (if you turn them on):** saved as text files in `Documents\BusyLightz Transcripts`, and deleted by BusyLightz after the number of days you pick (7 by default). Copy or move one out of the folder to keep it.
- **The log:** `%LOCALAPPDATA%\Krabbz9999 Blynclight\app.log` records errors, when the app started and stopped, and sometimes the text of a Teams status message you set yourself. It never holds passwords or sign-in tokens.

## Where it connects

- **Microsoft** (`login.microsoftonline.com` and `graph.microsoft.com`), to sign you in and to read and set what's listed above, only after you connect.
- **GitHub** (`github.com` and `api.github.com`), to check for and download new versions of the app.

Nothing is sent anywhere else. **Send feedback** and **Email crash log** open a message or an issue on your screen for you to read and send yourself; BusyLightz sends nothing on its own.

## Teams meeting transcripts

Transcripts are optional and off by default. BusyLightz doesn't record anything and never listens to your calls. It only saves a copy of the transcript Teams already made of a Teams meeting you were invited to. Whether a meeting is transcribed, and whether the people in it have been told, is decided in Teams by your organization, not by BusyLightz. Your organization is responsible for any notices or consent that its rules or local law require; ask your legal or compliance contact.

## Removing it

Uninstall from **Settings > Apps**. Your settings and the files above stay until you delete them; the exact places are listed in [Installing and Updating](docs/Installing-and-Updating.md#where-the-app-keeps-things).
