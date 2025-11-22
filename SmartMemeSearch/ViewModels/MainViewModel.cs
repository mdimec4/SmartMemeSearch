using Microsoft.UI.Dispatching;
using SmartMemeSearch.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;
using Windows.Storage.Pickers;


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

        public ICommand ImportCommand { get; }

        private readonly ClipService _clip;
        private readonly OcrService _ocr;
        private readonly DatabaseService _db;
        private readonly ImporterService _importer;
        private readonly SearchService _search;
        private readonly AutoSyncService _autoSync;

        private bool _isImportingFolder;


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

            ImportCommand = new RelayCommand(async () => await ImportFolder());
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
