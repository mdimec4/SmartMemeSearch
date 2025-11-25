using System.ComponentModel;
using System.Windows.Media.Imaging;

namespace SmartMemeSearch.Wpf
{
    public class SearchResult : BindableBase
    {
        public string FilePath { get; set; } = string.Empty;
        public double Score { get; set; }

        // Optional: for future UI display
        public string OcrPreview { get; set; } = string.Empty;

        // Thumbnail image (loaded asynchronously)
        private BitmapImage? _thumbnail;
        public BitmapImage? Thumbnail { get => _thumbnail; set=> SetProperty(ref _thumbnail, value); }
    }
}