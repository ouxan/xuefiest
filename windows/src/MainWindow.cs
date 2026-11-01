using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Shapes;

namespace Xuefiest
{
    internal class MainWindow : Window
    {
        private readonly Settings _settings = Settings.Load();
        private readonly SteamCmdRunner _steamCmd = new SteamCmdRunner();
        private readonly ObservableCollection<GameItem> _items = new ObservableCollection<GameItem>();
        private readonly Dictionary<string, string> _latest = new Dictionary<string, string>(); // appid -> latest build
        private readonly ICollectionView _view;
        private bool _busy;

        private readonly TextBox _searchBox, _logBox;
        private readonly Button _addBtn, _refreshBtn, _settingsBtn, _checkAllBtn, _freezeBtn, _checkBtn, _restoreBtn, _cancelBtn, _logBtn;
        private readonly CheckBox _showToolsBox, _onlyOnLaunchBox, _clearCacheBox;
        private readonly ListBox _gameList;
        private readonly TextBlock _countText, _emptyLibraryText, _noticeIcon, _noticeText, _statusText;
        private readonly FrameworkElement _emptyLibrary, _emptyDetail, _detail, _logPanel, _busyPanel;
        private readonly Border _notice;
        private readonly Ellipse _statusDot;

        public MainWindow()
        {
            Ui.Prepare(this);
            Title = "Xuefiest";
            Width = 1260;
            Height = 800;
            MinWidth = 1000;
            MinHeight = 640;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var root = (FrameworkElement)Ui.Load("MainWindow.xaml");
            Content = root;

            _searchBox = Ui.Find<TextBox>(root, "SearchBox");
            _logBox = Ui.Find<TextBox>(root, "LogBox");
            _addBtn = Ui.Find<Button>(root, "AddBtn");
            _refreshBtn = Ui.Find<Button>(root, "RefreshBtn");
            _settingsBtn = Ui.Find<Button>(root, "SettingsBtn");
            _checkAllBtn = Ui.Find<Button>(root, "CheckAllBtn");
            _freezeBtn = Ui.Find<Button>(root, "FreezeBtn");
            _checkBtn = Ui.Find<Button>(root, "CheckBtn");
            _restoreBtn = Ui.Find<Button>(root, "RestoreBtn");
            _cancelBtn = Ui.Find<Button>(root, "CancelBtn");
            _logBtn = Ui.Find<Button>(root, "LogBtn");
            _showToolsBox = Ui.Find<CheckBox>(root, "ShowToolsBox");
            _onlyOnLaunchBox = Ui.Find<CheckBox>(root, "OnlyOnLaunchBox");
            _clearCacheBox = Ui.Find<CheckBox>(root, "ClearCacheBox");
            _gameList = Ui.Find<ListBox>(root, "GameList");
            _countText = Ui.Find<TextBlock>(root, "CountText");
            _emptyLibraryText = Ui.Find<TextBlock>(root, "EmptyLibraryText");
            _noticeIcon = Ui.Find<TextBlock>(root, "NoticeIcon");
            _noticeText = Ui.Find<TextBlock>(root, "NoticeText");
            _statusText = Ui.Find<TextBlock>(root, "StatusText");
            _emptyLibrary = Ui.Find<FrameworkElement>(root, "EmptyLibrary");
            _emptyDetail = Ui.Find<FrameworkElement>(root, "EmptyDetail");
            _detail = Ui.Find<FrameworkElement>(root, "Detail");
            _logPanel = Ui.Find<FrameworkElement>(root, "LogPanel");
            _busyPanel = Ui.Find<FrameworkElement>(root, "BusyPanel");
            _notice = Ui.Find<Border>(root, "Notice");
            _statusDot = Ui.Find<Ellipse>(root, "StatusDot");

            _view = CollectionViewSource.GetDefaultView(_items);
            _view.SortDescriptions.Add(new SortDescription("Name", ListSortDirection.Ascending));
            _view.Filter = FilterItem;
            _gameList.ItemsSource = _view;
            _gameList.ItemsPanel = new ItemsPanelTemplate(new FrameworkElementFactory(typeof(CardPanel)));

            _showToolsBox.IsChecked = _settings.GetBool("showTools");
            _onlyOnLaunchBox.IsChecked = _settings.GetBool("onlyOnLaunch");
            _clearCacheBox.IsChecked = _settings.GetBool("clearCache");

            _searchBox.TextChanged += (s, e) => ApplyFilter();
            _showToolsBox.Click += (s, e) =>
            {
                _settings.SetBool("showTools", _showToolsBox.IsChecked == true);
                ApplyFilter();
            };
            _onlyOnLaunchBox.Click += (s, e) => _settings.SetBool("onlyOnLaunch", _onlyOnLaunchBox.IsChecked == true);
            _clearCacheBox.Click += (s, e) => _settings.SetBool("clearCache", _clearCacheBox.IsChecked == true);
            _gameList.SelectionChanged += (s, e) => OnSelectionChanged();

            _addBtn.Click += (s, e) => OpenCreate();
            _refreshBtn.Click += (s, e) => RefreshApps();
            _settingsBtn.Click += (s, e) => OpenSettings();
            _checkAllBtn.Click += async (s, e) => await CheckAll();
            _checkBtn.Click += async (s, e) => await CheckSelected();
            _freezeBtn.Click += async (s, e) => await FreezeSelected();
            _restoreBtn.Click += (s, e) => RestoreSelected();
            _cancelBtn.Click += (s, e) => _steamCmd.Cancel();
            _logBtn.Click += (s, e) => _logPanel.Visibility =
                _logPanel.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;

            PreviewKeyDown += (s, e) =>
            {
                if (e.Key == Key.F5)
                {
                    RefreshApps();
                    e.Handled = true;
                }
                else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
                {
                    _searchBox.Focus();
                    _searchBox.SelectAll();
                    e.Handled = true;
                }
            };

            Loaded += (s, e) =>
            {
                EnsurePaths();
                RefreshApps();
            };
            Closing += (s, e) =>
            {
                _steamCmd.Cancel();
                _settings.Save();
            };
        }

