using SmartMemeSearch.Wpf.Services;
using SmartMemeSearch.Wpf.Views;
using System.Collections.ObjectModel;
using System.Windows.Input;
using System.Windows.Threading;


namespace SmartMemeSearch.Wpf.ViewModels
{
    public class MainViewModel : BindableBase
    {
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly DispatcherTimer _debounceTimer;
        private const int DebounceDelayMs = 200;

        private string _query = string.Empty;

        public ObservableCollection<SearchResult> Results { get; } = new();

        public string Query
        {
            get => _query;
            set
            {
                if (SetProperty(ref _query, value))
                {
                    // start debounce every time the user types
                    _debounceTimer.Stop();
                    _debounceTimer.Start();
                }
            }
        }

        private bool _isImporting;
        public bool IsImporting
        {
            get => _isImporting;
            set => SetProperty(ref _isImporting, value);
        }

        private string _currentFile = "";
        public string CurrentFile
        {
            get => _currentFile;
            set => SetProperty(ref _currentFile, value);
        }

        private double _progressValue;
        public double ProgressValue
        {
            get => _progressValue;
            set => SetProperty(ref _progressValue, value);
        }

        private bool _isPremium;
        public bool IsPremium
        {
            get => _isPremium;
            set => SetProperty(ref _isPremium, value);
        }

        private readonly ClipService _clip;
        private readonly OcrService _ocr;
        private readonly DatabaseService _db;
        private readonly ImporterService _importer;
        private readonly SearchService _search;
        private readonly AutoSyncService _autoSync;
        private readonly StoreService _store;

        //private bool _isImportingFolder;
        private bool _syncRunning = false;


        public MainViewModel()
        {
            _clip = new ClipService();
            _ocr = new OcrService();
            _db = new DatabaseService();
            _importer = new ImporterService(_clip, _ocr, _db);
            _search = new SearchService(_clip, _db);
            _autoSync = new AutoSyncService(_importer, _db);

            _store = new StoreService(/*App.Current.MainWindow*/);

            _debounceTimer = new DispatcherTimer();
            _debounceTimer.Interval = TimeSpan.FromMilliseconds(DebounceDelayMs);
            _debounceTimer.Tick += (s, e) =>
            {
                _debounceTimer.Stop();
                Search();
            };

            ThumbnailCache.Initialize(_dispatcher);

            // Kick off premium/license check (auto-restore)
            _ = InitializePremiumAsync();
        }

        public void Search()
        {
            var results = _search.Search(Query);

            // Clear collection on UI thread
            _dispatcher.Invoke(() => Results.Clear());

            foreach (var r in results)
            {
                // Add items on UI thread
                _dispatcher.Invoke(() =>
                {
                    Results.Add(r);
                });

                var thumb = ThumbnailCache.TryGetMemory(r.FilePath);
                if (thumb != null)
                    r.Thumbnail = thumb;
                else
                    _ = LoadThumbnailAsync(r);
            }
        }

        /*
        public async Task ImportFolder()
        {
            if (IsImporting)
                return;
            // Prevent double-click / re-entry
            if (_isImportingFolder)
                return;

            _isImportingFolder = true;

            try
            {
                // 1) Pick folder
                var picker = new FolderPicker();
                picker.FileTypeFilter.Add("*");

                // Hook picker to main window
                var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
                WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

                var folder = await picker.PickSingleFolderAsync();
                if (folder == null)
                    return;

                // 2) Update UI state
                IsImporting = true;
                CurrentFile = "Importing...";
                ProgressValue = 0;

                string rootPath = folder.Path;

                // 3) Run import work off UI thread
                await Task.Run(async () =>
                {
                    await _importer.ImportFolderAsync(
                        rootPath,
                        file => _dispatcher.TryEnqueue(() => CurrentFile = file),
                        p => _dispatcher.TryEnqueue(() => ProgressValue = p)
                    );
                });

                // 4) Done
                CurrentFile = "Done";
                ProgressValue = 1.0;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("IMPORT ERROR: " + ex);
                CurrentFile = "Import failed";
            }
            finally
            {
                IsImporting = false;
                _isImportingFolder = false;
            }
        }*/

