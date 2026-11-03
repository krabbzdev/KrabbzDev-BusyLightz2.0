using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Security.Cryptography;
using System.Web.Script.Serialization;

namespace Krabbz.Blynclight
{
    // A newer release found on GitHub, downloaded and checked against GitHub's SHA-256 digest.
    public class UpdateInfo
    {
        public Version Version;
        public string MsiPath;
    }

    // Checks the public GitHub repo's latest release and installs it if it's newer than this build.
    public static class Updater
    {
        static readonly string updateDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Krabbz9999 Blynclight", "updates");

        // Returns the downloaded update, or null when this build is current. Throws on network or verification errors.
        public static UpdateInfo DownloadIfNewer(string repo, Version current)
        {
            // .NET 4.x doesn't enable TLS 1.2 by default; GitHub requires it.
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

            var release = (Dictionary<string, object>)new JavaScriptSerializer()
                .DeserializeObject(Get("https://api.github.com/repos/" + repo + "/releases/latest"));
            Version latest;
            if (!Version.TryParse(((string)release["tag_name"]).TrimStart('v', 'V'), out latest)) return null;
            if (latest <= new Version(current.Major, current.Minor, current.Build)) return null;

            Dictionary<string, object> msi = null;
            foreach (Dictionary<string, object> asset in (IEnumerable)release["assets"])
                if (((string)asset["name"]).EndsWith(".msi", StringComparison.OrdinalIgnoreCase)) msi = asset;
            if (msi == null) throw new InvalidOperationException("Release " + latest + " has no .msi file.");

            string path = Download(msi, updateDir, "KrabbzDev-BusyLightz2.0-" + latest + ".msi");
            return new UpdateInfo { Version = latest, MsiPath = path };
        }

        // The latest release's version, or null if its tag isn't a version.
        public static Version LatestVersion(string repo)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var release = (Dictionary<string, object>)new JavaScriptSerializer()
                .DeserializeObject(Get("https://api.github.com/repos/" + repo + "/releases/latest"));
            Version latest;
            return Version.TryParse(((string)release["tag_name"]).TrimStart('v', 'V'), out latest) ? latest : null;
        }

        // The all-users installer (Program Files) is published in its own release, tagged e.g. v2.27.0-allusers and
        // never marked latest: copies installed just for one user before it existed install the last .msi in the
        // latest release, and must never be handed this one.
        public const string AllUsersMsiName = "BusyLightz-Setup.msi";

        public static string AllUsersTag(Version version) { return "v" + version.ToString(3) + "-allusers"; }

        // The latest version's all-users installer, downloaded into dir and checked like DownloadIfNewer; null when
        // the latest version isn't newer than current (pass null to always get it) or it has no all-users installer.
        public static UpdateInfo DownloadAllUsersIfNewer(string repo, Version current, string dir)
        {
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var serializer = new JavaScriptSerializer();
            var latestRelease = (Dictionary<string, object>)serializer.DeserializeObject(
                Get("https://api.github.com/repos/" + repo + "/releases/latest"));
            Version latest;
            if (!Version.TryParse(((string)latestRelease["tag_name"]).TrimStart('v', 'V'), out latest)) return null;
            if (current != null && latest <= new Version(current.Major, current.Minor, current.Build)) return null;

            Dictionary<string, object> release;
            try
            {
                release = (Dictionary<string, object>)serializer.DeserializeObject(
                    Get("https://api.github.com/repos/" + repo + "/releases/tags/" + AllUsersTag(latest)));
            }
            catch (WebException) { return null; } // Not published for this version.
            foreach (Dictionary<string, object> asset in (IEnumerable)release["assets"])
                if (string.Equals((string)asset["name"], AllUsersMsiName, StringComparison.OrdinalIgnoreCase))
                    return new UpdateInfo { Version = latest, MsiPath = Download(asset, dir, "BusyLightz-Setup-" + latest + ".msi") };
            return null;
        }

        // Downloads a release asset into dir as fileName (clearing older installers there), and returns its path.
        // Only keeps files whose hash matches what GitHub recorded at upload time.
        static string Download(Dictionary<string, object> asset, string dir, string fileName)
        {
            object digest;
            string expected = asset.TryGetValue("digest", out digest) ? digest as string : null;
            if (expected == null || !expected.StartsWith("sha256:"))
                throw new InvalidOperationException(asset["name"] + " has no SHA-256 digest; not installing it.");
            expected = expected.Substring("sha256:".Length);

            Directory.CreateDirectory(dir);
            foreach (var old in Directory.GetFiles(dir, "*.msi")) File.Delete(old);
            string path = Path.Combine(dir, fileName);
            using (var web = NewClient()) web.DownloadFile((string)asset["browser_download_url"], path);

            if (!string.Equals(Sha256(path), expected, StringComparison.OrdinalIgnoreCase))
            {
                File.Delete(path);
                throw new InvalidOperationException("Downloaded update failed its SHA-256 check; deleted it.");
            }
            return path;
        }

        // Installs the per-user MSI silently from a hidden helper, then reopens the app. The caller must exit right away:
        // the helper waits a couple of seconds so the app's file isn't in use when the installer replaces it.
        public static void Install(UpdateInfo update, string appPath, bool showWindow)
        {
            string log = Path.Combine(updateDir, "install-" + update.Version + ".log");
            string relaunchArgs = (showWindow ? "" : "--tray ") + "--updated";
            string script =
                "ping -n 3 127.0.0.1 >nul" +
                " & start \"\" /wait msiexec.exe /i \"" + update.MsiPath + "\" /qn /l*v \"" + log + "\"" +
                " & start \"\" \"" + appPath + "\" " + relaunchArgs;
            var helper = new ProcessStartInfo("cmd.exe", "/c " + script);
            helper.UseShellExecute = false;
            helper.CreateNoWindow = true;
            helper.WindowStyle = ProcessWindowStyle.Hidden;
            Process.Start(helper);
        }

        // The GitHub release notes for a version as plain text, one change per line, or null when there are none
        // (or GitHub can't be reached). Markdown headings and bold are dropped and list items get bullets.
        public static string ReleaseNotes(string repo, string version)
        {
            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
                var release = (Dictionary<string, object>)new JavaScriptSerializer()
                    .DeserializeObject(Get("https://api.github.com/repos/" + repo + "/releases/tags/v" + version));
                var lines = new List<string>();
                foreach (string raw in ((release["body"] as string) ?? "").Split('\n'))
                {
                    string line = raw.Trim().Replace("**", "");
                    if (line.Length == 0 || line.StartsWith("#") || line == "KrabbzDev-BusyLightz2.0 " + version) continue;
                    if (line.StartsWith("- ") || line.StartsWith("* ")) line = "• " + line.Substring(2);
                    lines.Add(line);
                }
                return lines.Count > 0 ? string.Join("\n", lines.ToArray()) : null;
            }
            catch (Exception) { return null; }
        }

        public static string ReleasePage(string repo, string version)
        {
            return "https://github.com/" + repo + "/releases/tag/v" + version;
        }

        static string Get(string url)
        {
            using (var web = NewClient())
            {
                web.Headers[HttpRequestHeader.Accept] = "application/vnd.github+json";
                return web.DownloadString(url);
            }
        }

        static WebClient NewClient()
        {
            var web = new WebClient();
            web.Headers[HttpRequestHeader.UserAgent] = "KrabbzDev-BusyLightz2.0";
            return web;
        }

        static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (var file = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(file)).Replace("-", "");
        }
    }
}
