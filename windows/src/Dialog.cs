using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace Xuefiest
{
    internal enum DialogKind { Info, Question, Warning, Error, Success }

    // Borderless, themed message dialog used instead of the system MessageBox.
    internal class Dialog : Window
    {
        private bool _result;

        private Dialog(Window owner, DialogKind kind, string title, string message, string primary, string secondary, bool danger)
        {
            Ui.Prepare(this);
            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            Background = Brushes.Transparent;
            ResizeMode = ResizeMode.NoResize;
            SizeToContent = SizeToContent.WidthAndHeight;
            ShowInTaskbar = false;
            Title = title;
            if (owner != null && owner.IsLoaded)
            {
                Owner = owner;
                WindowStartupLocation = WindowStartupLocation.CenterOwner;
            }
            else
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen;
            }

            var root = (FrameworkElement)Ui.Load("Dialog.xaml");
            Content = root;

            string glyph, color;
            switch (kind)
            {
                case DialogKind.Warning: glyph = ""; color = "Warning"; break;
                case DialogKind.Error: glyph = ""; color = "Danger"; break;
                case DialogKind.Success: glyph = ""; color = "Success"; break;
                case DialogKind.Question: glyph = ""; color = "Accent"; break;
                default: glyph = ""; color = "Accent"; break;
            }
            var icon = Ui.Find<TextBlock>(root, "IconText");
            icon.Text = glyph;
            icon.Foreground = Ui.Brush(color);
            Ui.Find<Border>(root, "IconBg").Background = Ui.Soft(color, 0x26);
            Ui.Find<TextBlock>(root, "TitleText").Text = title;
            Ui.Find<TextBlock>(root, "MessageText").Text = message;

            var primaryBtn = Ui.Find<Button>(root, "PrimaryBtn");
            primaryBtn.Content = primary;
            if (danger)
                primaryBtn.Style = (Style)Application.Current.FindResource("DangerButton");
            primaryBtn.Click += (s, e) =>
            {
                _result = true;
                Close();
            };

            var secondaryBtn = Ui.Find<Button>(root, "SecondaryBtn");
            if (secondary == null)
            {
                secondaryBtn.Visibility = Visibility.Collapsed;
                primaryBtn.IsCancel = true;
            }
            else
            {
                secondaryBtn.Content = secondary;
                secondaryBtn.Click += (s, e) => Close();
            }

            // Allow dragging the borderless window.
            root.MouseLeftButtonDown += (s, e) =>
            {
                if (e.ButtonState == MouseButtonState.Pressed)
                    DragMove();
            };
            Loaded += (s, e) => primaryBtn.Focus();
        }

        public static bool Ask(Window owner, DialogKind kind, string title, string message, string primary,
                               string secondary = null, bool danger = false)
        {
            var d = new Dialog(owner, kind, title, message, primary, secondary ?? L.T("Common.Cancel"), danger);
            d.ShowDialog();
            return d._result;
        }

        public static void Info(Window owner, DialogKind kind, string title, string message)
        {
            new Dialog(owner, kind, title, message, L.T("Common.OK"), null, false).ShowDialog();
        }
    }
}