        private GameItem SelectedGame
        {
            get { return _gameList.SelectedItem as GameItem; }
        }

        // ---------------------------------------------------------------- paths and library

        private void EnsurePaths()
        {
            if (_settings.SteamRoot.Length == 0 || !Directory.Exists(_settings.SteamRoot))
                _settings.SteamRoot = SteamLocator.DetectSteamRoot() ?? "";
            else
                _settings.SteamRoot = SteamLocator.ProperCase(_settings.SteamRoot);
            if (_settings.SteamCmd.Length == 0 || !File.Exists(_settings.SteamCmd))
                _settings.SteamCmd = SteamLocator.DetectSteamCmd() ?? "";
            _settings.Save();
        }

        private void RefreshApps()
        {
            string root = _settings.SteamRoot;
            List<InstalledApp> apps = root.Length > 0 && Directory.Exists(root)
                                          ? SteamLocator.InstalledApps(root)
                                          : new List<InstalledApp>();

            var existing = new Dictionary<string, GameItem>(StringComparer.OrdinalIgnoreCase);
            foreach (GameItem i in _items)
                existing[i.App.AcfPath] = i;

            var keep = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (InstalledApp app in apps)
            {
                GameItem item;
                if (existing.TryGetValue(app.AcfPath, out item))
                {
                    item.App = app;
                }
                else
                {
                    item = new GameItem(app);
                    _items.Add(item);
                }
                string latest;
                if (_latest.TryGetValue(app.AppId, out latest))
                    item.LatestRaw = latest;
                keep.Add(app.AcfPath);
            }
            foreach (GameItem gone in _items.Where(i => !keep.Contains(i.App.AcfPath)).ToList())
                _items.Remove(gone);

            ApplyFilter();
            LoadCovers();
            UpdateButtons();

            if (root.Length == 0)
                SetStatus(L.T("Main.NoSteam"), "Warning");
            else
                SetStatus(L.F("Main.GamesFound", apps.Count(a => !a.IsTool)), "Success");
        }

