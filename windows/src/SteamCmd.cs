using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace Xuefiest
{
    public class SteamCmdResult
    {
        public bool Ok;
        public string Error;
        public Dictionary<string, VdfNode> Apps = new Dictionary<string, VdfNode>();
    }

    // Runs SteamCMD with an anonymous login and collects app info (app_info_print output).
    public class SteamCmdRunner
    {
        // SteamCMD updates itself on first run, which can take a while.
        private const int TimeoutMs = 10 * 60 * 1000;
        private static readonly Regex Ansi = new Regex(@"\x1B\[[0-9;?]*[A-Za-z]");

        private volatile Process _proc;
        private volatile bool _cancelled;

        public Task<SteamCmdResult> FetchAppInfoAsync(string exe, IList<string> appIds, Action<string> log)
        {
            _cancelled = false;
            return Task.Run(() => FetchAppInfo(exe, appIds, log));
        }

        public void Cancel()
        {
            _cancelled = true;
            Process p = _proc;
            if (p != null)
                TryKill(p);
        }

        private SteamCmdResult FetchAppInfo(string exe, IList<string> appIds, Action<string> log)
        {
            var args = new StringBuilder(
                "+@ShutdownOnFailedCommand 0 +@NoPromptForPassword 1 +login anonymous +app_info_update 1");
            // The first app_info_print can return stale cached data, so print twice and use
            // the last block.
            for (int pass = 0; pass < 2; pass++)
            {
                foreach (string id in appIds)
                    args.Append(" +app_info_print ").Append(id);
            }
            args.Append(" +quit");

            // After its first-run self-update SteamCMD restarts and that run may print nothing,
            // so retry once when no data comes back.
            for (int attempt = 1; attempt <= 2; attempt++)
            {
                log("> steamcmd " + args);
                string output;
                string error = RunOnce(exe, args.ToString(), log, out output);
                if (error != null)
                    return new SteamCmdResult { Error = error };

                Dictionary<string, VdfNode> apps = ParseAppInfoOutput(output, appIds);
                if (apps.Count > 0)
                    return new SteamCmdResult { Ok = true, Apps = apps };

                if (attempt == 1)
                    log(L.T("SteamCmd.Retry"));
            }
            return new SteamCmdResult
            {
                Error = L.T("SteamCmd.NoInfo")
            };
        }

        private string RunOnce(string exe, string args, Action<string> log, out string output)
        {
            output = "";
            if (string.IsNullOrEmpty(exe) || !File.Exists(exe))
                return L.F("SteamCmd.NotFound", exe);

            var psi = new ProcessStartInfo(exe, args)
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true,
                CreateNoWindow = true,
                WorkingDirectory = Path.GetDirectoryName(exe),
                StandardOutputEncoding = Encoding.UTF8,
                StandardErrorEncoding = Encoding.UTF8,
            };

            var sb = new StringBuilder();
            var gate = new object();
            DataReceivedEventHandler onData = (s, e) =>
            {
                if (e.Data == null)
                    return;
                string line = Ansi.Replace(e.Data, "");
                lock (gate)
                    sb.AppendLine(line);
                string t = line.Trim();
                // Skip the (long) VDF body of app_info_print in the log.
                if (t.Length > 0 && t[0] != '"' && t != "{" && t != "}")
                    log(t);
            };

            using (var p = new Process { StartInfo = psi })
            {
                p.OutputDataReceived += onData;
                p.ErrorDataReceived += onData;
                try
                {
                    p.Start();
                }
                catch (Exception ex)
                {
                    return L.F("SteamCmd.StartFailed", ex.Message);
                }
                _proc = p;
                try
                {
                    p.StandardInput.Close(); // never block on a password prompt
                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    if (!p.WaitForExit(TimeoutMs))
                    {
                        TryKill(p);
                        return L.T("SteamCmd.Timeout");
                    }
                    p.WaitForExit(); // let the async readers drain
                }
                finally
                {
                    _proc = null;
                }
                if (_cancelled)
                    return L.T("Common.Cancelled");
            }

            lock (gate)
                output = sb.ToString();
            return null;
        }

        private static void TryKill(Process p)
        {
            try
            {
                if (!p.HasExited)
                    p.Kill();
            }
            catch (Exception)
            {
            }
        }

        // Extracts the requested apps' blocks from app_info_print output.
        public static Dictionary<string, VdfNode> ParseAppInfoOutput(string output, IList<string> appIds)
        {
            var result = new Dictionary<string, VdfNode>();
            foreach (string id in appIds)
            {
                var re = new Regex("^\\s*\"" + Regex.Escape(id) + "\"\\s*$", RegexOptions.Multiline);
                // The last match is the most recent data.
                int start = -1;
                foreach (Match m in re.Matches(output))
                    start = m.Index;
                if (start < 0)
                    continue;

                int brace = output.IndexOf('{', start);
                if (brace < 0)
                    continue;
                int end = MatchingBraceEnd(output, brace);
                if (end < 0)
                    continue;

                try
                {
                    VdfNode root = Vdf.Parse(output.Substring(start, end - start));
                    if (root.Children.Count > 0)
                        result[id] = root.Children[0];
                }
                catch (FormatException)
                {
                }
            }
            return result;
        }

        // Returns the index after the '}' matching the '{' at pos.
        private static int MatchingBraceEnd(string s, int pos)
        {
            int depth = 0;
            bool inQuote = false;
            for (int i = pos; i < s.Length; i++)
            {
                char c = s[i];
                if (inQuote)
                {
                    if (c == '\\')
                        i++;
                    else if (c == '"')
                        inQuote = false;
                    continue;
                }
                if (c == '"')
                {
                    inQuote = true;
                }
                else if (c == '{')
                {
                    depth++;
                }
                else if (c == '}')
                {
                    if (--depth == 0)
                        return i + 1;
                }
            }
            return -1;
        }
    }
}
