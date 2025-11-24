using System;
using System.IO;
using Tesseract;

namespace SmartMemeSearch.Wpf.Services
{
    public class OcrService
    {

        public async Task<string> ExtractTextAsync(byte[] imageBytes)
        {
            try
            {
                var engine = new TesseractEngine("./tessdata", "eng", EngineMode.Default);
                using var img = Pix.LoadFromMemory(imageBytes);
                using var page = engine.Process(img);
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
