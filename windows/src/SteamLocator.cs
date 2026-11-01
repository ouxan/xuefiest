using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;

namespace Xuefiest
{
    public class InstalledApp
    {
        public string AppId = "";
        public string Name = "";
        public string BuildId = "";
        public string Branch = "public";
        public string StateFlags = "";
        public string InstallDir = "";
        public long SizeOnDisk;
        public string AcfPath = "";
        public string SteamappsDir = "";
        public List<string> DlcAppIds = new List<string>(); // dlcappid values from InstalledDepots
        public bool HasBackup;

        // Non-game tools such as Proton or the Steam Linux Runtime.
        public bool IsTool
        {
            get
            {
                if (AppId == "228980")
                    return true;
                foreach (string p in new[] { "Proton", "Steam Linux Runtime", "Steamworks Common", "SteamVR" })
                {
                    if (Name.StartsWith(p, StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                return false;
            }
        }

        public string InstallPath
        {
            get { return Path.Combine(SteamappsDir, "common", InstallDir); }
        }
    }

    public static class SteamLocator
    {
        public static string ManagedSteamCmdDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "Xuefiest", "steamcmd");
            }
        }

        public static string BackupPath(string acfPath)
        {
            return acfPath + ".bak";
        }

        private static string SteamappsIn(string libraryRoot)
        {
            if (string.IsNullOrEmpty(libraryRoot))
                return null;
            try
            {
                string p = Path.Combine(libraryRoot, "steamapps");
                return Directory.Exists(p) ? ProperCase(Path.GetFullPath(p)) : null;
            }
            catch (Exception)
            {
                return null; // invalid path (e.g. Linux paths in libraryfolders.vdf)
            }
        }

