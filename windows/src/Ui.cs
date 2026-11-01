using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Markup;
using System.Windows.Media;

namespace Xuefiest
{
    // XAML files are embedded in the exe (see build.cmd: /resource:ui\X.xaml,ui.X.xaml).
    internal static class Ui
    {
        public static object Load(string name)
        {
            using (Stream s = Assembly.GetExecutingAssembly().GetManifestResourceStream("ui." + name))
            {
                if (s == null)
                    throw new InvalidOperationException("Embedded UI file not found: " + name);
                return XamlReader.Load(s);
            }
        }

        public static T Find<T>(FrameworkElement root, string name) where T : class
        {
            var o = root.FindName(name) as T;
            if (o == null)
                throw new InvalidOperationException("UI element not found: " + name);
            return o;
        }

        public static Brush Brush(string key)
        {
            return (Brush)Application.Current.FindResource(key);
        }

        public static Color ColorOf(string key)
        {
            var b = Brush(key) as SolidColorBrush;
            return b != null ? b.Color : Colors.Gray;
        }

        // Translucent tint of a theme color (badge and notice backgrounds).
        public static Brush Soft(string key, byte alpha)
        {
            Color c = ColorOf(key);
            var b = new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
            b.Freeze();
            return b;
        }

        public static void Prepare(Window w)
        {
            w.Background = Brush("Bg");
            w.Foreground = Brush("Text");
            w.FontFamily = new FontFamily("Segoe UI Variable Text, Segoe UI");
            w.FontSize = 13;
            w.UseLayoutRounding = true;
            TextOptions.SetTextFormattingMode(w, TextFormattingMode.Display);
            w.SourceInitialized += (s, e) => DarkTitleBar(w);
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

        // Dark title bar on Windows 10/11; on Windows 11 the caption color also matches the background.
        private static void DarkTitleBar(Window w)
        {
            try
            {
                IntPtr h = new WindowInteropHelper(w).Handle;
                int on = 1;
                DwmSetWindowAttribute(h, 20, ref on, 4); // DWMWA_USE_IMMERSIVE_DARK_MODE
                Color c = ColorOf("Bg");
                int bgr = c.R | (c.G << 8) | (c.B << 16);
                DwmSetWindowAttribute(h, 35, ref bgr, 4); // DWMWA_CAPTION_COLOR (Windows 11)
            }
            catch (Exception)
            {
                // older Windows: keep the default title bar
            }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes <= 0)
                return "—";
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double v = bytes;
            int u = 0;
            while (v >= 1024 && u < units.Length - 1)
            {
                v /= 1024;
                u++;
            }
            return v.ToString(u == 0 ? "0" : "0.0") + " " + units[u];
        }
    }
}
