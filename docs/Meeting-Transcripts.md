# Teams Meeting Transcripts

BusyLightz can save a copy of the transcript Microsoft Teams makes of your Teams meetings, as a text file on your PC, and tell you when it's ready. It's **off by default**, and **for Teams meetings only**.

**BusyLightz doesn't record anything and doesn't start transcription.** Teams makes the transcript, and only when someone turns transcription on in that meeting (Teams tells everyone in the meeting when it does). BusyLightz saves a copy of what Teams already made, for meetings you were invited to, which Microsoft lets you read anyway. It never listens to your microphone or your calls.

## Turn it on (once)

1. **Connect to Microsoft Teams** and allow **calendar access** first (see [Meeting Alerts](Meeting-Alerts.md)).
2. Open **Settings > Transcripts** and click **Allow access...**. Microsoft asks in your browser.
3. Turn on **Save a transcript of my Teams meetings**.

> **Your IT admin has to approve it.** Reading meeting transcripts is a permission Microsoft only lets an administrator of your Microsoft 365 approve (`OnlineMeetingTranscript.Read.All`). If you get *You need admin approval*, ask them: see [Setting Up Teams for Your Organization](Setting-Up-Teams-for-Your-Organization.md#transcripts-optional). An admin can also turn off access to transcripts for apps altogether; the page says so if they have.

## What you get

- A few minutes after a Teams meeting ends, if Teams made a transcript of it, a notice says **Transcript ready** with the meeting's name. Click it to open the file.
- Files are saved in **Documents\BusyLightz Transcripts** (the **Open transcripts folder** button opens it), named like `2026-11-20 0930 - Weekly planning.txt`.
- Each file starts with the meeting's title, time and organizer, then lines like `[00:01:23] Sam Rivera: Let's start with the budget.`
- Each transcript is saved once. A meeting transcribed in two parts gives two files.

## Good to know

- **Only meetings with a transcript.** If nobody turned transcription on, there's nothing to save.
- **Only Teams meetings.** Zoom, phone and other calls aren't included. This feature never records them.
- **Files are plain text on your PC,** not encrypted. They're yours to keep, share or delete like any other file. How long you keep transcripts, and who you share them with, may be covered by your organization's or your clients' rules (for example about recordings and personal information): ask whoever looks after that for your company if you're not sure.
- **Microsoft decides what you can read.** You can only get transcripts of meetings you were invited to, and only while Microsoft still keeps them.
- It checks every few minutes, for meetings that ended in the last 6 hours.

## If it doesn't work

- **The page says *Transcript access isn't allowed yet*:** click **Allow access...** and accept in the browser. If Microsoft says it needs admin approval, ask your admin.
- **It says your organization turned off access to transcripts:** that's a Teams admin setting (*Graph API access to transcripts*). Only they can change it.
- **No notice after a meeting:** was transcription on in the meeting? Is **Save a transcript of my Teams meetings** on? Did you decline the meeting? Give it 5 to 10 minutes after the meeting ends.

---
[Guide home](README.md) · [Meeting Alerts](Meeting-Alerts.md) · [Teams Meeting Transcripts](Meeting-Transcripts.md) · [Teams Status](Teams-Status.md) · [Setting Up Teams for Your Organization](Setting-Up-Teams-for-Your-Organization.md) · [Troubleshooting and FAQ](Troubleshooting-and-FAQ.md)
