using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using SmartMemeSearch.Services;
using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SmartMemeSearch.ViewModels
{
    public class MainViewModel : BindableBase, INotifyPropertyChanged
    {
        private readonly DispatcherQueue _dispatcher = DispatcherQueue.GetForCurrentThread();

        new public event PropertyChangedEventHandler? PropertyChanged;

        private string _query = string.Empty;
        public string Query
        {
            get => _query;
            set => SetProperty(ref _query, value);
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

        public ICommand SearchCommand { get; }
        public ICommand ImportCommand { get; }

        private readonly ClipService _clip;
        private readonly OcrService _ocr;
        private readonly DatabaseService _db;
        private readonly ImporterService _importer;
        private readonly SearchService _search;
        private readonly AutoSyncService _autoSync;


        public MainViewModel()
        {
            _clip = new ClipService();
            _ocr = new OcrService();
            _db = new DatabaseService();
            _importer = new ImporterService(_clip, _ocr, _db);
            _search = new SearchService(_clip, _db);
            _autoSync = new AutoSyncService(_importer, _db);

            ThumbnailCache.Initialize(_dispatcher);

            SearchCommand = new RelayCommand(Search);
            ImportCommand = new RelayCommand(async () => await ImportFolder());



            Task.Run(async () =>
            {
                IsImporting = true;
                CurrentFile = "Checking folders...";
                ProgressValue = 0;

                await _autoSync.SyncAllAsync(
                    file => _dispatcher.TryEnqueue(() => CurrentFile = file),
                    p => _dispatcher.TryEnqueue(() => ProgressValue = p)
                );

                IsImporting = false;

                // After syncing, show results for last query or blank search
                if (!string.IsNullOrWhiteSpace(Query))
                    Search();
            });
        }

        private void Search()
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

                // Load thumbnail asynchronously (NOT on UI thread)
                _ = LoadThumbnailAsync(r);
            }
        }

        private async Task ImportFolder()
        {
            if (IsImporting)
                return;

            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");

            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null) return;

            IsImporting = true;
            CurrentFile = "";
            ProgressValue = 0;

            await Task.Run(async () =>
            {
                _db.AddFolder(folder.Path);

                await _importer.ImportFolderAsync(
                    folder.Path,
                    file =>
                    {
                        _dispatcher.TryEnqueue(() => CurrentFile = file);
                    },
                    p =>
                    {
                        _dispatcher.TryEnqueue(() => ProgressValue = p);
                    }
                );
            });

            IsImporting = false;
            Search();
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


        protected void OnPropertyChanged([System.Runtime.CompilerServices.CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
