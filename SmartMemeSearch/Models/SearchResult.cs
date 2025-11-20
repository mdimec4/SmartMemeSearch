namespace SmartMemeSearch
{
    public class SearchResult
    {
        public string FilePath { get; set; } = string.Empty;
        public double Score { get; set; }

        // Optional: for future UI display
        public string OcrPreview { get; set; } = string.Empty;

        // Thumbnail image (loaded asynchronously)
        public Microsoft.UI.Xaml.Media.Imaging.BitmapImage? Thumbnail { get; set; }
    }
}