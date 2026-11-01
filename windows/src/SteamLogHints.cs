using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace Xuefiest
{
    // Reads Steam's logs\content_log.txt to find out why a game is marked "update required".
    // Even with a matching build ID, Steam updates when the installed depot list differs from what
    // it expects (e.g. a language depot that doesn't match the game language, or DLC not owned).
    public class SteamLogHints
    {
        public readonly HashSet<string> RemovedDepots = new HashSet<string>();
        public readonly HashSet<string> AddedDepots = new HashSet<string>();
        public readonly HashSet<string> MissingKeyDepots = new HashSet<string>();

        private static readonly Regex DepotList = new Regex(@"(added|removed) depots ([\d,\s]+)", RegexOptions.IgnoreCase);
        // Only recent "missing key" entries count, so DLC bought later isn't removed by mistake.
        private static readonly TimeSpan MissingKeyWindow = TimeSpan.FromDays(30);
        private static readonly Regex Stamp = new Regex(@"^\[(\d{4}-\d{2}-\d{2} \d{2}:\d{2}:\d{2})\]");

        private static readonly Regex MissingKey = new Regex(@"depot (\d+), manifest \d+ \(Missing decryption key\)", RegexOptions.IgnoreCase);

        public static SteamLogHints Read(string steamRoot, string appId)
        {
            var hints = new SteamLogHints();
            if (string.IsNullOrEmpty(steamRoot))
                return hints;
            string path = Path.Combine(steamRoot, "logs", "content_log.txt");
            if (!File.Exists(path))
                return hints;

            string marker = "AppID " + appId + " ";
            try
            {
                // Steam may hold the file open for writing.
                using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(fs, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.IndexOf(marker, StringComparison.Ordinal) < 0)
                            continue;

                        int cfg = line.IndexOf("config changed :", StringComparison.Ordinal);
                        if (cfg >= 0)
                        {
                            // The latest "config changed" line wins.
                            hints.AddedDepots.Clear();
                            hints.RemovedDepots.Clear();
                            foreach (Match m in DepotList.Matches(line.Substring(cfg)))
                            {
                                HashSet<string> target = m.Groups[1].Value.Equals("added", StringComparison.OrdinalIgnoreCase)
                                                             ? hints.AddedDepots
                                                             : hints.RemovedDepots;
                                foreach (string id in m.Groups[2].Value.Split(new[] { ',', ' ' }, StringSplitOptions.RemoveEmptyEntries))
                                    target.Add(id.Trim());
                            }
                            continue;
                        }

                        Match k = MissingKey.Match(line);
                        if (k.Success && IsRecent(line))
                            hints.MissingKeyDepots.Add(k.Groups[1].Value);
                    }
                }
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
            return hints;
        }

        private static bool IsRecent(string line)
        {
            Match m = Stamp.Match(line);
            DateTime t;
            if (!m.Success || !DateTime.TryParseExact(m.Groups[1].Value, "yyyy-MM-dd HH:mm:ss",
                                                     System.Globalization.CultureInfo.InvariantCulture,
                                                     System.Globalization.DateTimeStyles.AssumeLocal, out t))
                return false;
            return DateTime.Now - t < MissingKeyWindow;
        }
    }
}
