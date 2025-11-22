using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using SmartMemeSearch.Services;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;


namespace SmartMemeSearch.ViewModels
{
    public class MainViewModel : BindableBase
    {
        private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();
        private readonly DispatcherQueueTimer _debounceTimer;
        private const int DebounceDelayMs = 200;

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

        public ObservableCollection<SearchResult> Results { get; } = new();

        public ICommand ManageFoldersCommand { get; }

        private readonly ClipService _clip;
        private readonly OcrService _ocr;
        private readonly DatabaseService _db;
        private readonly ImporterService _importer;
        private readonly SearchService _search;
        private readonly AutoSyncService _autoSync;

        //private bool _isImportingFolder;
        private bool _syncRunning = false;


        public MainViewModel()
        {
            _clip = new ClipService();
            _ocr = new OcrService(_dispatcher);
            _db = new DatabaseService();
            _importer = new ImporterService(_clip, _ocr, _db);
            _search = new SearchService(_clip, _db);
            _autoSync = new AutoSyncService(_importer, _db);

            _debounceTimer = DispatcherQueue.GetForCurrentThread().CreateTimer();
            _debounceTimer.Interval = TimeSpan.FromMilliseconds(DebounceDelayMs);
            _debounceTimer.IsRepeating = false;
            _debounceTimer.Tick += (s, e) => Search();

            ThumbnailCache.Initialize(_dispatcher);

            ManageFoldersCommand = new RelayCommand(() => _ = ManageFoldersWrapper());
        }

        public void Search()
        {
            var results = _search.Search(Query);

            // Clear collection on UI thread
            _dispatcher.TryEnqueue(() => Results.Clear());

            foreach (var r in results)
            {
                // Add items on UI thread
                _dispatcher.TryEnqueue(() =>
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
                _dispatcher.TryEnqueue(() =>
                {
                    CurrentFile = "Done";
                    IsImporting = false;
                    ProgressValue = 1.0;
                });
                EndSync();
            }
        }

        private async Task ManageFoldersWrapper()
        {
            await Task.Yield();   // ← forces continuation off UI-thread-pre-block
            await ManageFolders();
        }

        private async Task ManageFolders()
        {
            var existing = _db.GetFolders().ToList();

            var dialog = new Views.FolderManagerDialog(existing)
            {
                XamlRoot = App.Window?.Content.XamlRoot
            };

            var result = await dialog.ShowAsync();
            if (result != ContentDialogResult.Primary)
                return;

            var newList = dialog.Folders.ToList();

            // Save new folder list immediately
            _db.SetFolders(newList);

            // -------------------------------
            // Show UI status
            // -------------------------------
            _dispatcher.TryEnqueue(() =>
            {
                IsImporting = true;
                CurrentFile = "Removing deleted files...";
                ProgressValue = 0;
            });

            // -------------------------------
            // Move ALL heavy lifting off UI thread
            // -------------------------------
            await Task.Run(() =>
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
                        e.FilePath.StartsWith(root + Path.DirectorySeparatorChar,
                                              StringComparison.OrdinalIgnoreCase));

                    if (!inside)
                    {
                        _dispatcher.TryEnqueue(() => CurrentFile = e.FilePath);
                        _db.RemoveEmbeddingWithinTransaction(con, e.FilePath);
                        ThumbnailCache.Delete(e.FilePath);
                    }

                    index++;

                    // update progress every 100 items (UI throttled)
                    if (index % 100 == 0)
                    {
                        double p = (double)index / total;
                        _dispatcher.TryEnqueue(() => ProgressValue = p);
                    }
                }

                tr.Commit();
            });

            // -------------------------------
            // Start background sync (exclusive)
            // -------------------------------
            if (!TryBeginSync())
            {
                _dispatcher.TryEnqueue(() => IsImporting = false);
                return;
            }

            _dispatcher.TryEnqueue(() =>
            {
                CurrentFile = "Syncing folders...";
                ProgressValue = 0;
            });

            // Use the existing helper
            _ = Task.Run(async () => await RunExclusiveAutoSyncFromVM());
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

                _dispatcher.TryEnqueue(() =>
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
                file => _dispatcher.TryEnqueue(() => CurrentFile = file),
                p => _dispatcher.TryEnqueue(() => ProgressValue = p)
            );
        }
    }
}
