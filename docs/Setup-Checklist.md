# Setup Checklist

The Setup Checklist walks you through getting everything working. It opens by itself the first time the app runs, and anytime from the tray menu > **Setup checklist...**.

Each step shows:

- ✓ **green check**: done
- **! orange**: needs doing; there's a button to fix it
- **– gray dash**: nothing to do yet, or not needed

It re-checks every couple of seconds, so steps tick themselves off as you go.

## The steps

### 1. Light plugged in
Checks that at least one Blynclight or Luxafor Flag is connected, and lists what it found (e.g. *Found: Blynclight + 2 Luxafor*). If none, plug one in with a data cable and click **Check again**.

### 2. Your organization's Teams app
The app needs an "app registration" in your organization's Microsoft 365 before it can read Teams status.

- ✓ **Already set up**: your PC belongs to an organization the app already works with, or your IT team has entered your organization's details. Nothing to do. **Change...** lets you enter different details.
- **! Needs setting up**: click **Set up...**. See [Setting Up Teams for Your Organization](Setting-Up-Teams-for-Your-Organization.md).

### 3. Connect to Microsoft Teams
Click **Connect** and sign in with your work account. See [Teams Status](Teams-Status.md).

### 4. Close Embrava Connect
Once you're connected to Teams, this app replaces Embrava Connect, and running both makes them fight over the light.

- Click **Quit it** to close Embrava Connect now.
- Then uninstall it from **Settings > Apps** so it doesn't start again at sign-in.
- Until you're connected to Teams, keep Embrava Connect; this step shows a dash.

### 5. Light follows your Teams status
This is on by default for new installs. If it shows **Turn on**, click it to switch back to Follow Teams Status. Picking Busy, Available or another mode overrides it until the override time runs out, or until you turn it back on.

### 6. Red while your microphone is in use
On unless you switch off *Turn lights red when my mic is in use* in Settings. **Test microphone...** shows live which apps Windows reports using the microphone, and has **Ignore apps...** for apps whose microphone use shouldn't turn the light red. The step shows **!** if Windows is blocking apps from the microphone. See [Call Detection](Call-Detection.md).

### 7. Your email address
The address the [K2 USBC Key](Mute-Button-and-K2-Key.md#your-email-address) types when you press both its buttons. Type it in the box and click **Save**. Until you save one, the step shows **!** and the key types the example address `YourEmailHere@spacex.com`. You can change it later in **Settings > K2 USBC Key**.

### Bottom buttons
- **Test the light**: shows the call colors (flashing red and blue, or steady red) for 3 seconds so you can see the light respond.
- **Done**: closes the checklist. Reopen it anytime from the tray menu.

---
[Guide home](README.md) · [Installing and Updating](Installing-and-Updating.md) · [Using the Light](Using-the-Light.md) · [Mutesync Button and K2 USBC Key](Mute-Button-and-K2-Key.md) · [Teams Status](Teams-Status.md) · [Call Detection](Call-Detection.md) · [Setup Checklist](Setup-Checklist.md) · [Setting Up Teams for Your Organization](Setting-Up-Teams-for-Your-Organization.md) · [Troubleshooting and FAQ](Troubleshooting-and-FAQ.md) · [For Developers](For-Developers.md)
