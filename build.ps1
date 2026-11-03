<#
.SYNOPSIS
  Builds KrabbzDev-BusyLightz2.0 (tray app) and its per-user MSI (no admin rights needed).
  Output: dist\KrabbzDev-BusyLightz2.0-<version>.msi

.EXAMPLE
  .\build.ps1 -Version 2.0.3            # build only
  .\build.ps1 -Version 2.0.3 -Publish   # build, then publish as GitHub release v2.0.3 (installed apps auto-update to it)
  .\build.ps1 -Version 2.0.3 -Publish -Notes "- Fixed X`n- Added Y"   # with release notes, shown in the app after updating
#>
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,
    # GitHub owner/repo the app checks for newer releases; empty disables auto-update.
    [string]$UpdateRepo = 'krabbzdev/KrabbzDev-BusyLightz2.0',
    # Entra app registration (Application and Directory IDs) for "Connect to Microsoft Teams", created by
    # scripts\New-EntraAppRegistration.ps1. Not secrets. Pass empty strings to build without the feature.
    [string]$GraphClientId = '0213fb00-29af-4f12-a2f9-8b90447266d3',
    [string]$GraphTenantId = 'e32df814-cde8-4e0e-b51a-f6043d246ca5',
    # Publish the MSI as a GitHub release (needs GitHub CLI, signed in).
    [switch]$Publish,
    # What changed, as Markdown list items (e.g. "- Open sets Teams to Available"). Becomes the release notes,
    # which the app shows in its "Updated to version x" card after updating.
    [string]$Notes
)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc = "$env:WINDIR\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
$wix = Join-Path $root 'tools\wix'
$src = Join-Path $root 'src'
$assets = Join-Path $root 'assets'
$build = Join-Path $root 'build'
$dist = Join-Path $root 'dist'
New-Item -ItemType Directory -Force $assets, $build, $dist | Out-Null

if (-not (Test-Path "$wix\candle.exe")) {
    throw "WiX 3.14 not found in tools\wix. Download wix314-binaries.zip from https://github.com/wixtoolset/wix3/releases/tag/wix3141rtm and extract it there."
}

function Invoke-Tool([string]$exe, [string[]]$arguments) {
    & $exe @arguments
    if ($LASTEXITCODE -ne 0) { throw "$(Split-Path $exe -Leaf) failed with exit code $LASTEXITCODE" }
}

# Writes a multi-size .ico (PNG entries) of a light split red/blue.
function New-AppIcon([string]$path) {
    Add-Type -AssemblyName System.Drawing
    $sizes = 16, 32, 48, 256
    $pngs = foreach ($size in $sizes) {
        $bmp = New-Object System.Drawing.Bitmap $size, $size
        $g = [System.Drawing.Graphics]::FromImage($bmp)
        $g.SmoothingMode = 'AntiAlias'
        $pad = [Math]::Max(1, [int]($size / 16))
        $d = $size - 2 * $pad
        $g.FillPie([System.Drawing.Brushes]::Red, $pad, $pad, $d, $d, 135, 180)
        $g.FillPie([System.Drawing.Brushes]::RoyalBlue, $pad, $pad, $d, $d, 315, 180)
        $pen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(50, 50, 50)), ([Math]::Max(1, $size / 24))
        $g.DrawEllipse($pen, $pad, $pad, $d, $d)
        $g.Dispose()
        $ms = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        , $ms.ToArray()
    }
    $out = New-Object System.IO.MemoryStream
    $w = New-Object System.IO.BinaryWriter $out
    $w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
    $offset = 6 + 16 * $sizes.Count
    for ($i = 0; $i -lt $sizes.Count; $i++) {
        $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
        $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
        $w.Write([uint16]1); $w.Write([uint16]32)
        $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
        $offset += $pngs[$i].Length
    }
    foreach ($png in $pngs) { $w.Write($png) }
    [System.IO.File]::WriteAllBytes($path, $out.ToArray())
}

