# Replaces the Steam Workshop cover of Vanity Studio with workshop_preview.gif.
#
# tModLoader always uploads icon.png as the cover when you publish, and it can't upload a GIF,
# so publish the mod from tModLoader as usual first, then run this (double-click "Publish Workshop Preview.bat").
# Only the cover image is changed: files, description, tags and visibility stay as they are.
#
#   -SteamUser <name>       Steam account that owns the Workshop item
#   -PublishedFileId <id>   Workshop item id (the number in the item's URL); found automatically if possible
#   -SteamCmd <path>        steamcmd.exe to use; downloaded next to this script if missing
#   -Gif <path>             a different GIF/PNG/JPG to upload
#   -ResetLogin             forget the saved Steam login first (after a password change, or to switch accounts)

param(
	[string]$SteamUser,
	[string]$PublishedFileId,
	[string]$SteamCmd,
	[string]$Gif,
	[switch]$ResetLogin
)

$ErrorActionPreference = "Stop"
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$appId = "1281930"   # tModLoader
$modName = "VanityStudio"
$settingsFile = Join-Path $here "publish_settings.txt"

function Read-Settings {
	$s = @{}
	if (Test-Path $settingsFile) {
		foreach ($line in Get-Content $settingsFile) {
			$i = $line.IndexOf("=")
			if ($i -gt 0) { $s[$line.Substring(0, $i).Trim()] = $line.Substring($i + 1).Trim() }
		}
	}
	return $s
}

function Save-Settings($s) {
	$s.GetEnumerator() | ForEach-Object { "$($_.Key)=$($_.Value)" } | Set-Content $settingsFile -Encoding ASCII
}

