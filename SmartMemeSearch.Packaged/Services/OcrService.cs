using Microsoft.UI.Dispatching;
using System;
using System.IO;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage.Streams;

namespace SmartMemeSearch.Services
{
    public class OcrService
    {
        private readonly OcrEngine _engine;
        private readonly DispatcherQueue _dispatcher;

        public OcrService(DispatcherQueue dispatcher)
        {
            // Use system language or default to English
            _engine = OcrEngine.TryCreateFromLanguage(
                new Windows.Globalization.Language("en-US"));
            _dispatcher = dispatcher;
        }

        public async Task<string> ExtractTextAsync(byte[] imageBytes)
        {
            try
            {
                using var ras = new InMemoryRandomAccessStream();
                await ras.WriteAsync(imageBytes.AsBuffer());
                ras.Seek(0);

                var decoder = await _dispatcher.EnqueueAsync(() =>
                    BitmapDecoder.CreateAsync(ras).AsTask()
                );

                var pixelData = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Rgba8,
                    BitmapAlphaMode.Ignore,
                    new BitmapTransform(),
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                byte[] pixels = pixelData.DetachPixelData();

                var softwareBitmap = SoftwareBitmap.CreateCopyFromBuffer(
                    pixels.AsBuffer(),
                    BitmapPixelFormat.Rgba8,
                    (int)decoder.PixelWidth,
                    (int)decoder.PixelHeight);

                var result = await _engine.RecognizeAsync(softwareBitmap);
                return result.Text ?? "";
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("OCR ERROR: " + ex);
                return "";
            }
        }
    }
}