        private static string RegString(RegistryKey hive, string path, string name)
        {
            try
            {
                using (RegistryKey k = hive.OpenSubKey(path))
                {
                    return k == null ? null : k.GetValue(name) as string;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static string DetectSteamRoot()
        {
            var candidates = new[]
            {
                RegString(Registry.CurrentUser, @"Software\Valve\Steam", "SteamPath"),
                RegString(Registry.LocalMachine, @"SOFTWARE\WOW6432Node\Valve\Steam", "InstallPath"),
                RegString(Registry.LocalMachine, @"SOFTWARE\Valve\Steam", "InstallPath"),
                @"C:\Program Files (x86)\Steam",
            };
            foreach (string c in candidates)
            {
                if (SteamappsIn(c) != null)
                    return ProperCase(Path.GetFullPath(c));
            }
            return null;
        }

        // "c:\program files (x86)\steam" -> "C:\Program Files (x86)\Steam"
        public static string ProperCase(string path)
        {
            try
            {
                var dir = new DirectoryInfo(path);
                if (dir.Parent == null)
                    return dir.FullName.ToUpperInvariant();
                string parent = ProperCase(dir.Parent.FullName);
                FileSystemInfo[] match = new DirectoryInfo(parent).GetFileSystemInfos(dir.Name);
                return match.Length == 1 ? Path.Combine(parent, match[0].Name) : Path.Combine(parent, dir.Name);
            }
            catch (Exception)
            {
                return path;
            }
        }

        // steamapps folders of all libraries.
        public static List<string> LibraryFolders(string steamRoot)
        {
            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Action<string> add = root =>
            {
                string sa = SteamappsIn(root);
                if (sa != null && seen.Add(sa))
                    result.Add(sa);
            };

            add(steamRoot);

            string mainSa = SteamappsIn(steamRoot);
            var vdfCandidates = new List<string>();
            if (mainSa != null)
                vdfCandidates.Add(Path.Combine(mainSa, "libraryfolders.vdf"));
            vdfCandidates.Add(Path.Combine(steamRoot, "config", "libraryfolders.vdf"));

            foreach (string vdfPath in vdfCandidates)
            {
                if (!File.Exists(vdfPath))
                    continue;
                VdfNode root;
                try { root = Vdf.Load(vdfPath); }
                catch (Exception) { continue; }

                VdfNode lf = root.Child("libraryfolders");
                if (lf == null || !lf.IsObject)
                    continue;
                foreach (VdfNode e in lf.Children)
                {
                    int dummy;
                    if (!int.TryParse(e.Key, out dummy))
                        continue;
                    // New format: "0" { "path" "..." }; old format: "1" "D:\\SteamLibrary"
                    string path = e.IsObject ? e.ChildValue("path") : e.Value;
                    add(path);
                }
                break;
            }
            return result;
        }

        public static InstalledApp ReadInstalledApp(string acfPath)
        {
            VdfNode root = Vdf.Load(acfPath);
            VdfNode st = root.Child("AppState");
            if (st == null || !st.IsObject)
                throw new FormatException(L.F("Patch.NoAppStateIn", acfPath));

            var app = new InstalledApp
            {
                AppId = st.ChildValue("appid"),
                Name = st.ChildValue("name"),
                BuildId = st.ChildValue("buildid"),
                StateFlags = st.ChildValue("StateFlags"),
                InstallDir = st.ChildValue("installdir"),
                AcfPath = acfPath,
                SteamappsDir = Path.GetDirectoryName(acfPath),
                HasBackup = File.Exists(BackupPath(acfPath)),
            };

            long size;
            if (long.TryParse(st.ChildValue("SizeOnDisk"), out size))
                app.SizeOnDisk = size;

            VdfNode beta = st.Find("UserConfig", "BetaKey");
            if (beta == null || string.IsNullOrEmpty(beta.Value))
                beta = st.Find("MountedConfig", "BetaKey");
            if (beta != null && !string.IsNullOrEmpty(beta.Value))
                app.Branch = beta.Value;

            VdfNode depots = st.Child("InstalledDepots");
            if (depots != null)
            {
                foreach (VdfNode d in depots.Children)
                {
                    string dlc = d.ChildValue("dlcappid");
                    if (dlc.Length > 0 && !app.DlcAppIds.Contains(dlc))
                        app.DlcAppIds.Add(dlc);
                }
            }
            if (app.Name.Length == 0)
                app.Name = L.F("Game.Unnamed", app.AppId);
            return app;
        }

        public static List<InstalledApp> InstalledApps(string steamRoot)
        {
            var apps = new List<InstalledApp>();
            foreach (string sa in LibraryFolders(steamRoot))
            {
                string[] files;
                try { files = Directory.GetFiles(sa, "appmanifest_*.acf"); }
                catch (Exception) { continue; }

                foreach (string f in files)
                {
                    try
                    {
                        InstalledApp a = ReadInstalledApp(f);
                        if (a.AppId.Length > 0)
                            apps.Add(a);
                    }
                    catch (Exception)
                    {
                        // unreadable manifest: skip
                    }
                }
            }
            apps.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.CurrentCultureIgnoreCase));
            return apps;
        }

        public static string DetectSteamCmd()
        {
            var candidates = new List<string>();
            candidates.Add(Path.Combine(ManagedSteamCmdDir, "steamcmd.exe"));
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steamcmd", "steamcmd.exe"));
            candidates.Add(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steamcmd.exe"));
            candidates.Add(@"C:\steamcmd\steamcmd.exe");

            string pathVar = Environment.GetEnvironmentVariable("PATH") ?? "";
            foreach (string dir in pathVar.Split(';'))
            {
                if (dir.Trim().Length > 0)
                {
                    try { candidates.Add(Path.Combine(dir.Trim(), "steamcmd.exe")); }
                    catch (ArgumentException) { }
                }
            }

            foreach (string c in candidates)
            {
                if (File.Exists(c))
                    return Path.GetFullPath(c);
            }
            return null;
        }

        public static bool IsSteamRunning()
        {
            Process[] ps = Process.GetProcessesByName("steam");
            foreach (Process p in ps)
                p.Dispose();
            return ps.Length > 0;
        }
    }
}