        private bool FilterItem(object o)
        {
            var item = (GameItem)o;
            if (_showToolsBox.IsChecked != true && item.App.IsTool)
                return false;
            string q = _searchBox.Text.Trim();
            return q.Length == 0
                   || item.Name.IndexOf(q, StringComparison.CurrentCultureIgnoreCase) >= 0
                   || item.AppId.Contains(q);
        }

        private void ApplyFilter()
        {
            _view.Refresh();
            int count = _view.Cast<object>().Count();
            _countText.Text = count.ToString();
            _emptyLibrary.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (_settings.SteamRoot.Length == 0)
                _emptyLibraryText.Text = L.T("Main.EmptyNoSteam");
            else if (_searchBox.Text.Trim().Length > 0)
                _emptyLibraryText.Text = L.T("Main.EmptyNoMatch");
            else
                _emptyLibraryText.Text = L.T("Main.EmptyNone");
        }

        // Loads cover art in the background: local files first (fast), then downloads.
        private void LoadCovers()
        {
            List<GameItem> todo = _items.Where(i => !i.CoverRequested).ToList();
            if (todo.Count == 0)
                return;
            foreach (GameItem i in todo)
                i.CoverRequested = true;

            string root = _settings.SteamRoot;
            Task.Run(() =>
            {
                var remote = new List<GameItem>();
                foreach (GameItem item in todo)
                {
                    string p = GameArt.FindLocal(root, item.AppId, GameArt.Portrait) ?? GameArt.FindCached(item.AppId, GameArt.Portrait);
                    if (p != null)
                        SetCover(item, p);
                    else if (!item.App.IsTool)
                        remote.Add(item);
                }
                foreach (GameItem item in remote)
                    SetCover(item, GameArt.Download(item.AppId, GameArt.Portrait[0]));
            });
        }

        private void SetCover(GameItem item, string path)
        {
            var img = GameArt.LoadImage(path, 320);
            if (img != null)
                Dispatcher.BeginInvoke(new Action(() => item.Cover = img));
        }

