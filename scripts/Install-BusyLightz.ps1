<#
.SYNOPSIS
  Installs (or updates) BusyLightz for everyone on this PC, in C:\Program Files\BusyLightz, from the newest release.
  For RMM tools: run as SYSTEM (or an admin). Safe to run again: it does nothing when the newest version is installed.

.DESCRIPTION
  Finds the newest version on GitHub, downloads its all-users installer (BusyLightz-Setup.msi, published in the
  release tagged v<version>-allusers), checks it against the SHA-256 GitHub recorded, and installs it silently.
  Copies installed just for one user move over on their own the next time they run, keeping their settings.
  Exit codes: 0 installed or already up to date, 3010 installed but Windows wants a restart, anything else failed.

.EXAMPLE
  powershell -NoProfile -ExecutionPolicy Bypass -File Install-BusyLightz.ps1
.EXAMPLE
  # Also set your organization's Teams app for everyone on the PC (see docs/Setting-Up-Teams-for-Your-Organization.md):
  powershell -NoProfile -ExecutionPolicy Bypass -File Install-BusyLightz.ps1 -TeamsClientId <client ID> -TeamsTenantId <tenant ID>
#>
param(
    [string]$TeamsClientId,
    [string]$TeamsTenantId,
    [string]$Repo = 'krabbzdev/KrabbzDev-BusyLightz2.0'
)

$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.ServicePointManager]::SecurityProtocol -bor [Net.SecurityProtocolType]::Tls12
$headers = @{ 'User-Agent' = 'BusyLightz-RMM-Install'; 'Accept' = 'application/vnd.github+json' }

$me = [Security.Principal.WindowsIdentity]::GetCurrent()
if (-not ([Security.Principal.WindowsPrincipal]$me).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Output 'Needs admin rights: run as SYSTEM or an administrator.'
    exit 1
}

try {
    # The newest version, from the latest release (the per-user one), then its all-users installer.
    $latest = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/latest" -Headers $headers
    $version = [version]($latest.tag_name.TrimStart('v', 'V'))
    $release = Invoke-RestMethod "https://api.github.com/repos/$Repo/releases/tags/v$version-allusers" -Headers $headers
    $asset = $release.assets | Where-Object { $_.name -eq 'BusyLightz-Setup.msi' } | Select-Object -First 1
    if (-not $asset) { throw "Release v$version-allusers has no BusyLightz-Setup.msi." }
    if ($asset.digest -notlike 'sha256:*') { throw "BusyLightz-Setup.msi has no SHA-256 digest on GitHub; not installing it." }
    $expected = $asset.digest.Substring(7)

    # Already on this version (or newer)?
    $exe = Join-Path $env:ProgramFiles 'BusyLightz\BusyLightz.exe'
    if (Test-Path $exe) {
        $installed = [version](Get-Item $exe).VersionInfo.FileVersion
        if ($installed -ge $version) {
            Write-Output "BusyLightz $installed is already installed (newest is $version)."
            exit 0
        }
    }

    # Download where only admins can write, so nothing can swap the file before it's installed.
    $dir = Join-Path $env:ProgramFiles 'BusyLightz Updates'
    New-Item -ItemType Directory -Force $dir | Out-Null
    $msi = Join-Path $dir "BusyLightz-Setup-$version.msi"
    Invoke-WebRequest $asset.browser_download_url -OutFile $msi -UseBasicParsing -Headers @{ 'User-Agent' = 'BusyLightz-RMM-Install' }
    $actual = (Get-FileHash $msi -Algorithm SHA256).Hash
    if ($actual -ne $expected) {
        Remove-Item $msi -Force
        throw "Download failed its SHA-256 check (expected $expected, got $actual); deleted it."
    }

    $log = Join-Path $dir "install-$version.log"
    $arguments = "/i `"$msi`" /qn /norestart /l*v `"$log`""
    if ($TeamsClientId -and $TeamsTenantId) { $arguments += " TEAMSCLIENTID=$TeamsClientId TEAMSTENANTID=$TeamsTenantId" }
    Write-Output "Installing BusyLightz $version..."
    $process = Start-Process msiexec.exe -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -eq 0 -or $process.ExitCode -eq 3010) {
        Write-Output "Installed BusyLightz $version in $env:ProgramFiles\BusyLightz."
    } else {
        Write-Output "The installer failed with code $($process.ExitCode). Log: $log"
    }
    exit $process.ExitCode
}
catch {
    Write-Output "Couldn't install BusyLightz: $($_.Exception.Message)"
    exit 1
}
