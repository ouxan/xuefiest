using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace Xuefiest
{
    internal class SettingsWindow : Window
    {
        private readonly Settings _settings;
        private readonly string _originalLanguage;
        private readonly TextBox _steamBox, _cmdBox;
        private readonly TextBlock _steamHint, _cmdHint;
        private readonly Button _installBtn, _saveBtn;
        private readonly ProgressBar _progress;
        private readonly ComboBox _langCombo;

        public SettingsWindow(Window owner, Settings settings)
        {
            _settings = settings;
            Ui.Prepare(this);
            SetResourceReference(TitleProperty, "Settings.Title");
            Owner = owner;
            Width = 660;
            SizeToContent = SizeToContent.Height;
            ResizeMode = ResizeMode.NoResize;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = (FrameworkElement)Ui.Load("SettingsWindow.xaml");
            Content = root;
            _steamBox = Ui.Find<TextBox>(root, "SteamBox");
            _cmdBox = Ui.Find<TextBox>(root, "CmdBox");
            _steamHint = Ui.Find<TextBlock>(root, "SteamHint");
            _cmdHint = Ui.Find<TextBlock>(root, "CmdHint");
            _installBtn = Ui.Find<Button>(root, "CmdInstallBtn");
            _saveBtn = Ui.Find<Button>(root, "SaveBtn");
            _progress = Ui.Find<ProgressBar>(root, "Progress");
            _langCombo = Ui.Find<ComboBox>(root, "LangCombo");
            foreach (ComboBoxItem item in _langCombo.Items)
            {
                if ((string)item.Tag == L.Language)
                    _langCombo.SelectedItem = item;
            }
            // Apply the language immediately so this window previews it.
            _langCombo.SelectionChanged += (s, e) =>
            {
                var item = _langCombo.SelectedItem as ComboBoxItem;
                if (item != null && (string)item.Tag != L.Language)
                {
                    L.Apply((string)item.Tag);
                    UpdateHints();
                }
            };
            _originalLanguage = L.Language;
            Closed += (s, e) =>
            {
                // Closed without saving: revert the language.
                if (DialogResult != true && L.Language != _originalLanguage)
                    L.Apply(_originalLanguage);
            };

            _steamBox.Text = settings.SteamRoot;
            _cmdBox.Text = settings.SteamCmd;
            _steamBox.TextChanged += (s, e) => UpdateHints();
            _cmdBox.TextChanged += (s, e) => UpdateHints();
            UpdateHints();

            Ui.Find<Button>(root, "SteamBrowseBtn").Click += (s, e) =>
            {
                string d = FolderPicker.Pick(this, L.T("Settings.PickSteam"), _steamBox.Text);
                if (d != null)
                    _steamBox.Text = d;
            };
            Ui.Find<Button>(root, "SteamDetectBtn").Click += (s, e) =>
            {
                string d = SteamLocator.DetectSteamRoot();
                if (d != null)
                    _steamBox.Text = d;
                else
                    _steamHint.Text = L.T("Settings.SteamNotDetected");
            };
            Ui.Find<Button>(root, "CmdBrowseBtn").Click += (s, e) =>
            {
                var dlg = new Microsoft.Win32.OpenFileDialog
                {
                    Title = L.T("Settings.PickCmd"),
                    Filter = "steamcmd.exe|steamcmd.exe|" + L.T("Settings.ExeFilter") + "|*.exe",
                };
                if (dlg.ShowDialog(this) == true)
                    _cmdBox.Text = dlg.FileName;
            };
            _installBtn.Click += async (s, e) =>
            {
                _installBtn.IsEnabled = false;
                _saveBtn.IsEnabled = false;
                _progress.Visibility = Visibility.Visible;
                _progress.Value = 0;
                _cmdHint.Text = L.T("Settings.Downloading");
                try
                {
                    string exe = await SteamCmdInstaller.InstallAsync(p => _progress.Value = p);
                    _cmdBox.Text = exe;
                }
                catch (Exception ex)
                {
                    _cmdHint.Text = L.F("Settings.InstallFailed", ex.Message);
                }
                finally
                {
                    _installBtn.IsEnabled = true;
                    _saveBtn.IsEnabled = true;
                    _progress.Visibility = Visibility.Collapsed;
                }
            };
            _saveBtn.Click += (s, e) =>
            {
                _settings.SteamRoot = _steamBox.Text.Trim();
                _settings.SteamCmd = _cmdBox.Text.Trim();
                _settings.Set("language", L.Language);
                _settings.Save();
                DialogResult = true;
            };
        }

        private void UpdateHints()
        {
            string root = _steamBox.Text.Trim();
            if (root.Length == 0 || !Directory.Exists(Path.Combine(root, "steamapps")))
            {
                _steamHint.Text = L.T("Settings.NoSteamapps");
                _steamHint.Foreground = Ui.Brush("Warning");
            }
            else
            {
                int libs = SteamLocator.LibraryFolders(root).Count;
                _steamHint.Text = L.F("Settings.SteamFound", libs);
                _steamHint.Foreground = Ui.Brush("Success");
            }

            string cmd = _cmdBox.Text.Trim();
            if (cmd.Length > 0 && File.Exists(cmd))
            {
                _cmdHint.Text = L.T("Settings.CmdReady");
                _cmdHint.Foreground = Ui.Brush("Success");
            }
            else
            {
                _cmdHint.Text = L.T("Settings.CmdMissing");
                _cmdHint.Foreground = Ui.Brush("Muted");
            }
        }
    }
}