        private void LoadDetailArt(GameItem item)
        {
            if (item.DetailArtRequested)
                return;
            item.DetailArtRequested = true;
            string root = _settings.SteamRoot;
            bool online = !item.App.IsTool;
            Task.Run(() =>
            {
                string hero = GameArt.Resolve(root, item.AppId, GameArt.Hero, online)
                              ?? GameArt.Resolve(root, item.AppId, GameArt.Header, online);
                var heroImg = GameArt.LoadImage(hero, 900);
                var logoImg = GameArt.LoadImage(GameArt.Resolve(root, item.AppId, GameArt.Logo, online), 500);
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    item.Hero = heroImg;
                    item.Logo = logoImg;
                }));
            });
        }

        private void OnSelectionChanged()
        {
            GameItem g = SelectedGame;
            _detail.DataContext = g;
            _detail.Visibility = g != null ? Visibility.Visible : Visibility.Collapsed;
            _emptyDetail.Visibility = g != null ? Visibility.Collapsed : Visibility.Visible;
            HideNotice();
            UpdateButtons();
            if (g != null)
                LoadDetailArt(g);
        }

        // ---------------------------------------------------------------- status

        private void UpdateButtons()
        {
            GameItem g = SelectedGame;
            _freezeBtn.IsEnabled = !_busy && g != null;
            _checkBtn.IsEnabled = !_busy && g != null;
            _restoreBtn.IsEnabled = !_busy && g != null && g.App.HasBackup;
            _checkAllBtn.IsEnabled = !_busy;
            _addBtn.IsEnabled = !_busy;
            _refreshBtn.IsEnabled = !_busy;
            _settingsBtn.IsEnabled = !_busy;
        }

        private void SetBusy(bool busy, string text)
        {
            _busy = busy;
            _busyPanel.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            if (busy)
                SetStatus(text, "Accent");
            UpdateButtons();
        }

        private void SetStatus(string text, string colorKey)
        {
            _statusText.Text = text;
            _statusDot.Fill = Ui.Brush(colorKey);
        }

        private void Log(string line)
        {
            _logBox.AppendText("[" + DateTime.Now.ToString("HH:mm:ss") + "] " + line + Environment.NewLine);
            _logBox.ScrollToEnd();
            string t = line.TrimStart();
            if (t.StartsWith("✘"))
                SetStatus(t, "Danger");
            else if (t.StartsWith("✔"))
                SetStatus(t, "Success");
            else if (_busy)
                SetStatus(t, "Accent");
        }

        private void LogFromAnyThread(string line)
        {
            Dispatcher.BeginInvoke(new Action(() => Log(line)));
        }

        private void ShowNotice(string kind, string text)
        {
            string glyph = kind == "Success" ? "" : kind == "Danger" ? "" : kind == "Warning" ? "" : "";
            _notice.Background = Ui.Soft(kind, 0x1C);
            _notice.BorderBrush = Ui.Soft(kind, 0x55);
            _noticeIcon.Text = glyph;
            _noticeIcon.Foreground = Ui.Brush(kind);
            _noticeText.Text = text;
            _notice.Visibility = Visibility.Visible;
        }

        private void HideNotice()
        {
            _notice.Visibility = Visibility.Collapsed;
        }

        private bool ConfirmSteamClosed()
        {
            if (!SteamLocator.IsSteamRunning())
                return true;
            return Dialog.Ask(this, DialogKind.Warning, L.T("Steam.RunningTitle"), L.T("Steam.RunningMsg"),
                              L.T("Common.ContinueAnyway"));
        }

        // ---------------------------------------------------------------- SteamCMD

        private async Task<SteamCmdResult> Fetch(List<GameItem> games)
        {
            string exe = await SteamCmdInstaller.EnsureAsync(this, _settings, Log);
            if (exe == null)
                return null;

            var ids = new List<string>();
            foreach (GameItem g in games)
            {
                if (!ids.Contains(g.AppId))
                    ids.Add(g.AppId);
                foreach (string d in g.App.DlcAppIds)
                {
                    if (!ids.Contains(d))
                        ids.Add(d);
                }
            }

            SetBusy(true, L.T("Main.Fetching"));
            SteamCmdResult r;
            try
            {
                r = await _steamCmd.FetchAppInfoAsync(exe, ids, LogFromAnyThread);
            }
            finally
            {
                SetBusy(false, null);
            }

            if (!r.Ok)
            {
                Log("✘ " + r.Error);
                return null;
            }
            foreach (GameItem g in games)
            {
                VdfNode info;
                if (!r.Apps.TryGetValue(g.AppId, out info))
                    continue;
                string latest = Patcher.LatestBuildId(info, g.App.Branch);
                if (latest.Length == 0)
                    continue;
                _latest[g.AppId] = latest;
                g.LatestRaw = latest;
            }
            return r;
        }

        private async Task CheckAll()
        {
            List<GameItem> games = _view.Cast<GameItem>().ToList();
            if (games.Count == 0)
                return;
            HideNotice();
            if (await Fetch(games) == null)
                return;

            int updates = games.Count(g => g.LatestRaw != null && g.LatestRaw != g.App.BuildId);
            string msg = updates == 0
                             ? L.T("Main.AllUpToDate")
                             : L.F("Main.UpdatesFound", updates);
            Log(msg);
            foreach (GameItem g in games.Where(x => x.LatestRaw != null && x.LatestRaw != x.App.BuildId))
                Log("    " + L.F("Main.UpdateLine", g.Name, g.App.BuildId, g.LatestRaw));
        }

        private async Task CheckSelected()
        {
            GameItem g = SelectedGame;
            if (g == null)
                return;
            HideNotice();
            if (await Fetch(new List<GameItem> { g }) == null)
            {
                ShowNotice("Danger", L.T("Notice.FetchFailed"));
                return;
            }
            if (g.LatestRaw == null)
                ShowNotice("Warning", L.T("Notice.NoLatest"));
            else if (g.LatestRaw == g.App.BuildId)
                ShowNotice("Success", L.F("Notice.UpToDate", g.App.BuildId));
            else
                ShowNotice("Warning", L.F("Notice.UpdateAvailable", g.LatestRaw));
        }

        private async Task FreezeSelected()
        {
            GameItem g = SelectedGame;
            if (g == null)
                return;
            HideNotice();
            if (!ConfirmSteamClosed())
                return;
            if (!Dialog.Ask(this, DialogKind.Question, L.T("Detail.Freeze"), L.F("Freeze.ConfirmMsg", g.Name),
                            L.T("Freeze.ConfirmBtn")))
                return;

            SteamCmdResult r = await Fetch(new List<GameItem> { g });
            if (r == null)
            {
                ShowNotice("Danger", L.T("Notice.FetchFailed"));
                return;
            }

            InstalledApp fresh;
            try
            {
                // The manifest may have changed while SteamCMD was running; re-read it.
                fresh = SteamLocator.ReadInstalledApp(g.App.AcfPath);
            }
            catch (Exception ex)
            {
                Log("✘ " + g.Name + ": " + ex.Message);
                ShowNotice("Danger", ex.Message);
                return;
            }

            var opt = new FreezeOptions
            {
                UpdateOnlyOnLaunch = _onlyOnLaunchBox.IsChecked == true,
                ClearDownloadCache = _clearCacheBox.IsChecked == true,
                SteamRoot = _settings.SteamRoot,
            };
            FreezeResult res = Patcher.Freeze(fresh, r.Apps, opt);
            if (!res.Ok)
            {
                Log("✘ " + g.Name + ": " + res.Error);
                ShowNotice("Danger", res.Error);
                return;
            }

            if (res.AlreadyLatest)
            {
                Log(L.F("Freeze.AlreadyLog", g.Name, res.LatestBuildId));
                ShowNotice("Success", L.F("Freeze.AlreadyNotice", res.LatestBuildId));
            }
            else
            {
                Log(L.F("Freeze.DoneLog", g.Name, res.LatestBuildId));
                foreach (string c in res.Changes)
                    Log("    " + c);
                foreach (string w in res.Warnings)
                    Log("    ! " + w);
                string extra = res.RemovedDepots > 0 ? " " + L.F("Freeze.DepotsFixed", res.RemovedDepots) : "";
                ShowNotice("Success", L.F("Freeze.DoneNotice", res.LatestBuildId, extra));
            }
            RefreshApps();
        }

        private void RestoreSelected()
        {
            GameItem g = SelectedGame;
            if (g == null || !g.App.HasBackup)
                return;
            HideNotice();
            if (!ConfirmSteamClosed())
                return;
            if (!Dialog.Ask(this, DialogKind.Warning, L.T("Restore.Title"), L.F("Restore.Msg", g.Name),
                            L.T("Detail.Restore")))
                return;
            try
            {
                Patcher.Restore(g.App);
                Log(L.F("Restore.DoneLog", g.Name));
                ShowNotice("Success", L.T("Restore.DoneNotice"));
            }
            catch (Exception ex)
            {
                Log("✘ " + g.Name + ": " + ex.Message);
                ShowNotice("Danger", ex.Message);
            }
            RefreshApps();
        }

        // ---------------------------------------------------------------- windows

        private void OpenCreate()
        {
            if (_settings.SteamRoot.Length == 0 || !Directory.Exists(_settings.SteamRoot))
            {
                Dialog.Info(this, DialogKind.Warning, L.T("Steam.NotFoundTitle"), L.T("Steam.NotFoundMsg"));
                return;
            }
            var w = new CreateWindow(this, _settings, Log);
            w.ShowDialog();
            RefreshApps();
        }

        private void OpenSettings()
        {
            var w = new SettingsWindow(this, _settings);
            if (w.ShowDialog() == true)
            {
                foreach (GameItem i in _items)
                {
                    i.CoverRequested = false;
                    i.Refresh();
                }
                HideNotice();
                RefreshApps();
            }
        }
    }
}
