using System;
using System.IO;
using System.Net;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Xuefiest
{
    // Game artwork lookup: Steam's local cache (appcache\librarycache) first, then our own
    // cache, then the Steam CDN.
    internal static class GameArt
    {
        public static readonly string[] Portrait = { "library_600x900.jpg", "library_capsule.jpg" };
        public static readonly string[] Header = { "header.jpg", "library_header.jpg" };
        public static readonly string[] Hero = { "library_hero.jpg" };
        public static readonly string[] Logo = { "logo.png" };

        private static readonly string[] CdnHosts =
        {
            "https://shared.cloudflare.steamstatic.com/store_item_assets/steam/apps/{0}/{1}",
            "https://cdn.cloudflare.steamstatic.com/steam/apps/{0}/{1}",
        };

        private static string CacheDir
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "Xuefiest", "art");
            }
        }

        public static string CdnUrl(string appId, string name)
        {
            return string.Format(CdnHosts[0], appId, name);
        }

        // Steam uses two layouts: the old flat one (librarycache\730_header.jpg) and the newer
        // per-app folders (librarycache\730\header.jpg or librarycache\730\<hash>\header.jpg).
        public static string FindLocal(string steamRoot, string appId, string[] names)
        {
            if (string.IsNullOrEmpty(steamRoot))
                return null;
            try
            {
                string lc = Path.Combine(steamRoot, "appcache", "librarycache");
                string dir = Path.Combine(lc, appId);
                string[] subdirs = Directory.Exists(dir) ? Directory.GetDirectories(dir) : new string[0];
                foreach (string name in names)
                {
                    string direct = Path.Combine(dir, name);
                    if (File.Exists(direct))
                        return direct;
                    foreach (string sub in subdirs)
                    {
                        string p = Path.Combine(sub, name);
                        if (File.Exists(p))
                            return p;
                    }
                    string flat = Path.Combine(lc, appId + "_" + name);
                    if (File.Exists(flat))
                        return flat;
                }
            }
            catch (Exception)
            {
            }
            return null;
        }

        public static string FindCached(string appId, string[] names)
        {
            foreach (string name in names)
            {
                string p = Path.Combine(CacheDir, appId + "_" + name);
                if (File.Exists(p))
                    return p;
            }
            return null;
        }

        // Downloads from the CDN (call from a background thread). Missing images are remembered
        // so they are not requested again.
        public static string Download(string appId, string name)
        {
            string dest = Path.Combine(CacheDir, appId + "_" + name);
            string miss = dest + ".miss";
            if (File.Exists(dest))
                return dest;
            if (File.Exists(miss))
                return null;

            bool notFound = true;
            foreach (string host in CdnHosts)
            {
                try
                {
                    Directory.CreateDirectory(CacheDir);
                    var req = (HttpWebRequest)WebRequest.Create(string.Format(host, appId, name));
                    req.Timeout = 10000;
                    req.ReadWriteTimeout = 10000;
                    req.UserAgent = "Xuefiest";
                    string part = dest + ".part";
                    using (var resp = (HttpWebResponse)req.GetResponse())
                    using (Stream rs = resp.GetResponseStream())
                    using (FileStream fs = File.Create(part))
                    {
                        rs.CopyTo(fs);
                    }
                    if (File.Exists(dest))
                        File.Delete(dest);
                    File.Move(part, dest);
                    return dest;
                }
                catch (WebException ex)
                {
                    var r = ex.Response as HttpWebResponse;
                    if (r == null || r.StatusCode != HttpStatusCode.NotFound)
                        notFound = false; // network error: retry later
                }
                catch (Exception)
                {
                    notFound = false;
                }
            }
            if (notFound)
            {
                try { File.WriteAllText(miss, ""); }
                catch (Exception) { }
            }
            return null;
        }

        public static string Resolve(string steamRoot, string appId, string[] names, bool allowDownload)
        {
            string p = FindLocal(steamRoot, appId, names) ?? FindCached(appId, names);
            if (p == null && allowDownload)
                p = Download(appId, names[0]);
            return p;
        }

        // Loads without locking the file and decodes at the requested width. The result is
        // frozen, so it can be created on a background thread and used by the UI.
        public static ImageSource LoadImage(string path, int decodeWidth)
        {
            if (path == null)
                return null;
            try
            {
                var bmp = new BitmapImage();
                using (FileStream fs = File.OpenRead(path))
                {
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.StreamSource = fs;
                    if (decodeWidth > 0)
                        bmp.DecodePixelWidth = decodeWidth;
                    bmp.EndInit();
                }
                bmp.Freeze();
                return bmp;
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
