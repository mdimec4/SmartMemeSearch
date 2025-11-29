using SmartMemeSearch.Wpf.Services;
using SmartMemeSearch.Wpf.Views;
using System.Collections.ObjectModel;
using System.Windows.Threading;


namespace SmartMemeSearch.Wpf.ViewModels
{
    public class MainViewModel : BindableBase
    {
        private readonly Dispatcher _dispatcher = Dispatcher.CurrentDispatcher;
        private readonly DispatcherTimer _debounceTimer;
        private const int DebounceDelayMs = 200;


        public ObservableCollection<SearchResult> Results { get; } = new();
        public event Action? SearchCompleted;

        private static readonly SemaphoreSlim _searchLock = new SemaphoreSlim(1, 1);


        private string _query = string.Empty;
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

        //private bool _isImportingFolder;
        private bool _syncRunning = false;
        private bool _autoSyncStarted = false;


        private CancellationTokenSource searchCenclationSource = new CancellationTokenSource();

        public MainViewModel()
        {
            _clip = new ClipService();
            _ocr = new OcrService();
            _db = new DatabaseService();
            _importer = new ImporterService(_clip, _ocr, _db);
            _search = new SearchService(_clip, _db);
            _autoSync = new AutoSyncService(_importer, _db);

            _debounceTimer = new DispatcherTimer();
            _debounceTimer.Interval = TimeSpan.FromMilliseconds(DebounceDelayMs);

            _debounceTimer.Tick += (s, e) =>
            {
                _debounceTimer.Stop();
                if (searchCenclationSource.Token.CanBeCanceled)
                {
                    searchCenclationSource.Cancel();
                    searchCenclationSource = new CancellationTokenSource();
                }
                Search(searchCenclationSource.Token);
            };

            ThumbnailCache.Initialize(_dispatcher);
        }

        public void Search(CancellationToken token)
        {
            if (token.IsCancellationRequested)
                return;
            var results = _search.Search(Query);
            if (token.IsCancellationRequested)
                return;
            //Limit the number of relavant results
            //results = results.Take(500).ToList();


            if (token.IsCancellationRequested)
                return;
            _ = Task.Run(async () =>
            {
                if (token.IsCancellationRequested)
                    return;

                _searchLock.Wait();
                try
                {
                    if (token.IsCancellationRequested)
                        return;
                    // Clear collection on UI thread
                    _dispatcher.Invoke(() => Results.Clear());



                    foreach (var r in results)
                    {
                        if (token.IsCancellationRequested)
                            return;
                        // Add items on UI thread
                        _dispatcher.Invoke(() =>
                        {
                            Results.Add(r);
                        });

                        if (token.IsCancellationRequested)
                            return;

                        var thumb = ThumbnailCache.TryGetMemory(r.FilePath);
                        if (thumb != null)
                        {
                            _dispatcher.Invoke(() =>
                            {
                                r.Thumbnail = thumb;
                            });
                        }
                    }

                    if (token.IsCancellationRequested)
                        return;

                    _dispatcher.Invoke(() =>
                    {
                        // 🔥 Fire event after results are added
                        SearchCompleted?.Invoke();
                    });

                    if (token.IsCancellationRequested)
                        return;

                    // create/load thumbnail files if they don't exist already in memory cache
                    List<Task> thumbnailTasksToWait = new List<Task>(Environment.ProcessorCount);
                    foreach (var r in results)
                    {
                        if (token.IsCancellationRequested)
                            return;
                        if (r.Thumbnail != null)
                            continue;
                        if (token.IsCancellationRequested)
                            return;

                        thumbnailTasksToWait.Add(LoadThumbnailAsync(r));
                        if (thumbnailTasksToWait.Count >= Environment.ProcessorCount)
                        {
                            foreach (Task t in thumbnailTasksToWait)
                            {
                                if (token.IsCancellationRequested)
                                    return;
                                await t;
                            }
                            thumbnailTasksToWait.Clear();
                        }
                    }
                }
                finally
                {
                    _searchLock.Release();
                }
            });
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

        public void StartAutoSyncLoop()
        {
            if (_autoSyncStarted)
                return;

            _autoSyncStarted = true;
            _ = Task.Run(async () =>
                {
                    await RunExclusiveAutoSyncFromVM(); // first sync

                    while (true)
                    {
                        await Task.Delay(TimeSpan.FromMinutes(5));
                        await RunExclusiveAutoSyncFromVM();
                    }
                });
        }

        private async Task RunExclusiveAutoSyncFromVM()
        {
            if (!TryBeginSync())
                return;

            // Use the UI thread dispatcher stored in the Page field
            _dispatcher.Invoke(() =>
            {
                IsImporting = true;
                CurrentFile = "Checking folders...";
                ProgressValue = 0;
            });

            try
            {
                await AutoSyncAllAsync();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("AUTO SYNC ERROR: " + ex);
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

        public bool HasAnyFolders()
        {
            return _db.GetFolders().Any();
        }

        public async Task ExecuteManageFoldersAsync()
        {
            ManageFolders();
        }

        public void ManageFolders()
        {
            var existing = _db.GetFolders().ToList();

            var dialog = new FolderManagerDialog(existing);
            dialog.Owner = App.Current.MainWindow;

            if (dialog.ShowDialog() != true)
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
    }
}
