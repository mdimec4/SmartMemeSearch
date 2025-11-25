using System;
using System.IO;
using Tesseract;

namespace SmartMemeSearch.Wpf.Services
{
    public class OcrService
    {
        private readonly TesseractEngine _engine;
        public OcrService()
        {
            string baseDir = AppContext.BaseDirectory;
            string dataPath = Path.Combine(baseDir, "Assets", "tessdata");
            _engine = new TesseractEngine(dataPath, "lat", EngineMode.Default);
        }

        public async Task<string> ExtractTextAsync(byte[] imageBytes)
        {
            try
            {
                using var img = Pix.LoadFromMemory(imageBytes);
                using var page = _engine.Process(img);
                return page.GetText();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("OCR ERROR: " + ex);
                return "";
            }
        }
    }
}
