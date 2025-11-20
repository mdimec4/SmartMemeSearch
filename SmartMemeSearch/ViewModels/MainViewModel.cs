using Microsoft.UI.Xaml;
using SmartMemeSearch.Models;
using SmartMemeSearch.Services;
using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace SmartMemeSearch.ViewModels
{
    public class MainViewModel : BindableBase
    {
        private string _query = string.Empty;
        public string Query
        {
            get => _query;
            set => SetProperty(ref _query, value);
        }

        public ObservableCollection<SearchResult> Results { get; } = new();

        public ICommand SearchCommand { get; }
        public ICommand ImportCommand { get; }

        private readonly ClipService _clip;
        private readonly OcrService _ocr;
        private readonly DatabaseService _db;
        private readonly ImporterService _importer;
        private readonly SearchService _search;

        public MainViewModel()
        {
            _clip = new ClipService();
            _ocr = new OcrService();
            _db = new DatabaseService();
            _importer = new ImporterService(_clip, _ocr, _db);
            _search = new SearchService(_clip, _db);

            SearchCommand = new RelayCommand(Search);
            ImportCommand = new RelayCommand(async () => await ImportFolder());
        }

        private void Search()
        {
            Results.Clear();

            var results = _search.Search(Query);

            foreach (var r in results)
                Results.Add(r);
        }

        private async Task ImportFolder()
        {
            // Pick folder dialog
            var picker = new Windows.Storage.Pickers.FolderPicker();
            picker.FileTypeFilter.Add("*");

            // WinUI 3 requirement:
            var hwnd = WinRT.Interop.WindowNative.GetWindowHandle(App.Window);
            WinRT.Interop.InitializeWithWindow.Initialize(picker, hwnd);

            var folder = await picker.PickSingleFolderAsync();
            if (folder == null)
                return;

            await _importer.ImportFolderAsync(folder.Path);

            Search(); // update results after importing
        }
    }
}
