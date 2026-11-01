using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace Xuefiest
{
    // A row in the depot list.
    public class DepotRow : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private bool _checked;

        public DepotInfo Depot { get; private set; }
        public string Title { get; private set; }
        public string Tags { get; private set; }
        public string SizeText { get; private set; }
        public string Note { get; private set; }

        public DepotRow(DepotInfo d)
        {
            Depot = d;
            _checked = d.DefaultSelected;
            if (d.IsShared)
                Title = L.T("Depot.Shared");
            else if (d.Name.Length > 0)
                Title = d.Name;
            else
                Title = L.F("Depot.Title", d.Id);

            var tags = new List<string> { "#" + d.Id };
            if (d.IsShared)
                tags.Add(L.F("Depot.SharedTag", d.DepotFromApp));
            if (d.OsList.Length > 0)
                tags.Add(d.OsList.Replace("windows", "Windows").Replace("linux", "Linux").Replace("macos", "macOS"));
            if (d.OsArch.Length > 0)
                tags.Add(d.OsArch + "-bit");
            if (d.Language.Length > 0)
                tags.Add(d.Language);
            if (d.DlcAppId.Length > 0)
                tags.Add("DLC " + d.DlcAppId);
            if (d.LowViolence)
                tags.Add(L.T("Depot.LowViolenceTag"));
            Tags = string.Join("  ·  ", tags);
            SizeText = d.IsShared ? "" : Ui.FormatSize(d.Size);
            Note = d.Note;
        }

        public bool IsChecked
        {
            get { return _checked; }
            set
            {
                _checked = value;
                Raise("IsChecked");
                Raise("RowOpacity");
            }
        }

        public double RowOpacity { get { return _checked ? 1.0 : 0.55; } }

        private void Raise(string name)
        {
            PropertyChangedEventHandler h = PropertyChanged;
            if (h != null)
                h(this, new PropertyChangedEventArgs(name));
        }
    }

    public class StoreResult
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public string IdText { get { return "AppID " + Id; } }
        public ImageSource Image { get; set; }
    }

    // Creates an up-to-date app manifest for an existing game folder, so Steam picks the game
    // up as installed without starting an update or validation.
    internal class CreateWindow : Window
    {
        private readonly Settings _settings;
        private readonly Action<string> _log;
        private readonly SteamCmdRunner _runner = new SteamCmdRunner();
        private readonly ObservableCollection<DepotRow> _rows = new ObservableCollection<DepotRow>();

        private readonly TextBox _folderBox, _appIdBox, _branchBox;
        private readonly TextBlock _folderHintIcon, _folderHint, _appIdHint, _previewName, _previewInfo, _statusText;
        private readonly FrameworkElement _folderHintPanel, _gamePreview, _contentCard, _depotBox, _depotPlaceholder;
        private readonly ImageBrush _previewImage;
        private readonly ListBox _searchList;
        private readonly ComboBox _langCombo;
        private readonly ItemsControl _depotList;
        private readonly ProgressBar _progress;
        private readonly Button _browseBtn, _searchBtn, _fetchBtn, _createBtn;

        private VdfNode _appInfo;
        private string _appInfoId;
        private string _appInfoName;
        private string _folderChecked;
        private bool _busy, _canCreate, _suppressLang;

        public CreateWindow(Window owner, Settings settings, Action<string> log)
        {
            _settings = settings;
            _log = log;
            Ui.Prepare(this);
            Title = L.T("Create.Title");
            Owner = owner;
            Width = 820;
            Height = 820;
            MinWidth = 700;
            MinHeight = 560;
            ShowInTaskbar = false;
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            var root = (FrameworkElement)Ui.Load("CreateWindow.xaml");
            Content = root;
            _folderBox = Ui.Find<TextBox>(root, "FolderBox");
            _appIdBox = Ui.Find<TextBox>(root, "AppIdBox");
            _branchBox = Ui.Find<TextBox>(root, "BranchBox");
            _folderHintIcon = Ui.Find<TextBlock>(root, "FolderHintIcon");
            _folderHint = Ui.Find<TextBlock>(root, "FolderHint");
            _appIdHint = Ui.Find<TextBlock>(root, "AppIdHint");
            _previewName = Ui.Find<TextBlock>(root, "PreviewName");
            _previewInfo = Ui.Find<TextBlock>(root, "PreviewInfo");
            _statusText = Ui.Find<TextBlock>(root, "StatusText");
            _folderHintPanel = Ui.Find<FrameworkElement>(root, "FolderHintPanel");
            _gamePreview = Ui.Find<FrameworkElement>(root, "GamePreview");
            _contentCard = Ui.Find<FrameworkElement>(root, "ContentCard");
            _depotBox = Ui.Find<FrameworkElement>(root, "DepotBox");
            _depotPlaceholder = Ui.Find<FrameworkElement>(root, "DepotPlaceholder");
            _previewImage = Ui.Find<ImageBrush>(root, "PreviewImage");
            _searchList = Ui.Find<ListBox>(root, "SearchList");
            _langCombo = Ui.Find<ComboBox>(root, "LangCombo");
            _depotList = Ui.Find<ItemsControl>(root, "DepotList");
            _progress = Ui.Find<ProgressBar>(root, "Progress");
            _browseBtn = Ui.Find<Button>(root, "BrowseBtn");
            _searchBtn = Ui.Find<Button>(root, "SearchBtn");
            _fetchBtn = Ui.Find<Button>(root, "FetchBtn");
            _createBtn = Ui.Find<Button>(root, "CreateBtn");
            _depotList.ItemsSource = _rows;

            _browseBtn.Click += (s, e) => BrowseFolder();
            _folderBox.LostFocus += (s, e) => CheckFolder(_folderBox.Text.Trim());
            _searchBtn.Click += async (s, e) => await SearchStore();
            _fetchBtn.Click += async (s, e) => await FetchInfo();
            _appIdBox.KeyDown += async (s, e) =>
            {
                if (e.Key == Key.Enter)
                    await FetchInfo();
            };
            _searchList.SelectionChanged += (s, e) =>
            {
                var r = _searchList.SelectedItem as StoreResult;
                if (r != null)
                    _appIdBox.Text = r.Id;
            };
            _searchList.MouseDoubleClick += async (s, e) => await FetchInfo();
            _langCombo.SelectionChanged += (s, e) =>
            {
                if (!_suppressLang)
                    PopulateDepots();
            };
            _branchBox.LostFocus += (s, e) => PopulateDepots();
            _createBtn.Click += async (s, e) => await Create();
            Ui.Find<Button>(root, "CloseBtn").Click += (s, e) => Close();
            Closing += (s, e) => _runner.Cancel();

            Status(L.T("Create.StatusStart"));
        }

        private void Status(string text)
        {
            _statusText.Text = text;
            _statusText.ToolTip = text;
        }

        private void Log(string text)
        {
            Status(text);
            if (_log != null)
                _log(text);
        }

        private void SetBusy(bool busy)
        {
            _busy = busy;
            _progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            _browseBtn.IsEnabled = !busy;
            _searchBtn.IsEnabled = !busy;
            _fetchBtn.IsEnabled = !busy;
            _createBtn.IsEnabled = !busy && _canCreate;
        }

        // ---------------------------------------------------------------- folder and AppID

        private void BrowseFolder()
        {
            string start = Directory.Exists(_folderBox.Text) ? _folderBox.Text : null;
            if (start == null)
            {
                List<string> libs = SteamLocator.LibraryFolders(_settings.SteamRoot);
                if (libs.Count > 0)
                    start = Path.Combine(libs[0], "common");
            }
            string d = FolderPicker.Pick(this, L.T("Create.PickFolder"), start);
            if (d == null)
                return;
            _folderBox.Text = d;
            CheckFolder(d);
        }

        private void CheckFolder(string folder)
        {
            if (folder.Length == 0 || folder == _folderChecked)
                return;
            _folderChecked = folder;
            if (!Directory.Exists(folder))
            {
                FolderHint("", "Warning", L.T("Create.FolderMissing"));
                return;
            }

            string installDir;
            string lib = ManifestBuilder.FindLibraryFor(folder, SteamLocator.LibraryFolders(_settings.SteamRoot), out installDir);
            if (lib != null)
                FolderHint("", "Success", L.F("Create.InLibrary", lib));
            else
                FolderHint("", "Accent", L.T("Create.OutsideLibrary"));

            string id = ManifestBuilder.FindAppIdInFolder(folder);
            if (id != null)
            {
                _appIdBox.Text = id;
                AppIdHint(L.T("Create.AppIdFound"));
                Status(L.F("Create.AppIdFoundStatus", id));
            }
            else if (_appIdBox.Text.Trim().Length == 0)
            {
                _appIdBox.Text = Path.GetFileName(folder.TrimEnd('\\'));
                AppIdHint(L.T("Create.AppIdGuess"));
                Status(L.T("Create.AppIdNotFoundStatus"));
            }
        }

        private void FolderHint(string glyph, string color, string text)
        {
            _folderHintIcon.Text = glyph;
            _folderHintIcon.Foreground = Ui.Brush(color);
            _folderHint.Text = text;
            _folderHintPanel.Visibility = Visibility.Visible;
        }

        private void AppIdHint(string text)
        {
            _appIdHint.Text = text;
            _appIdHint.Visibility = Visibility.Visible;
        }

        private async Task SearchStore()
        {
            string term = _appIdBox.Text.Trim();
            long dummy;
            if (term.Length == 0 || long.TryParse(term, out dummy))
                term = Path.GetFileName(_folderBox.Text.Trim().TrimEnd('\\'));
            if (string.IsNullOrEmpty(term))
            {
                Status(L.T("Create.SearchNeedTerm"));
                return;
            }

            SetBusy(true);
            try
            {
                Status(L.F("Create.Searching", term));
                string json;
                using (var wc = new WebClient { Encoding = Encoding.UTF8 })
                {
                    string url = "https://store.steampowered.com/api/storesearch/?l=" + (L.Language == "tr" ? "turkish" : "english")
                                 + "&cc=" + StoreRegion() + "&term=" + Uri.EscapeDataString(term);
                    json = await wc.DownloadStringTaskAsync(new Uri(url));
                }

                var results = new List<StoreResult>();
                var data = new JavaScriptSerializer().Deserialize<Dictionary<string, object>>(json);
                object itemsObj;
                var items = data != null && data.TryGetValue("items", out itemsObj) ? itemsObj as IEnumerable : null;
                if (items != null)
                {
                    foreach (object o in items)
                    {
                        var it = o as Dictionary<string, object>;
                        if (it == null || !it.ContainsKey("id"))
                            continue;
                        var r = new StoreResult
                        {
                            Id = Convert.ToString(it["id"]),
                            Name = it.ContainsKey("name") ? Convert.ToString(it["name"]) : "",
                        };
                        object img;
                        if (it.TryGetValue("tiny_image", out img) && img is string)
                        {
                            try { r.Image = new BitmapImage(new Uri((string)img)); }
                            catch (Exception) { }
                        }
                        results.Add(r);
                    }
                }
                _searchList.ItemsSource = results;
                _searchList.Visibility = results.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
                Status(results.Count > 0
                           ? L.F("Create.SearchResults", results.Count)
                           : L.T("Create.SearchNone"));
            }
            catch (Exception ex)
            {
                Log(L.F("Create.SearchFailed", ex.Message));
            }
            finally
            {
                SetBusy(false);
            }
        }

        // The store search returns nothing without a region (cc); use the Windows region.
        private static string StoreRegion()
        {
            try
            {
                string cc = System.Globalization.RegionInfo.CurrentRegion.TwoLetterISORegionName;
                if (cc.Length == 2)
                    return cc;
            }
            catch (Exception)
            {
            }
            return "US";
        }

        // ---------------------------------------------------------------- SteamCMD

        private async Task FetchInfo()
        {
            if (_busy)
                return;
            string appId = _appIdBox.Text.Trim();
            long dummy;
            if (!long.TryParse(appId, out dummy))
            {
                // A name was entered: search the store first.
                await SearchStore();
                return;
            }

            string exe = await SteamCmdInstaller.EnsureAsync(this, _settings, Log);
            if (exe == null)
                return;

            SetBusy(true);
            Log(L.F("Create.Fetching", appId));
            SteamCmdResult r;
            try
            {
                r = await _runner.FetchAppInfoAsync(exe, new List<string> { appId },
                                                    line => Dispatcher.BeginInvoke(new Action(() =>
                                                    {
                                                        if (_log != null)
                                                            _log(line);
                                                    })));
            }
            finally
            {
                SetBusy(false);
            }

            VdfNode info;
            if (!r.Ok || !r.Apps.TryGetValue(appId, out info))
            {
                Log("✘ " + (r.Error ?? L.T("Create.NoAppInfo")));
                return;
            }

            _appInfo = info;
            _appInfoId = appId;
            VdfNode nameNode = info.Find("common", "name");
            _appInfoName = nameNode != null ? nameNode.Value : "";
            _searchList.Visibility = Visibility.Collapsed;
            _appIdHint.Visibility = Visibility.Collapsed;

            // Preview image: Steam's local cache, otherwise the store image.
            string local = GameArt.FindLocal(_settings.SteamRoot, appId, GameArt.Header) ?? GameArt.FindCached(appId, GameArt.Header);
            try
            {
                _previewImage.ImageSource = local != null
                                                ? GameArt.LoadImage(local, 400)
                                                : new BitmapImage(new Uri(GameArt.CdnUrl(appId, "header.jpg")));
            }
            catch (Exception)
            {
                _previewImage.ImageSource = null;
            }
            _previewName.Text = _appInfoName.Length > 0 ? _appInfoName : "AppID " + appId;
            _gamePreview.Visibility = Visibility.Visible;

            // Language list; defaults to the Steam client language.
            string steamLang = null;
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\Valve\Steam"))
                    steamLang = k == null ? null : k.GetValue("Language") as string;
            }
            catch (Exception)
            {
            }
            List<string> langs = ManifestBuilder.Languages(info);
            _suppressLang = true;
            _langCombo.ItemsSource = langs;
            int idx = steamLang != null ? langs.FindIndex(l => string.Equals(l, steamLang, StringComparison.OrdinalIgnoreCase)) : -1;
            if (idx < 0)
                idx = langs.FindIndex(l => l == "english");
            _langCombo.SelectedIndex = Math.Max(0, idx);
            _suppressLang = false;

            _contentCard.IsEnabled = true;
            PopulateDepots();
        }

        private string CurrentBranch
        {
            get
            {
                string b = _branchBox.Text.Trim();
                return b.Length == 0 ? "public" : b;
            }
        }

        private string CurrentLanguage
        {
            get { return _langCombo.SelectedItem as string ?? "english"; }
        }

        private void PopulateDepots()
        {
            if (_appInfo == null)
                return;
            string build = Patcher.LatestBuildId(_appInfo, CurrentBranch);
            List<DepotInfo> depots = ManifestBuilder.ListDepots(_appInfo, CurrentBranch, CurrentLanguage);

            _rows.Clear();
            foreach (DepotInfo d in depots)
                _rows.Add(new DepotRow(d));
            _depotBox.Visibility = depots.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
            _depotPlaceholder.Visibility = depots.Count > 0 ? Visibility.Collapsed : Visibility.Visible;

            _previewInfo.Text = L.F("Create.PreviewInfo", _appInfoId, CurrentBranch,
                                    build.Length > 0 ? build : L.T("Common.NotFound"), depots.Count);

            if (depots.Count == 0)
                Log(L.T("Create.NoDepots"));
            else if (build.Length == 0)
                Log(L.F("Create.NoBuild", CurrentBranch));
            else
                Status(L.T("Create.ReviewDepots"));

            _canCreate = depots.Count > 0 && build.Length > 0;
            _createBtn.IsEnabled = !_busy && _canCreate;
        }

        // ---------------------------------------------------------------- create

        private async Task Create()
        {
            if (_appInfo == null)
                return;
            string folder = _folderBox.Text.Trim().TrimEnd('\\');
            if (folder.Length == 0 || !Directory.Exists(folder))
            {
                Status(L.T("Create.NeedFolder"));
                return;
            }
            if (_appIdBox.Text.Trim() != _appInfoId)
            {
                Status(L.T("Create.AppIdChanged"));
                return;
            }

            List<DepotInfo> selected = _rows.Where(r => r.IsChecked).Select(r => r.Depot).ToList();
            if (!selected.Any(d => !d.IsShared))
            {
                Status(L.T("Create.NeedDepot"));
                return;
            }
            string buildId = Patcher.LatestBuildId(_appInfo, CurrentBranch);
            if (buildId.Length == 0)
                return;

            // Steam starts an update to remove depots that don't match the game language.
            List<string> wrongLang = selected
                .Where(d => !d.IsShared && d.Language.Length > 0
                            && !string.Equals(d.Language, CurrentLanguage, StringComparison.OrdinalIgnoreCase))
                .Select(d => d.Id + " (" + d.Language + ")").ToList();
            if (wrongLang.Count > 0
                && !Dialog.Ask(this, DialogKind.Warning, L.T("Create.LangMismatchTitle"),
                               L.F("Create.LangMismatchMsg", CurrentLanguage, string.Join("\n", wrongLang)),
                               L.T("Common.ContinueAnyway")))
                return;

            if (SteamLocator.IsSteamRunning()
                && !Dialog.Ask(this, DialogKind.Warning, L.T("Steam.RunningTitle"), L.T("Steam.RunningMsg"),
                               L.T("Common.ContinueAnyway")))
                return;

            // 1) The folder must be inside a library's steamapps\common.
            List<string> libs = SteamLocator.LibraryFolders(_settings.SteamRoot);
            string installDir;
            string library = ManifestBuilder.FindLibraryFor(folder, libs, out installDir);
            if (library == null)
            {
                folder = OfferMove(folder, libs);
                if (folder == null)
                    return;
                library = ManifestBuilder.FindLibraryFor(folder, libs, out installDir);
                if (library == null)
                    return;
            }

            // 2) Existing manifests for the same game.
            string acfPath = ManifestBuilder.AcfPathFor(library, _appInfoId);
            foreach (string lib in libs)
            {
                string other = ManifestBuilder.AcfPathFor(lib, _appInfoId);
                if (!File.Exists(other) || string.Equals(other, acfPath, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!Dialog.Ask(this, DialogKind.Warning, L.T("Create.OtherLibTitle"), L.F("Create.OtherLibMsg", other),
                                L.T("Common.ContinueAnyway")))
                    return;
            }
            if (File.Exists(acfPath)
                && !Dialog.Ask(this, DialogKind.Question, L.T("Create.ExistsTitle"), L.F("Create.ExistsMsg", acfPath),
                               L.T("Create.Overwrite")))
                return;

            SetBusy(true);
            try
            {
                Status(L.T("Create.Sizing"));
                string scan = folder;
                long size = await Task.Run(() => ManifestBuilder.FolderSize(scan));

                string name = _appInfoName.Length > 0 ? _appInfoName : installDir;
                VdfNode acf = ManifestBuilder.BuildAcf(_appInfoId, name, installDir, buildId, selected, CurrentLanguage,
                                                       CurrentBranch, size, ManifestBuilder.MostRecentSteamId(_settings.SteamRoot));
                if (File.Exists(acfPath))
                {
                    string bak = SteamLocator.BackupPath(acfPath);
                    if (!File.Exists(bak))
                        File.Copy(acfPath, bak);
                }
                Vdf.Save(acfPath, acf);

                Log(L.F("Create.DoneLog", acfPath));
                if (_log != null)
                    _log("    " + L.F("Create.DoneSummary", name, buildId, selected.Count(d => !d.IsShared), CurrentLanguage));
                SetBusy(false);
                Dialog.Info(this, DialogKind.Success, L.T("Create.DoneTitle"), L.F("Create.DoneMsg", name));
                Close();
            }
            catch (Exception ex)
            {
                Log(L.F("Create.Failed", ex.Message));
            }
            finally
            {
                if (IsLoaded)
                    SetBusy(false);
            }
        }

        // Offers to move the folder into a library on the same drive. A same-drive move is
        // instant; no files are copied.
        private string OfferMove(string folder, List<string> libs)
        {
            string drive = Path.GetPathRoot(Path.GetFullPath(folder));
            string target = libs.FirstOrDefault(l => string.Equals(Path.GetPathRoot(l), drive, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                Dialog.Info(this, DialogKind.Warning, L.T("Create.NeedLibraryTitle"), L.F("Create.NeedLibraryMsg", drive));
                return null;
            }

            string common = Path.Combine(target, "common");
            string dest = Path.Combine(common, Path.GetFileName(folder));
            if (Directory.Exists(dest) || File.Exists(dest))
            {
                Dialog.Info(this, DialogKind.Warning, L.T("Create.CantMoveTitle"), L.F("Create.CantMoveMsg", dest));
                return null;
            }
            if (!Dialog.Ask(this, DialogKind.Question, L.T("Create.MoveTitle"), L.F("Create.MoveMsg", folder, dest),
                            L.T("Create.MoveBtn")))
                return null;

            try
            {
                Directory.CreateDirectory(common);
                Directory.Move(folder, dest);
            }
            catch (Exception ex)
            {
                Log(L.F("Create.MoveFailed", ex.Message));
                return null;
            }
            _folderBox.Text = dest;
            _folderChecked = dest;
            FolderHint("", "Success", L.F("Create.Moved", dest));
            Log(L.F("Create.MovedLog", dest));
            return dest;
        }
    }
}
