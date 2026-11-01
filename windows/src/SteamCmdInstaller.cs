using System;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Threading.Tasks;
using System.Windows;

namespace Xuefiest
{
    internal static class SteamCmdInstaller
    {
        public const string Url = "https://client-update.steamstatic.com/installer/steamcmd.zip";

        // Downloads SteamCMD from Valve into %LOCALAPPDATA%\Xuefiest\steamcmd.
        public static async Task<string> InstallAsync(Action<int> progress)
        {
            string dir = SteamLocator.ManagedSteamCmdDir;
            Directory.CreateDirectory(dir);
            string zip = Path.Combine(dir, "steamcmd.zip");

            using (var wc = new WebClient())
            {
                if (progress != null)
                    wc.DownloadProgressChanged += (s, e) => progress(e.ProgressPercentage);
                await wc.DownloadFileTaskAsync(new Uri(Url), zip);
            }

            await Task.Run(() =>
            {
                string root = Path.GetFullPath(dir) + Path.DirectorySeparatorChar;
                using (ZipArchive archive = ZipFile.OpenRead(zip))
                {
                    foreach (ZipArchiveEntry entry in archive.Entries)
                    {
                        if (entry.Name.Length == 0)
                            continue;
                        string dest = Path.GetFullPath(Path.Combine(dir, entry.FullName));
                        // Never extract outside the target folder.
                        if (!dest.StartsWith(root, StringComparison.OrdinalIgnoreCase))
                            continue;
                        Directory.CreateDirectory(Path.GetDirectoryName(dest));
                        entry.ExtractToFile(dest, true);
                    }
                }
                File.Delete(zip);
            });

            string exe = Path.Combine(dir, "steamcmd.exe");
            if (!File.Exists(exe))
                throw new FileNotFoundException(L.T("SteamCmd.NoArchiveExe"));
            return exe;
        }

        // Returns a usable SteamCMD path, offering to install it if missing. Null if unavailable.
        public static async Task<string> EnsureAsync(Window owner, Settings settings, Action<string> log)
        {
            string exe = settings.SteamCmd;
            if (exe.Length > 0 && File.Exists(exe))
                return exe;
            exe = SteamLocator.DetectSteamCmd();
            if (exe != null)
            {
                settings.SteamCmd = exe;
                settings.Save();
                return exe;
            }

            if (!Dialog.Ask(owner, DialogKind.Question, L.T("SteamCmd.RequiredTitle"),
                            L.F("SteamCmd.RequiredMsg", Url), L.T("Settings.Install")))
                return null;

            try
            {
                log(L.T("SteamCmd.Downloading"));
                exe = await InstallAsync(null);
                settings.SteamCmd = exe;
                settings.Save();
                log(L.F("SteamCmd.Installed", exe));
                return exe;
            }
            catch (Exception ex)
            {
                log(L.F("SteamCmd.InstallFailed", ex.Message));
                Dialog.Info(owner, DialogKind.Error, L.T("SteamCmd.InstallFailedTitle"), ex.Message);
                return null;
            }
        }
    }
}
