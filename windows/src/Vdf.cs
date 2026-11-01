using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Xuefiest
{
    // Tree for Steam's text KeyValues format (VDF/ACF). Key order is preserved so rewritten
    // files keep Steam's layout; key lookups are case-insensitive, like Steam's.
    public class VdfNode
    {
        public string Key = "";
        public string Value = "";
        public bool IsObject;
        public List<VdfNode> Children = new List<VdfNode>();

        public VdfNode Child(string key)
        {
            foreach (VdfNode c in Children)
            {
                if (string.Equals(c.Key, key, StringComparison.OrdinalIgnoreCase))
                    return c;
            }
            return null;
        }

        public VdfNode Find(params string[] path)
        {
            VdfNode n = this;
            foreach (string p in path)
            {
                n = n.Child(p);
                if (n == null)
                    return null;
            }
            return n;
        }

        public string ChildValue(string key, string def = "")
        {
            VdfNode c = Child(key);
            return (c != null && !c.IsObject) ? c.Value : def;
        }

        // Updates the value if the key exists, otherwise appends it.
        public void SetChildValue(string key, string value)
        {
            VdfNode c = Child(key);
            if (c != null)
            {
                c.IsObject = false;
                c.Children.Clear();
                c.Value = value;
                return;
            }
            Children.Add(new VdfNode { Key = key, Value = value });
        }
    }

    public static class Vdf
    {
        private enum Tok { Str, Open, Close, End }

        private class Lexer
        {
            private readonly string _s;
            private int _pos;

            public Lexer(string s) { _s = s; }

            public Tok Next(out string text)
            {
                text = null;
                for (;;)
                {
                    SkipSpaceAndComments();
                    if (_pos >= _s.Length)
                        return Tok.End;

                    char c = _s[_pos];
                    if (c == '{') { _pos++; return Tok.Open; }
                    if (c == '}') { _pos++; return Tok.Close; }
                    if (c == '[')
                    {
                        // Platform conditionals such as [$WIN32]; not needed here.
                        int end = _s.IndexOf(']', _pos);
                        _pos = end < 0 ? _s.Length : end + 1;
                        continue;
                    }
                    text = c == '"' ? ReadQuoted() : ReadBare();
                    return Tok.Str;
                }
            }

            private void SkipSpaceAndComments()
            {
                while (_pos < _s.Length)
                {
                    char c = _s[_pos];
                    if (char.IsWhiteSpace(c))
                    {
                        _pos++;
                    }
                    else if (c == '/' && _pos + 1 < _s.Length && _s[_pos + 1] == '/')
                    {
                        int nl = _s.IndexOf('\n', _pos);
                        _pos = nl < 0 ? _s.Length : nl + 1;
                    }
                    else
                    {
                        break;
                    }
                }
            }

            private string ReadQuoted()
            {
                var sb = new StringBuilder();
                _pos++; // opening quote
                while (_pos < _s.Length)
                {
                    char c = _s[_pos++];
                    if (c == '"')
                        return sb.ToString();
                    if (c == '\\' && _pos < _s.Length)
                    {
                        char e = _s[_pos];
                        switch (e)
                        {
                            case '\\': sb.Append('\\'); _pos++; continue;
                            case '"': sb.Append('"'); _pos++; continue;
                            case 'n': sb.Append('\n'); _pos++; continue;
                            case 't': sb.Append('\t'); _pos++; continue;
                        }
                        // unknown escape: keep the backslash
                    }
                    sb.Append(c);
                }
                return sb.ToString();
            }

            private string ReadBare()
            {
                int start = _pos;
                while (_pos < _s.Length)
                {
                    char c = _s[_pos];
                    if (char.IsWhiteSpace(c) || c == '"' || c == '{' || c == '}')
                        break;
                    _pos++;
                }
                return _s.Substring(start, _pos - start);
            }
        }

        // The returned root is synthetic; top-level entries are in root.Children.
        public static VdfNode Parse(string text)
        {
            var root = new VdfNode { IsObject = true };
            ParseObject(new Lexer(text), root, true);
            return root;
        }

        private static void ParseObject(Lexer lx, VdfNode parent, bool topLevel)
        {
            for (;;)
            {
                string text;
                Tok t = lx.Next(out text);
                if (t == Tok.End)
                {
                    if (topLevel)
                        return;
                    throw new FormatException(L.T("Vdf.UnexpectedEnd"));
                }
                if (t == Tok.Close)
                {
                    if (!topLevel)
                        return;
                    throw new FormatException(L.T("Vdf.ExtraClose"));
                }
                if (t == Tok.Open)
                    throw new FormatException(L.T("Vdf.UnexpectedOpen"));

                var node = new VdfNode { Key = text };
                string value;
                Tok v = lx.Next(out value);
                if (v == Tok.Open)
                {
                    node.IsObject = true;
                    ParseObject(lx, node, false);
                }
                else if (v == Tok.Str)
                {
                    node.Value = value;
                }
                else
                {
                    throw new FormatException(L.F("Vdf.MissingValue", node.Key));
                }
                parent.Children.Add(node);
            }
        }

        public static string Serialize(VdfNode root)
        {
            var sb = new StringBuilder();
            foreach (VdfNode c in root.Children)
                WriteNode(c, 0, sb);
            return sb.ToString();
        }

        private static string Escape(string s)
        {
            return s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\t", "\\t");
        }

        private static void WriteNode(VdfNode n, int depth, StringBuilder sb)
        {
            string indent = new string('\t', depth);
            sb.Append(indent).Append('"').Append(Escape(n.Key)).Append('"');
            if (!n.IsObject)
            {
                sb.Append("\t\t\"").Append(Escape(n.Value)).Append("\"\n");
                return;
            }
            sb.Append('\n').Append(indent).Append("{\n");
            foreach (VdfNode c in n.Children)
                WriteNode(c, depth + 1, sb);
            sb.Append(indent).Append("}\n");
        }

        public static VdfNode Load(string path)
        {
            try
            {
                return Parse(File.ReadAllText(path, Encoding.UTF8));
            }
            catch (FormatException ex)
            {
                throw new FormatException(L.F("Vdf.ParseFailed", path, ex.Message));
            }
        }

        // Writes to a temporary file first, then swaps it in, so a manifest is never half-written.
        public static void Save(string path, VdfNode root)
        {
            string tmp = path + ".tmp";
            File.WriteAllText(tmp, Serialize(root), new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(tmp, path, null);
            else
                File.Move(tmp, path);
        }
    }
}