$icon = Join-Path $assets 'app.ico'
if (-not (Test-Path $icon)) { New-AppIcon $icon }

# Stamp the version and build date into the exe; the app shows them in its window and tray menu.
@"
using System.Reflection;
[assembly: AssemblyTitle("KrabbzDev-BusyLightz2.0")]
[assembly: AssemblyProduct("KrabbzDev-BusyLightz2.0")]
[assembly: AssemblyVersion("$Version.0")]
[assembly: AssemblyFileVersion("$Version.0")]
[assembly: AssemblyInformationalVersion("$Version")]
[assembly: AssemblyMetadata("BuildDate", "$(Get-Date -Format 'yyyy-MM-dd')")]
[assembly: AssemblyMetadata("UpdateRepo", "$UpdateRepo")]
[assembly: AssemblyMetadata("GraphClientId", "$GraphClientId")]
[assembly: AssemblyMetadata("GraphTenantId", "$GraphTenantId")]
"@ | Set-Content "$build\AssemblyInfo.cs" -Encoding utf8

Write-Host 'Compiling tray app...'
# The exe keeps its original file name: installed copies relaunch themselves by exe path after updating.
Invoke-Tool $csc @('/nologo', '/optimize+', '/target:winexe', "/win32icon:$icon",
    "/out:$build\Krabbz9999 Blynclight App Control.exe",
    '/reference:System.Core.dll', '/reference:System.Drawing.dll', '/reference:System.Windows.Forms.dll', '/reference:System.Web.Extensions.dll', '/reference:System.Web.dll', '/reference:System.Security.dll',
    "$src\BlyncCore.cs", "$src\LightController.cs", "$src\TrayApp.cs", "$src\ControlForm.cs", "$src\Updater.cs", "$src\CallDetector.cs", "$src\EmbravaConnect.cs", "$src\TeamsPresence.cs", "$src\SetupForm.cs", "$src\TeamsAppConfig.cs", "$src\AppSettings.cs", "$src\SettingsForm.cs", "$src\CrashReport.cs", "$src\MicTestForm.cs", "$src\AudioSessions.cs", "$src\TrayMenu.cs", "$src\ModernControls.cs", "$src\AllUsers.cs", "$build\AssemblyInfo.cs")

Write-Host 'Building MSI...'
Invoke-Tool "$wix\candle.exe" @('-nologo', '-arch', 'x64', '-ext', 'WixUtilExtension',
    "-dVersion=$Version", "-dBuildDir=$build", "-dAssetsDir=$assets",
    '-out', "$build\Product.wixobj", "$root\installer\Product.wxs")
$msi = Join-Path $dist "KrabbzDev-BusyLightz2.0-$Version.msi"
Invoke-Tool "$wix\light.exe" @('-nologo', '-ext', 'WixUtilExtension', '-spdb', '-out', $msi, "$build\Product.wixobj")
Write-Host "Built $msi"

# The all-users installer (C:\Program Files\BusyLightz) and its "BusyLightz Updater" scheduled task, which runs as SYSTEM
# at startup and every 4 hours to install updates. schtasks wants the task definition as UTF-16.
@"
<?xml version="1.0" encoding="UTF-16"?>
<Task version="1.2" xmlns="http://schemas.microsoft.com/windows/2004/02/mit/task">
  <RegistrationInfo><Description>Installs BusyLightz updates for everyone on this PC.</Description></RegistrationInfo>
  <Triggers>
    <BootTrigger><Delay>PT5M</Delay></BootTrigger>
    <TimeTrigger>
      <StartBoundary>2026-01-01T00:00:00</StartBoundary>
      <Repetition><Interval>PT4H</Interval></Repetition>
      <RandomDelay>PT30M</RandomDelay>
    </TimeTrigger>
  </Triggers>
  <Principals><Principal id="System"><UserId>S-1-5-18</UserId><RunLevel>HighestAvailable</RunLevel></Principal></Principals>
  <Settings>
    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>
    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>
    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>
    <StartWhenAvailable>true</StartWhenAvailable>
    <RunOnlyIfNetworkAvailable>true</RunOnlyIfNetworkAvailable>
    <ExecutionTimeLimit>PT30M</ExecutionTimeLimit>
  </Settings>
  <Actions Context="System">
    <Exec><Command>"%ProgramFiles%\BusyLightz\BusyLightz.exe"</Command><Arguments>--system-update</Arguments></Exec>
  </Actions>