        private async Task RunExclusiveAutoSyncFromVM()
        {
            if (!TryBeginSync())
                return;

            try
            {
                await AutoSyncAllAsync();
            }
            finally
            {
                _dispatcher.Invoke(() =>
                {
                    CurrentFile = "Done";
                    IsImporting = false;
                    ProgressValue = 1.0;
                });
                EndSync();
            }
        }

        public void ManageFolders()
        {
            var existing = _db.GetFolders().ToList();

            var dialog = new FolderManagerDialog(existing);
            dialog.Owner = App.Current.MainWindow;

            if (!dialog.ShowDialog() != true)
            {
                return;
            }

            var newList = dialog.Folders.ToList();

            // Save new folder list immediately
            _db.SetFolders(newList);

            // -------------------------------
            // Show UI status
            // -------------------------------

            IsImporting = true;
            CurrentFile = "Removing deleted files...";
            ProgressValue = 0;

            // -------------------------------
            // Move ALL heavy lifting off UI thread
            // -------------------------------
            _ = Task.Run(async () =>
            {
                // read all embeddings OFF UI THREAD
                var allEmbeds = _db.GetAllEmbeddings().ToList();
                int total = allEmbeds.Count;
                int index = 0;

                // open one DB connection for performance
                using var con = _db.OpenConnection();
                using var tr = con.BeginTransaction();

                foreach (var e in allEmbeds)
                {
                    bool inside = newList.Any(root =>
                        Views.FolderManagerDialog.IsInside(e.FilePath, root));

                    if (!inside)
                    {
                        _dispatcher.Invoke(() => CurrentFile = e.FilePath);
                        _db.RemoveEmbeddingWithinTransaction(con, e.FilePath);
                        ThumbnailCache.Delete(e.FilePath);
                    }

                    index++;

                    // update progress every 100 items (UI throttled)
                    if (index % 100 == 0)
                    {
                        double p = (double)index / total;
                        _dispatcher.Invoke(() => ProgressValue = p);
                    }
                }

                tr.Commit();

                // -------------------------------
                // Start background sync (exclusive)
                // -------------------------------
                if (!CheckSync())
                {
                    _dispatcher.Invoke(() => IsImporting = false);
                    return;
                }

                _dispatcher.Invoke(() =>
                {
                    CurrentFile = "Syncing folders...";
                    ProgressValue = 0;
                });

                // Use the existing helper
               await RunExclusiveAutoSyncFromVM();
            });
        }



        public bool TryBeginSync()
        {
            if (_syncRunning) return false;
            _syncRunning = true;
            return true;
        }

        public bool CheckSync()
        {
            if (_syncRunning) return false;
            return true;
        }

        public void EndSync()
        {
            _syncRunning = false;
        }


        private async Task LoadThumbnailAsync(SearchResult r)
        {
            try
            {
                var bmp = await ThumbnailCache.LoadAsync(r.FilePath);

                _dispatcher.Invoke(() =>
                {
                    r.Thumbnail = bmp;
                    OnPropertyChanged(nameof(Results));
                });
            }
            catch
            {
                // ignore errors for missing/bad files
            }
        }

        public async Task AutoSyncAllAsync()
        {
            await _autoSync.SyncAllAsync(
                file => _dispatcher.Invoke(() => CurrentFile = file),
                p => _dispatcher.Invoke(() => ProgressValue = p)
            );
        }


        private async Task InitializePremiumAsync()
        {
            try
            {
                bool owned = await _store.IsPremiumAsync();
                IsPremium = owned;
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("InitializePremiumAsync error: " + ex);
            }
        }

        public async Task RemoveAdsAsync()
        {
            if (IsPremium)
                return; // already premium

            bool success = await _store.PurchaseRemoveAdsAsync();
            if (success)
            {
                IsPremium = true;
                // Optional: you could also trigger a toast or status text here.
                CurrentFile = "Thanks for supporting the app! Ads removed.";
            }
            else
            {
                // Optional: show feedback in UI
                CurrentFile = "Purchase cancelled or failed.";
            }
        }


    }
}
