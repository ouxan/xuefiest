using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace Xuefiest
{
    public class DepotInfo
    {
        public string Id = "";
        public string Name = "";
        public string OsList = "";
        public string OsArch = "";
        public string Language = "";
        public string DlcAppId = "";
        public string DepotFromApp = ""; // set for shared depots (e.g. DirectX/VC++ redistributables)
        public string Gid = "";
        public long Size;
        public bool LowViolence;
        public bool DefaultSelected;
        public string Note = "";

        public bool IsShared { get { return DepotFromApp.Length > 0; } }
    }

    // Builds an app manifest from scratch for an existing game folder.
    public static class ManifestBuilder
    {
        // Decides which depots count as installed, mirroring Steam's own selection rules.
        public static List<DepotInfo> ListDepots(VdfNode appInfo, string branch, string language)
        {
            var result = new List<DepotInfo>();
            VdfNode depots = appInfo.Child("depots");
            if (depots == null)
                return result;

            foreach (VdfNode d in depots.Children)
            {
                long dummy;
                if (!d.IsObject || !long.TryParse(d.Key, out dummy))
                    continue; // "branches", "baselanguages", ...

                var info = new DepotInfo
                {
                    Id = d.Key,
                    Name = d.ChildValue("name"),
                    DlcAppId = d.ChildValue("dlcappid"),
                    DepotFromApp = d.ChildValue("depotfromapp"),
                };
                VdfNode cfg = d.Child("config");
                if (cfg != null)
                {
                    info.OsList = cfg.ChildValue("oslist");
                    info.OsArch = cfg.ChildValue("osarch");
                    info.Language = cfg.ChildValue("language");
                    info.LowViolence = cfg.ChildValue("lowviolence") == "1";
                    if (cfg.ChildValue("realm") == "steamchina")
                        continue;
                }

                if (!info.IsShared)
                {
                    VdfNode m = d.Find("manifests", branch);
                    if (m == null && branch != "public")
                        m = d.Find("manifests", "public");
                    if (m == null)
                        continue; // no content on this branch
                    info.Gid = m.IsObject ? m.ChildValue("gid") : m.Value;
                    long size;
                    if (m.IsObject && long.TryParse(m.ChildValue("size"), out size))
                        info.Size = size;
                    if (info.Gid.Length == 0)
                        continue;
                }

                info.DefaultSelected = true;
                if (info.OsList.Length > 0 && info.OsList.IndexOf("windows", StringComparison.OrdinalIgnoreCase) < 0)
                    Exclude(info, L.T("Depot.NotWindows"));
                else if (info.OsArch.Length > 0 && info.OsArch != "64" && Environment.Is64BitOperatingSystem)
                    Exclude(info, L.F("Depot.Arch", info.OsArch));
                else if (info.Language.Length > 0 && !string.Equals(info.Language, language, StringComparison.OrdinalIgnoreCase))
                    Exclude(info, L.F("Depot.Lang", info.Language));
                else if (info.LowViolence)
                    Exclude(info, L.T("Depot.LowViolence"));
                else if (info.DlcAppId.Length > 0)
                    Exclude(info, L.F("Depot.Dlc", info.DlcAppId));

                result.Add(info);
            }
            return result;
        }

        private static void Exclude(DepotInfo info, string reason)
        {
            info.DefaultSelected = false;
            info.Note = reason;
        }

        public static List<string> Languages(VdfNode appInfo)
        {
            var set = new SortedSet<string>(StringComparer.OrdinalIgnoreCase) { "english" };
            VdfNode depots = appInfo.Child("depots");
            if (depots != null)
            {
                foreach (VdfNode d in depots.Children)
                {
                    VdfNode l = d.Find("config", "language");
                    if (l != null && l.Value.Length > 0)
                        set.Add(l.Value);
                }
            }
            return set.ToList();
        }

        // Looks for steam_appid.txt up to three levels deep.
        public static string FindAppIdInFolder(string folder)
        {
            var queue = new Queue<KeyValuePair<string, int>>();
            queue.Enqueue(new KeyValuePair<string, int>(folder, 0));
            int visited = 0;
            while (queue.Count > 0 && visited < 500)
            {
                KeyValuePair<string, int> cur = queue.Dequeue();
                visited++;
                try
                {
                    string f = Path.Combine(cur.Key, "steam_appid.txt");
                    if (File.Exists(f))
                    {
                        string id = File.ReadAllText(f).Trim();
                        long dummy;
                        if (long.TryParse(id, out dummy))
                            return id;
                    }
                    if (cur.Value < 3)
                    {
                        foreach (string sub in Directory.GetDirectories(cur.Key))
                            queue.Enqueue(new KeyValuePair<string, int>(sub, cur.Value + 1));
                    }
                }
                catch (Exception)
                {
                    // inaccessible folder: skip
                }
            }
            return null;
        }

        // Is the folder a library's steamapps\common\<installdir>?
        public static string FindLibraryFor(string folder, IEnumerable<string> steamappsDirs, out string installDir)
        {
            installDir = null;
            string full = Path.GetFullPath(folder).TrimEnd(Path.DirectorySeparatorChar);
            string parent = Path.GetDirectoryName(full);
            if (parent == null)
                return null;
            foreach (string sa in steamappsDirs)
            {
                string common = Path.Combine(sa, "common");
                if (string.Equals(Path.GetFullPath(common).TrimEnd(Path.DirectorySeparatorChar), parent,
                                  StringComparison.OrdinalIgnoreCase))
                {
                    installDir = Path.GetFileName(full);
                    return sa;
                }
            }
            return null;
        }

        public static long FolderSize(string folder)
        {
            long total = 0;
            try
            {
                foreach (string f in Directory.EnumerateFiles(folder, "*", SearchOption.AllDirectories))
                {
                    try { total += new FileInfo(f).Length; }
                    catch (Exception) { }
                }
            }
            catch (Exception)
            {
            }
            return total;
        }

        // SteamID64 of the most recent account in config\loginusers.vdf.
        public static string MostRecentSteamId(string steamRoot)
        {
            try
            {
                string path = Path.Combine(steamRoot, "config", "loginusers.vdf");
                if (!File.Exists(path))
                    return "0";
                VdfNode users = Vdf.Load(path).Child("users");
                if (users == null)
                    return "0";
                foreach (VdfNode u in users.Children)
                {
                    if (u.ChildValue("MostRecent") == "1" || u.ChildValue("mostrecent") == "1")
                        return u.Key;
                }
                return users.Children.Count > 0 ? users.Children[0].Key : "0";
            }
            catch (Exception)
            {
                return "0";
            }
        }

        public static string AcfPathFor(string steamappsDir, string appId)
        {
            return Path.Combine(steamappsDir, "appmanifest_" + appId + ".acf");
        }

        public static VdfNode BuildAcf(string appId, string name, string installDir, string buildId,
                                       IEnumerable<DepotInfo> depots, string language, string branch,
                                       long sizeOnDisk, string owner)
        {
            var st = new VdfNode { Key = "AppState", IsObject = true };
            string now = ((long)(DateTime.UtcNow - new DateTime(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc)).TotalSeconds)
                             .ToString(CultureInfo.InvariantCulture);

            st.SetChildValue("appid", appId);
            st.SetChildValue("Universe", "1");
            st.SetChildValue("name", name);
            st.SetChildValue("StateFlags", "4");
            st.SetChildValue("installdir", installDir);
            st.SetChildValue("LastUpdated", now);
            st.SetChildValue("LastPlayed", "0");
            st.SetChildValue("SizeOnDisk", sizeOnDisk.ToString(CultureInfo.InvariantCulture));
            st.SetChildValue("StagingSize", "0");
            st.SetChildValue("buildid", buildId);
            st.SetChildValue("LastOwner", owner);
            st.SetChildValue("UpdateResult", "0");
            st.SetChildValue("BytesToDownload", "0");
            st.SetChildValue("BytesDownloaded", "0");
            st.SetChildValue("BytesToStage", "0");
            st.SetChildValue("BytesStaged", "0");
            st.SetChildValue("TargetBuildID", buildId);
            st.SetChildValue("AutoUpdateBehavior", "0");
            st.SetChildValue("AllowOtherDownloadsWhileRunning", "0");
            st.SetChildValue("ScheduledAutoUpdate", "0");

            var installed = new VdfNode { Key = "InstalledDepots", IsObject = true };
            var shared = new VdfNode { Key = "SharedDepots", IsObject = true };
            foreach (DepotInfo d in depots)
            {
                if (d.IsShared)
                {
                    shared.SetChildValue(d.Id, d.DepotFromApp);
                    continue;
                }
                var n = new VdfNode { Key = d.Id, IsObject = true };
                n.SetChildValue("manifest", d.Gid);
                n.SetChildValue("size", d.Size.ToString(CultureInfo.InvariantCulture));
                if (d.DlcAppId.Length > 0)
                    n.SetChildValue("dlcappid", d.DlcAppId);
                installed.Children.Add(n);
            }
            st.Children.Add(installed);
            if (shared.Children.Count > 0)
                st.Children.Add(shared);

            foreach (string cfgName in new[] { "UserConfig", "MountedConfig" })
            {
                var cfg = new VdfNode { Key = cfgName, IsObject = true };
                cfg.SetChildValue("language", language);
                if (branch != "public")
                    cfg.SetChildValue("BetaKey", branch);
                st.Children.Add(cfg);
            }

            var root = new VdfNode { IsObject = true };
            root.Children.Add(st);
            return root;
        }
    }
}
