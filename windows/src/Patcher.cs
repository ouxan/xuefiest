using System;
using System.Collections.Generic;
using System.IO;

namespace Xuefiest
{
    public class FreezeOptions
    {
        // AutoUpdateBehavior = 1: "Only update this game when I launch it".
        public bool UpdateOnlyOnLaunch;
        // Delete partial update files in steamapps\downloading\<appid> and steamapps\temp\<appid>.
        public bool ClearDownloadCache;
        // Steam root, used to read logs\content_log.txt and align the installed depot list with
        // what Steam expects. Skipped when empty.
        public string SteamRoot;
    }

    public class FreezeResult
    {
        public bool Ok;
        public bool AlreadyLatest;
        public string LatestBuildId = "";
        public List<string> Changes = new List<string>();
        public List<string> Warnings = new List<string>();
        public string Error;
        // Number of depots removed based on Steam's log.
        public int RemovedDepots;
    }

    public static class Patcher
    {
        public static string LatestBuildId(VdfNode appInfo, string branch)
        {
            // PICS keeps branches under "depots" (depots/branches/<branch>/buildid); fall back to
            // a top-level "branches" section for older output.
            VdfNode b = appInfo.Find("depots", "branches", branch, "buildid")
                        ?? appInfo.Find("branches", branch, "buildid");
            return b != null ? b.Value : "";
        }

        private static string ManifestFor(VdfNode appInfo, string depotId, string branch)
        {
            VdfNode m = appInfo.Find("depots", depotId, "manifests", branch);
            if (m == null)
                return "";
            // New format: "public" { "gid" "..." }; old format: "public" "..."
            return m.IsObject ? m.ChildValue("gid") : m.Value;
        }

        private static string ManifestWithDlc(Dictionary<string, VdfNode> infos, VdfNode baseInfo,
                                              string depotId, string dlcAppId, string branch)
        {
            string gid = ManifestFor(baseInfo, depotId, branch);
            VdfNode dlcInfo;
            if (gid.Length == 0 && dlcAppId.Length > 0 && infos.TryGetValue(dlcAppId, out dlcInfo))
                gid = ManifestFor(dlcInfo, depotId, branch);
            return gid;
        }

        // Rewrites the manifest so Steam sees the latest build as installed.
        public static FreezeResult Freeze(InstalledApp app, Dictionary<string, VdfNode> infos, FreezeOptions opt)
        {
            var res = new FreezeResult();
            try
            {
                FreezeCore(app, infos, opt, res);
            }
            catch (Exception ex)
            {
                res.Ok = false;
                res.Error = ex.Message;
            }
            return res;
        }

