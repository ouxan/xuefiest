using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Xuefiest
{
    // Simple key=value settings stored in %LOCALAPPDATA%\Xuefiest\settings.ini.
    internal class Settings
    {
        private readonly Dictionary<string, string> _values = new Dictionary<string, string>();

        private static string FilePath
        {
            get
            {
                return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                                    "Xuefiest", "settings.ini");
            }
        }

        public static Settings Load()
        {
            var s = new Settings();
            try
            {
                if (File.Exists(FilePath))
                {
                    foreach (string line in File.ReadAllLines(FilePath, Encoding.UTF8))
                    {
                        int eq = line.IndexOf('=');
                        if (eq > 0)
                            s._values[line.Substring(0, eq)] = line.Substring(eq + 1);
                    }
                }
            }
            catch (Exception)
            {
            }
            return s;
        }

        public string Get(string key)
        {
            string v;
            return _values.TryGetValue(key, out v) ? v : null;
        }

        public bool GetBool(string key)
        {
            return Get(key) == "1";
        }

        public void Set(string key, string value)
        {
            _values[key] = value ?? "";
        }

        public void SetBool(string key, bool value)
        {
            Set(key, value ? "1" : "0");
        }

        public string SteamRoot
        {
            get { return Get("steamRoot") ?? ""; }
            set { Set("steamRoot", value); }
        }

        public string SteamCmd
        {
            get { return Get("steamCmd") ?? ""; }
            set { Set("steamCmd", value); }
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                var lines = new List<string>();
                foreach (KeyValuePair<string, string> kv in _values)
                    lines.Add(kv.Key + "=" + kv.Value);
                File.WriteAllLines(FilePath, lines, new UTF8Encoding(false));
            }
            catch (Exception)
            {
            }
        }
    }
}
