using System.Collections;
using System.Collections.Generic;
using System.Windows;

namespace Xuefiest
{
    // UI localization. Strings live in ui\Strings.<lang>.xaml. XAML binds to them with
    // {DynamicResource Key}, so open windows update immediately when the language changes;
    // code uses L.T("Key") and L.F("Key", args).
    internal static class L
    {
        // Plain copy of the strings so background threads can read them safely.
        private static volatile Dictionary<string, string> _map = new Dictionary<string, string>();
        private static ResourceDictionary _merged;

        public static string Language { get; private set; }

        public static string Normalize(string lang)
        {
            return lang == "tr" ? "tr" : "en";
        }

        public static void Apply(string lang)
        {
            lang = Normalize(lang);
            var dict = (ResourceDictionary)Ui.Load("Strings." + lang + ".xaml");

            var map = new Dictionary<string, string>();
            foreach (DictionaryEntry e in dict)
            {
                var v = e.Value as string;
                if (v != null)
                    map[(string)e.Key] = v.Replace("\\n", "\n");
            }
            _map = map;
            Language = lang;

            Application app = Application.Current;
            if (app != null)
            {
                if (_merged != null)
                    app.Resources.MergedDictionaries.Remove(_merged);
                app.Resources.MergedDictionaries.Add(dict);
                _merged = dict;
            }
        }

        public static string T(string key)
        {
            string v;
            return _map.TryGetValue(key, out v) ? v : key;
        }

        public static string F(string key, params object[] args)
        {
            return string.Format(T(key), args);
        }
    }
}
