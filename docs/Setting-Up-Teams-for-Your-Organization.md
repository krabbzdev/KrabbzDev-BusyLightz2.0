# Setting Up Teams for Your Organization

*For IT admins. Once per organization, about 3 minutes.*

To read and set Teams status, the app signs users in with a **Microsoft Entra app registration** in your organization's Microsoft 365. An admin creates it once, and everyone in the organization uses it.

- **Permissions:** delegated Microsoft Graph permissions `Presence.ReadWrite` (Teams status), plus `Calendars.Read` for [Meeting Alerts](Meeting-Alerts.md), and the basic sign-in scopes (`openid`, `profile`, `offline_access`). Each user can only read and set their own presence, and read only their own calendar. `Calendars.Read` is optional: without it everything works except Meeting Alerts, and it's only asked for when a user turns Meeting Alerts on.
- **No client secret or certificate.** The app is a public client that signs in through the user's browser.
- **Check first:** if an app registration named **Krabbz9999 Blynclight** already exists in your tenant, use it. Don't create a second one. The script below finds and reuses an existing one automatically.

## Option A: Entra admin center

1. Go to **[Entra admin center > App registrations](https://entra.microsoft.com/#view/Microsoft_AAD_RegisteredApps/ApplicationsListBlade)**. Search for *Krabbz9999 Blynclight*; if it exists, skip to step 6.
2. **New registration**:
   - **Name:** `Krabbz9999 Blynclight`
   - **Supported account types:** *Accounts in this organizational directory only* (single tenant)
   - **Redirect URI:** platform **Public client/native (mobile & desktop)**, value `http://localhost`
3. **API permissions > Add a permission > Microsoft Graph > Delegated permissions > `Presence.ReadWrite`** and **`Calendars.Read`** (the second is for Meeting Alerts, and can be left out). You can remove the default `User.Read`.
4. Click **Grant admin consent for (your organization)**.
5. Under **Authentication**, leave **Allow public client flows** set to *No*. Don't create any secrets or certificates.
6. On the app's **Overview** page, copy the **Application (client) ID** and **Directory (tenant) ID**.

## Option B: PowerShell script

[`scripts/New-EntraAppRegistration.ps1`](https://github.com/krabbzdev/KrabbzDev-BusyLightz2.0/blob/main/scripts/New-EntraAppRegistration.ps1) does all of the above, or reuses and updates an existing registration, then prints the two IDs.

Needs the Microsoft Graph PowerShell modules (`Microsoft.Graph.Authentication`, `.Applications`, `.Identity.SignIns`) and an account that can create app registrations and grant consent, such as Global Administrator or Cloud Application Administrator. From a folder containing the script:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\New-EntraAppRegistration.ps1 -OutFile .\entra-ids.json
```

Sign in with the admin account when the Microsoft window opens. It may be behind other windows. Signing in with a regular account fails with *Insufficient privileges* and changes nothing.

## Give the IDs to the app

Pick one:

- **Each user:** **Settings** > **Setup checklist...** > *Your organization's Teams app* > **Set up...**, paste both IDs, then click **Save**. The app checks the tenant ID exists. The directory ID is pre-filled when the PC is joined to Entra.
- **Deploy to everyone:** pass them to the installer, run for each user:
  ```
  msiexec /i KrabbzDev-BusyLightz2.0.msi /qn TEAMSCLIENTID=<client ID> TEAMSTENANTID=<tenant ID>
  ```
  Or, installing for everyone on the PC in Program Files (see [Installing for everyone on a PC](Installing-and-Updating.md#installing-for-everyone-on-a-pc-program-files)), run as SYSTEM, which sets them for every user on the PC:
  ```
  powershell -NoProfile -ExecutionPolicy Bypass -File Install-BusyLightz.ps1 -TeamsClientId <client ID> -TeamsTenantId <tenant ID>
  ```

Then each user clicks **Connect** and signs in with their own work account.

## Helping users ask you

In the **Set up...** dialog, **Copy admin instructions** copies a short version of this page that users can paste into an email or ticket for IT.

## Transcripts (optional)

[Teams Meeting Transcripts](Meeting-Transcripts.md) saves a copy of the transcript Teams makes of a meeting, for the user who turns it on. It's the only part of BusyLightz that needs a permission an administrator must approve, and it's left out unless you ask for it.

- **Permissions:** delegated Microsoft Graph `OnlineMeetings.Read` and `OnlineMeetingTranscript.Read.All`. The second needs admin consent. Users can read the transcripts of meetings they were invited to, and nothing else.
- **With the script:** run [`scripts/New-EntraAppRegistration.ps1`](../scripts/New-EntraAppRegistration.ps1) with `-Transcripts`.
- **By hand:** add both permissions under **API permissions > Microsoft Graph > Delegated permissions**, then **Grant admin consent**.
- **Tenant settings:** Teams has an admin setting, *Graph API access to transcripts*. If it's off, apps get a *403* and BusyLightz says your organization turned it off.
- **Think before you grant it for everyone:** the consent covers every signed-in user of the app, and any program on a PC that uses this app's client ID could ask for a user's transcripts with their sign-in. It only works for users who have also turned the feature on, but you may prefer to approve it for a pilot group first.

---
[Guide home](README.md) · [Installing and Updating](Installing-and-Updating.md) · [Using the Light](Using-the-Light.md) · [Mutesync Button and K2 USBC Key](Mute-Button-and-K2-Key.md) · [Teams Status](Teams-Status.md) · [Meeting Alerts](Meeting-Alerts.md) · [Teams Meeting Transcripts](Meeting-Transcripts.md) · [Call Detection](Call-Detection.md) · [Setup Checklist](Setup-Checklist.md) · [Setting Up Teams for Your Organization](Setting-Up-Teams-for-Your-Organization.md) · [Troubleshooting and FAQ](Troubleshooting-and-FAQ.md) · [For Developers](For-Developers.md)
