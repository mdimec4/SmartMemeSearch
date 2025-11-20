using System.Collections.ObjectModel;
using System.Windows.Input;
using Microsoft.UI.Xaml;


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


        private readonly Services.ClipService _clip;
        private readonly Services.DatabaseService _db;


        public MainViewModel()
        {
            _clip = new Services.ClipService();
            _db = new Services.DatabaseService();
            SearchCommand = new RelayCommand(Search);
        }


        private void Search()
        {
            float[] textEmbedding = _clip.GetTextEmbedding(Query);
            var items = _db.GetAllEmbeddings();


            Results.Clear();
            foreach (var item in items)
            {
                double score = Services.MathService.Cosine(textEmbedding, item.Vector);
                Results.Add(new SearchResult { FilePath = item.FilePath, Score = score });
            }
        }
    }
}