        private static void FreezeCore(InstalledApp app, Dictionary<string, VdfNode> infos,
                                       FreezeOptions opt, FreezeResult res)
        {
            VdfNode root = Vdf.Load(app.AcfPath);
            VdfNode st = root.Child("AppState");
            if (st == null || !st.IsObject)
            {
                res.Error = L.T("Patch.NoAppState");
                return;
            }

            VdfNode baseInfo;
            if (!infos.TryGetValue(app.AppId, out baseInfo))
            {
                res.Error = L.F("Patch.NoInfo", app.AppId);
                return;
            }

            string branch = app.Branch;
            res.LatestBuildId = LatestBuildId(baseInfo, branch);
            if (res.LatestBuildId.Length == 0)
            {
                res.Error = L.F("Patch.NoBuild", branch);
                return;
            }

            // Depots Steam wants removed (language/DLC mismatch) or cannot decrypt (DLC not owned).
            // While they are listed, Steam reports "update required" even with a matching build ID.
            VdfNode installed = st.Child("InstalledDepots");
            SteamLogHints hints = SteamLogHints.Read(opt.SteamRoot, app.AppId);
            var drop = new HashSet<string>();
            if (installed != null)
            {
                foreach (VdfNode d in installed.Children)
                {
                    if (hints.RemovedDepots.Contains(d.Key) || hints.MissingKeyDepots.Contains(d.Key))
                        drop.Add(d.Key);
                }
            }

            // Find the latest manifest ID for each installed depot.
            var newManifests = new Dictionary<string, string>();
            var missing = new List<string>();
            if (installed != null)
            {
                foreach (VdfNode d in installed.Children)
                {
                    if (drop.Contains(d.Key))
                        continue;
                    string dlc = d.ChildValue("dlcappid");
                    string gid = ManifestWithDlc(infos, baseInfo, d.Key, dlc, branch);
                    if (gid.Length == 0 && branch != "public")
                    {
                        gid = ManifestWithDlc(infos, baseInfo, d.Key, dlc, "public");
                        if (gid.Length > 0)
                            res.Warnings.Add(L.F("Patch.PublicFallback", d.Key, branch));
                    }
                    if (gid.Length == 0)
                        missing.Add(d.Key);
                    else
                        newManifests[d.Key] = gid;
                }
            }

            if (missing.Count > 0)
            {
                // Patching with missing depots is pointless: Steam would still update.
                res.Error = L.F("Patch.MissingDepots", string.Join(", ", missing));
                return;
            }

            // Apply changes.
            string oldBuild = st.ChildValue("buildid");
            if (oldBuild != res.LatestBuildId)
                res.Changes.Add(string.Format("buildid: {0} → {1}", oldBuild, res.LatestBuildId));

            if (installed != null && drop.Count > 0)
            {
                installed.Children.RemoveAll(d => drop.Contains(d.Key));
                res.RemovedDepots = drop.Count;
                foreach (string id in drop)
                {
                    res.Changes.Add(L.F("Patch.DepotRemoved", id,
                                        L.T(hints.MissingKeyDepots.Contains(id) ? "Patch.ReasonNoKey" : "Patch.ReasonConfig")));
                }
            }

            // Depots Steam expects to be added (e.g. a new depot from an update).
            if (installed != null)
            {
                foreach (string id in hints.AddedDepots)
                {
                    if (installed.Child(id) != null)
                        continue;
                    string gid = ManifestFor(baseInfo, id, branch);
                    if (gid.Length == 0 && branch != "public")
                        gid = ManifestFor(baseInfo, id, "public");
                    if (gid.Length == 0)
                    {
                        res.Warnings.Add(L.F("Patch.AddedNoManifest", id));
                        continue;
                    }
                    var n = new VdfNode { Key = id, IsObject = true };
                    n.SetChildValue("manifest", gid);
                    VdfNode size = baseInfo.Find("depots", id, "manifests", branch, "size")
                                   ?? baseInfo.Find("depots", id, "manifests", "public", "size");
                    n.SetChildValue("size", size != null ? size.Value : "0");
                    string dlc = baseInfo.Find("depots", id, "dlcappid") != null
                                     ? baseInfo.Find("depots", id, "dlcappid").Value
                                     : "";
                    if (dlc.Length > 0)
                        n.SetChildValue("dlcappid", dlc);
                    installed.Children.Add(n);
                    newManifests[id] = gid;
                    res.Changes.Add(L.F("Patch.DepotAdded", id));
                    res.Warnings.Add(L.F("Patch.AddedWarning", id));
                }
            }

            if (installed != null)
            {
                foreach (VdfNode d in installed.Children)
                {
                    string oldGid = d.ChildValue("manifest");
                    string newGid = newManifests[d.Key];
                    if (oldGid != newGid)
                    {
                        res.Changes.Add(string.Format("depot {0}: {1} → {2}", d.Key, oldGid, newGid));
                        d.SetChildValue("manifest", newGid);
                    }
                }
            }

            bool dirtyState = st.ChildValue("StateFlags") != "4"
                              || st.ChildValue("UpdateResult", "0") != "0"
                              || st.ChildValue("BytesToDownload", "0") != "0"
                              || st.ChildValue("TargetBuildID") != res.LatestBuildId;

            if (res.Changes.Count == 0 && !dirtyState && !opt.UpdateOnlyOnLaunch && !opt.ClearDownloadCache)
            {
                res.Ok = true;
                res.AlreadyLatest = true;
                return;
            }

            st.SetChildValue("buildid", res.LatestBuildId);
            st.SetChildValue("TargetBuildID", res.LatestBuildId);
            st.SetChildValue("StateFlags", "4");
            st.SetChildValue("UpdateResult", "0");
            foreach (string k in new[] { "BytesToDownload", "BytesDownloaded", "BytesToStage", "BytesStaged",
                                         "StagingSize", "ScheduledAutoUpdate" })
            {
                if (st.Child(k) != null)
                    st.SetChildValue(k, "0");
            }
            if (opt.UpdateOnlyOnLaunch)
            {
                st.SetChildValue("AutoUpdateBehavior", "1");
                res.Changes.Add(L.T("Patch.OnLaunchSet"));
            }
            if (dirtyState)
                res.Changes.Add(L.T("Patch.StateCleared"));

            // Back up only once, so the .bak always holds the original manifest.
            string bak = SteamLocator.BackupPath(app.AcfPath);
            if (!File.Exists(bak))
            {
                File.Copy(app.AcfPath, bak);
                res.Changes.Add(L.F("Patch.Backup", bak));
            }

            Vdf.Save(app.AcfPath, root);

            if (opt.ClearDownloadCache)
                ClearDownloadCache(app, res);

            res.Ok = true;
        }

        private static void ClearDownloadCache(InstalledApp app, FreezeResult res)
        {
            foreach (string sub in new[] { "downloading", "temp" })
            {
                string d = Path.Combine(app.SteamappsDir, sub, app.AppId);
                if (!Directory.Exists(d))
                    continue;
                try
                {
                    Directory.Delete(d, true);
                    res.Changes.Add(L.F("Patch.Deleted", d));
                }
                catch (Exception ex)
                {
                    res.Warnings.Add(L.F("Patch.DeleteFailed", d, ex.Message));
                }
            }
            // downloading\state_<appid>_<depot>.patch: state files of a partial update.
            string dl = Path.Combine(app.SteamappsDir, "downloading");
            if (!Directory.Exists(dl))
                return;
            foreach (string f in Directory.GetFiles(dl, "state_" + app.AppId + "_*.patch"))
            {
                try
                {
                    File.Delete(f);
                    res.Changes.Add(L.F("Patch.Deleted", f));
                }
                catch (Exception ex)
                {
                    res.Warnings.Add(L.F("Patch.DeleteFailed", f, ex.Message));
                }
            }
        }

        // Restores the .bak backup; Steam then updates the game normally.
        public static void Restore(InstalledApp app)
        {
            string bak = SteamLocator.BackupPath(app.AcfPath);
            if (!File.Exists(bak))
                throw new FileNotFoundException(L.F("Patch.NoBackup", bak));
            File.Copy(bak, app.AcfPath, true);
            File.Delete(bak);
        }
    }
}