function Find-SteamLibraries {
	$roots = @()
	foreach ($key in "HKCU:\Software\Valve\Steam", "HKLM:\SOFTWARE\WOW6432Node\Valve\Steam", "HKLM:\SOFTWARE\Valve\Steam") {
		try {
			$p = (Get-ItemProperty $key -ErrorAction Stop)
			if ($p.SteamPath) { $roots += $p.SteamPath }
			if ($p.InstallPath) { $roots += $p.InstallPath }
		} catch { }
	}
	$libs = @()
	foreach ($root in ($roots | Select-Object -Unique)) {
		$root = $root -replace "/", "\"
		$libs += $root
		$vdf = Join-Path $root "steamapps\libraryfolders.vdf"
		if (Test-Path $vdf) {
			foreach ($m in [regex]::Matches((Get-Content $vdf -Raw), '"path"\s+"([^"]+)"')) {
				$libs += ($m.Groups[1].Value -replace "\\\\", "\")
			}
		}
	}
	return $libs | Select-Object -Unique
}

# The publisher's own copy lives in steamapps\workshop\content\1281930\<id>\<version>\VanityStudio.tmod
function Find-PublishedId {
	$found = @()
	foreach ($lib in Find-SteamLibraries) {
		$content = Join-Path $lib "steamapps\workshop\content\$appId"
		if (-not (Test-Path $content)) { continue }
		foreach ($dir in Get-ChildItem $content -Directory) {
			if (Get-ChildItem $dir.FullName -Recurse -Filter "$modName.tmod" -ErrorAction SilentlyContinue | Select-Object -First 1) {
				$found += $dir.Name
			}
		}
	}
	return $found | Select-Object -Unique
}

function Get-SteamCmd($path) {
	if ($path -and (Test-Path $path)) { return (Resolve-Path $path).Path }
	$local = Join-Path $here "steamcmd\steamcmd.exe"
	if (Test-Path $local) { return $local }
	$onPath = Get-Command steamcmd.exe -ErrorAction SilentlyContinue
	if ($onPath) { return $onPath.Source }

	Write-Host "SteamCMD not found, downloading it into $(Join-Path $here 'steamcmd') ..." -ForegroundColor Yellow
	$dir = Join-Path $here "steamcmd"
	New-Item -ItemType Directory -Force -Path $dir | Out-Null
	$zip = Join-Path $dir "steamcmd.zip"
	[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
	Invoke-WebRequest "https://steamcdn-a.akamaihd.net/client/installer/steamcmd.zip" -OutFile $zip -UseBasicParsing
	Expand-Archive $zip -DestinationPath $dir -Force
	Remove-Item $zip
	return $local
}

# SteamCMD remembers the login as a token in config\config.vdf ("ConnectCache"). Changing the Steam password
# revokes that token, and SteamCMD then just says "Access Denied" instead of asking for the new password.
# Dropping the cached token makes it ask again; nothing else in the file is touched.
function Clear-CachedLogin($exe) {
	$config = Join-Path (Split-Path -Parent $exe) "config\config.vdf"
	if (-not (Test-Path $config)) { return $false }
	$text = Get-Content $config -Raw
	$clean = [regex]::Replace($text, '\s*"ConnectCache"\s*\{[^{}]*\}', "")
	if ($clean -eq $text) { return $false }
	Set-Content $config $clean -Encoding UTF8 -NoNewline
	return $true
}

function Get-ConsoleLog($exe) {
	$log = Join-Path (Split-Path -Parent $exe) "logs\console_log.txt"
	if (Test-Path $log) { return Get-Content $log -Raw }
	return ""
}

# --- gather everything -----------------------------------------------------------------

$settings = Read-Settings

if (-not $Gif) { $Gif = Join-Path $here "workshop_preview.gif" }
if (-not (Test-Path $Gif)) { throw "Preview image not found: $Gif" }
$Gif = (Resolve-Path $Gif).Path
$size = (Get-Item $Gif).Length
if ($size -ge 1MB) {
	throw ("{0} is {1:N0} KB. Steam rejects Workshop previews of 1 MB or more." -f (Split-Path $Gif -Leaf), ($size / 1KB))
}

if (-not $PublishedFileId) { $PublishedFileId = $settings["publishedfileid"] }
if (-not $PublishedFileId) {
	$ids = @(Find-PublishedId)
	if ($ids.Count -eq 1) {
		$PublishedFileId = $ids[0]
		Write-Host "Found the published Workshop item: $PublishedFileId"
	} elseif ($ids.Count -gt 1) {
		Write-Host "Several Workshop folders contain $modName.tmod: $($ids -join ', ')"
	}
}
while (-not ($PublishedFileId -match '^\d+$')) {
	$PublishedFileId = Read-Host "Workshop item id (the number after ?id= in the item's Steam page URL)"
}

if (-not $SteamUser) { $SteamUser = $settings["steamuser"] }
while (-not $SteamUser) { $SteamUser = Read-Host "Steam login name of the account that published the mod" }

$settings["publishedfileid"] = $PublishedFileId
$settings["steamuser"] = $SteamUser
Save-Settings $settings

$exe = Get-SteamCmd $SteamCmd

# --- describe the update: preview only, no contentfolder, so nothing else is touched ---

$vdf = Join-Path $here "preview.vdf"
$gifEscaped = $Gif -replace "\\", "\\"
@"
"workshopitem"
{
	"appid"		"$appId"
	"publishedfileid"		"$PublishedFileId"
	"previewfile"		"$gifEscaped"
	"changenote"		""
}
"@ | Set-Content $vdf -Encoding ASCII

Write-Host ""
Write-Host "Uploading $(Split-Path $Gif -Leaf) as the cover of item $PublishedFileId as '$SteamUser'." -ForegroundColor Cyan
Write-Host "SteamCMD will ask for the password and, if you use Steam Guard, the code." -ForegroundColor Cyan
Write-Host ""

if ($ResetLogin -and (Clear-CachedLogin $exe)) {
	Write-Host "Forgot the saved Steam login, SteamCMD will ask for the password." -ForegroundColor Yellow
}

$before = (Get-ConsoleLog $exe).Length
& $exe +login $SteamUser +workshop_build_item "$vdf" +quit
$code = $LASTEXITCODE

if ($code -ne 0) {
	$log = Get-ConsoleLog $exe
	$latest = if ($log.Length -ge $before) { $log.Substring($before) } else { $log }
	# a saved login that Steam no longer accepts: forget it and try once more with the password
	if ($latest -match "cached credentials" -and $latest -match "Access Denied|Expired|Invalid Password|Revoked") {
		if (Clear-CachedLogin $exe) {
			Write-Host ""
			Write-Host "Steam refused the saved login (the password was probably changed). Logging in again..." -ForegroundColor Yellow
			Write-Host ""
			& $exe +login $SteamUser +workshop_build_item "$vdf" +quit
			$code = $LASTEXITCODE
		}
	}
}

Write-Host ""
if ($code -eq 0) {
	Write-Host "Done. It can take a minute for Steam to show the new cover:" -ForegroundColor Green
	Write-Host "  https://steamcommunity.com/sharedfiles/filedetails/?id=$PublishedFileId"
	Write-Host "Publishing from tModLoader again puts icon.png back, so run this again after each publish."
} else {
	Write-Host "SteamCMD exited with code $code. Scroll up for its message (wrong password, Steam Guard, not the item's owner...).`n  If the login keeps failing, run it with -ResetLogin." -ForegroundColor Red
}
exit $code
