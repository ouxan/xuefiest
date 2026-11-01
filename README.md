# Xuefiest

Keep an installed Steam game on its current version.

Steam decides whether a game needs an update by comparing the `buildid` and depot manifest IDs in
`steamapps\appmanifest_<appid>.acf` with the latest ones on its servers. Xuefiest fetches
the latest values with **SteamCMD using an anonymous login** and writes them into the manifest.
Steam then treats the game as up to date, while the files on disk stay as they are.

Windows 10/11 · C# / WPF · .NET Framework 4.8 · English and Turkish UI

## Download

Get `Xuefiest.exe` from [Releases](https://github.com/ouxan/xuefiest/releases). It runs on
Windows 10/11 without installation. The executable is not code-signed, so Windows SmartScreen may
show a warning on first launch.

## Build

No SDK is required. The script uses the C# compiler that ships with .NET Framework 4.8:

```
windows\build.cmd
```

Output: `windows\bin\Xuefiest.exe`, a single executable that needs no installation.

## Usage

1. **Exit Steam** from the system tray. Steam may overwrite manifest changes while it is running.
2. Select a game in the library. The detail panel shows the installed and latest build.
3. Select **Block updates**. If SteamCMD is missing, the app offers to download it from Valve.
4. Start Steam. The game is shown as up to date.

When a newer build is released, the card shows **Newer build released**; select **Block updates**
again. **Restore** puts the original manifest back so Steam updates the game normally.
**Check for updates** queries every game in the library at once.

### Add from folder

For game files that are not in Steam yet (Steam would start an update when it detects them):

1. Exit Steam and select **Add from folder**.
2. Choose the game folder. The AppID is read from `steam_appid.txt`, or the game can be found
   with **Search store**.
3. Select **Fetch info**. Depots for Windows, 64-bit and the selected language are preselected;
   select DLC depots only if you own the DLC.
4. Select **Create manifest**. If the folder is outside a Steam library, the app offers to move it
   into a library on the same drive.

## How it works

- Finds the Steam installation and all library folders automatically.
- Reads app info with `steamcmd +login anonymous +app_info_print <appid>` and takes the build ID
  from `depots/branches/<branch>/buildid` and each depot's manifest ID from
  `depots/<depot>/manifests/<branch>/gid`.
- Reads Steam's `logs\content_log.txt` to align the installed depot list with what Steam expects
  (for example, a language depot that doesn't match the game language, or DLC that isn't owned).
  Otherwise Steam would still report an update even with a matching build ID.
- Writes a `.acf.bak` backup once, before the first change. Manifests are written atomically.
- Leaves the manifest unchanged if the latest manifest ID of any installed depot is unavailable.
- Supports beta branches (`UserConfig/BetaKey`) and DLC depots.
- Optionally deletes partial update files in `steamapps\downloading` and `steamapps\temp`.

Settings are stored in `%LOCALAPPDATA%\Xuefiest\settings.ini`. SteamCMD, when installed by
the app, goes to `%LOCALAPPDATA%\Xuefiest\steamcmd`.

## Limitations

- Some games do not provide depot information to anonymous users.
- **Verify integrity of game files** in Steam replaces the files with the latest version.
- Online games may refuse connections from an older version.
- This tool does not roll back a game that has already been updated.

## Disclaimer

Xuefiest only edits the manifest files of games that are already installed. It does not
download, unlock or decrypt any content. Use it with games you own. This project is not affiliated
with Valve or Steam.
