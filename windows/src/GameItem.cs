using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Media;

namespace Xuefiest
{
    // View model for a game card in the library.
    public class GameItem : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private InstalledApp _app;
        private string _latest;
        private ImageSource _cover, _hero, _logo;

        internal bool CoverRequested;
        internal bool DetailArtRequested;

        public GameItem(InstalledApp app)
        {
            _app = app;
        }

        public InstalledApp App
        {
            get { return _app; }
            set
            {
                _app = value;
                RaiseAll();
            }
        }

        // Latest build reported by SteamCMD, or null if not checked yet.
        public string LatestRaw
        {
            get { return _latest; }
            set
            {
                _latest = value;
                RaiseAll();
            }
        }

        public string AppId { get { return _app.AppId; } }
        public string Name { get { return _app.Name; } }
        public string InstalledBuild { get { return string.IsNullOrEmpty(_app.BuildId) ? "—" : _app.BuildId; } }
        public string LatestBuild { get { return string.IsNullOrEmpty(_latest) ? "—" : _latest; } }
        public string SizeText { get { return Ui.FormatSize(_app.SizeOnDisk); } }
        public string Location { get { return _app.InstallPath; } }

        public string SubTitle
        {
            get { return L.F("Game.SubTitle", _app.AppId, _app.Branch); }
        }

        public string Initials
        {
            get
            {
                string result = "";
                foreach (string w in _app.Name.Split(new[] { ' ', ':', '-', '_' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    if (char.IsLetterOrDigit(w[0]))
                        result += char.ToUpperInvariant(w[0]);
                    if (result.Length == 2)
                        break;
                }
                return result.Length > 0 ? result : "?";
            }
        }

        public Visibility FrozenVisibility
        {
            get { return _app.HasBackup ? Visibility.Visible : Visibility.Collapsed; }
        }

        private string StatusKey
        {
            get
            {
                int flags;
                int.TryParse(_app.StateFlags, out flags);
                bool known = !string.IsNullOrEmpty(_latest);
                if (known && _latest != _app.BuildId)
                    return "Warning";
                if (_app.HasBackup)
                    return "Ice";
                if (known)
                    return "Success";
                if ((flags & 2) != 0)
                    return "Warning";
                return "Faint";
            }
        }

        public string StatusText
        {
            get
            {
                int flags;
                int.TryParse(_app.StateFlags, out flags);
                bool known = !string.IsNullOrEmpty(_latest);
                if (known && _latest != _app.BuildId)
                    return L.T(_app.HasBackup ? "Game.NewUpdate" : "Game.UpdateAvailable");
                if (_app.HasBackup)
                    return L.T("Game.Frozen");
                if (known)
                    return L.T("Game.UpToDate");
                if ((flags & 2) != 0)
                    return L.T("Game.UpdatePending");
                return L.T("Game.Installed");
            }
        }

        public Brush StatusBrush { get { return Ui.Brush(StatusKey); } }
        public Brush StatusSoftBrush { get { return Ui.Soft(StatusKey, 0x24); } }

        public ImageSource Cover
        {
            get { return _cover; }
            set { _cover = value; Raise("Cover"); }
        }

        public ImageSource Hero
        {
            get { return _hero; }
            set { _hero = value; Raise("Hero"); }
        }

        public ImageSource Logo
        {
            get { return _logo; }
            set { _logo = value; Raise("Logo"); }
        }

        private void Raise(string name)
        {
            PropertyChangedEventHandler h = PropertyChanged;
            if (h != null)
                h(this, new PropertyChangedEventArgs(name));
        }

        // Re-evaluates all bound text, e.g. after a language change.
        public void Refresh()
        {
            RaiseAll();
        }

        private void RaiseAll()
        {
            Raise(string.Empty); // refresh all bindings
        }
    }
}