</Task>
"@ | Set-Content "$build\BusyLightzUpdater.xml" -Encoding Unicode
Invoke-Tool "$wix\candle.exe" @('-nologo', '-arch', 'x64', '-ext', 'WixUtilExtension',
    "-dVersion=$Version", "-dBuildDir=$build",
    '-out', "$build\ProductAllUsers.wixobj", "$root\installer\ProductAllUsers.wxs")
$allUsersMsi = Join-Path $dist "BusyLightz-Setup-$Version.msi"
# ICE43 and ICE57 assume the Start menu shortcut goes in one user's profile; in an all-users install it goes in
# everyone's Start menu, so they don't apply.
Invoke-Tool "$wix\light.exe" @('-nologo', '-ext', 'WixUtilExtension', '-spdb', '-sice:ICE43', '-sice:ICE57',
    '-out', $allUsersMsi, "$build\ProductAllUsers.wixobj")
Write-Host "Built $allUsersMsi"

if ($Publish) {
    $gh = (Get-Command gh -ErrorAction SilentlyContinue).Source
    if (-not $gh) { $gh = Get-ChildItem "$env:LOCALAPPDATA\Microsoft\WinGet\Packages\GitHub.cli_*\bin\gh.exe" -ErrorAction SilentlyContinue | Select-Object -First 1 -ExpandProperty FullName }
    if (-not $gh) { throw 'GitHub CLI (gh) not found. Install it with: winget install GitHub.cli' }
    # Upload under fixed names so "latest/download" links always get the newest installer. The old name is
    # kept too, so download links shared before the rename keep working.
    $releaseDir = Join-Path $dist 'release'
    New-Item -ItemType Directory -Force $releaseDir | Out-Null
    $releaseMsis = @('KrabbzDev-BusyLightz2.0.msi', 'Krabbz9999-BusyLightz2.0.msi', 'Krabbz9999-Blynclight-App-Control.msi') | ForEach-Object {
        $path = Join-Path $releaseDir $_
        Copy-Item $msi $path -Force
        $path
    }
    Write-Host "Publishing release v$Version to $UpdateRepo..."
    Invoke-Tool $gh (@('release', 'create', "v$Version") + $releaseMsis + @('--repo', $UpdateRepo,
        '--title', "Version $Version", '--notes', $(if ($Notes) { $Notes } else { "KrabbzDev-BusyLightz2.0 $Version" })))

    # The all-users installer goes in its own release, never marked latest: copies installed just for one user
    # install the last .msi in the latest release, and must never get this one. The all-users updater finds it by tag.
    $allUsersAsset = Join-Path $releaseDir 'BusyLightz-Setup.msi'
    Copy-Item $allUsersMsi $allUsersAsset -Force
    Write-Host "Publishing the all-users installer as v$Version-allusers..."
    Invoke-Tool $gh @('release', 'create', "v$Version-allusers", $allUsersAsset, '--repo', $UpdateRepo, '--latest=false',
        '--title', "Version $Version for everyone on the PC (C:\Program Files\BusyLightz)",
        '--notes', "Installs BusyLightz for every user on the PC, in C:\Program Files\BusyLightz. Needs admin rights; for IT and RMM deployment: msiexec /i BusyLightz-Setup.msi /qn. Copies installed just for one user move to it on their own, keeping their settings.")
}
