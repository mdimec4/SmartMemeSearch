
// --------------------------------------------------------
// Services/ClipService.cs
// REAL CLIP ONNX IMPLEMENTATION SETUP (OpenAI ViT-B/32)
// --------------------------------------------------------
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace SmartMemeSearch.Services
{
    public class ClipService
    {
        private readonly InferenceSession _img;
        private readonly InferenceSession _txt;

        public ClipService()
        {
            string baseDir = AppContext.BaseDirectory;

            string imageModelPath = Path.Combine(baseDir, "Assets", "clip_image.onnx");
            string textModelPath = Path.Combine(baseDir, "Assets", "clip_text.onnx");

            _img = new InferenceSession(imageModelPath);
            _txt = new InferenceSession(textModelPath);

            foreach (var kvp in _txt.InputMetadata)
                System.Diagnostics.Debug.WriteLine("TXT MODEL INPUT: " + kvp.Key);

            foreach (var kvp in _img.InputMetadata)
                System.Diagnostics.Debug.WriteLine("IMG MODEL INPUT: " + kvp.Key);
        }

        // -----------------------------------------------------
        // TEXT → EMBEDDING
        // -----------------------------------------------------
        public float[] GetTextEmbedding(string text)
        {
            var ids  = Tokenize(text);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", ids),
            };

            using var results = _txt.Run(inputs);
            var output = results.First().AsEnumerable<float>().ToArray();
            return output;
        }

        // -----------------------------------------------------
        // IMAGE → EMBEDDING
        // -----------------------------------------------------
        public float[] GetImageEmbedding(byte[] bytes)
        {
            var tensor = PreprocessImage(bytes);

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("pixel_values", tensor)
            };

            using var results = _img.Run(inputs);
            return results.First().AsEnumerable<float>().ToArray();
        }

        // -----------------------------------------------------
        // CLIP TEXT TOKENIZER (very simplified BPE stub)
        // -----------------------------------------------------
        private Tensor<long> Tokenize(string text)
        {
            const int maxLen = 77;
            long[] ids = new long[maxLen];

            var words = Regex.Split(text.ToLower(), "\\W+")
                             .Where(w => w.Length > 0)
                             .ToArray();

            for (int i = 0; i < Math.Min(words.Length, maxLen); i++)
                ids[i] = Math.Abs(words[i].GetHashCode()) % 30000;

            return new DenseTensor<long>(ids, new[] { 1, maxLen });
        }


        // -----------------------------------------------------
        // IMAGE PREPROCESSING (CLIP standard):
        // 1. decode → bitmap
        // 2. resize to 224x224
        // 3. normalize per-channel
        // 4. convert to CHW float tensor
        // -----------------------------------------------------
        private Tensor<float> PreprocessImage(byte[] bytes)
        {
            // Bridge async WinRT imaging APIs to sync caller
            return PreprocessImageAsync(bytes).GetAwaiter().GetResult();
        }

        private async Task<Tensor<float>> PreprocessImageAsync(byte[] bytes)
        {
            const int size = 224;

            using var ras = new InMemoryRandomAccessStream();
            await ras.WriteAsync(bytes.AsBuffer());
            ras.Seek(0);

            var decoder = await BitmapDecoder.CreateAsync(ras);

            var transform = new BitmapTransform
            {
                ScaledWidth = size,
                ScaledHeight = size,
                InterpolationMode = BitmapInterpolationMode.Fant
            };

            var pixelData = await decoder.GetPixelDataAsync(
                BitmapPixelFormat.Bgra8,
                BitmapAlphaMode.Premultiplied,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            byte[] pixels = pixelData.DetachPixelData(); // BGRA8

            float[] data = new float[1 * 3 * size * size];

            float[] mean = { 0.481454f, 0.457828f, 0.408210f };
            float[] std = { 0.268629f, 0.261303f, 0.275777f };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int pixelIndex = (y * size + x) * 4;
                    byte b = pixels[pixelIndex + 0];
                    byte g = pixels[pixelIndex + 1];
                    byte r = pixels[pixelIndex + 2];

                    int idx = y * size + x;

                    // R channel
                    data[idx] = ((r / 255f) - mean[0]) / std[0];
                    // G channel
                    data[idx + size * size] = ((g / 255f) - mean[1]) / std[1];
                    // B channel
                    data[idx + 2 * size * size] = ((b / 255f) - mean[2]) / std[2];
                }
            }

            return new DenseTensor<float>(data, new[] { 1, 3, size, size });
        }
    }
}
