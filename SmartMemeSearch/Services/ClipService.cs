// --------------------------------------------------------
// Services/ClipService.cs — Improved for Xenova CLIP ONNX
// --------------------------------------------------------
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices.WindowsRuntime;
using System.Threading;
using System.Threading.Tasks;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace SmartMemeSearch.Services
{
    public class ClipService
    {
        private readonly InferenceSession _img;
        private readonly InferenceSession _txt;
        private readonly JsonClipTokenizer _tokenizer;

        private static readonly SemaphoreSlim _clipImageLock = new SemaphoreSlim(1, 1);

        // Strong prompt templates (OpenAI used 80, but these already help a LOT)
        private static readonly string[] Templates = new[]
        {
            "a photo of a {}",
            "a close-up photo of a {}",
            "a cropped photo of a {}",
            "a jpeg photo of a {}",
            "a good photo of a {}",
            "a low-resolution photo of a {}"
        };

        public ClipService()
        {
            string baseDir = AppContext.BaseDirectory;

            string imageModelPath = Path.Combine(baseDir, "Assets", "clip_image.onnx");
            string textModelPath = Path.Combine(baseDir, "Assets", "clip_text.onnx");
            string tokenizerPath = Path.Combine(baseDir, "Assets", "tokenizer.json");

            var opts = new SessionOptions();
            opts.AppendExecutionProvider_DML();   // <--- GPU
            opts.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_WARNING;

            _img = new InferenceSession(imageModelPath, opts);
            _txt = new InferenceSession(textModelPath, opts);
            _tokenizer = new JsonClipTokenizer(tokenizerPath);

            Debug.WriteLine("Loaded Xenova CLIP ONNX models");


            // OPTIONAL: run test after app starts
            //            Task.Run(TestCatImageSimilarityAsync);
        }

        // -----------------------------------------------------
        // Normalize embedding (L2)
        // -----------------------------------------------------
        private static float[] Normalize(float[] v)
        {
            double sum = 0;
            foreach (var x in v) sum += x * x;

            double norm = Math.Sqrt(sum);
            if (norm == 0) return v;

            float[] o = new float[v.Length];
            for (int i = 0; i < v.Length; i++)
                o[i] = (float)(v[i] / norm);

            return o;
        }

        // TODO IMPORTANT: Pobably want to re-eable this
        // -----------------------------------------------------
        // TEXT → EMBEDDING (via prompt templates + averaging)
        // -----------------------------------------------------
        public float[] GetTextEmbedding(string text)
        {
            List<float[]> embeddings = new();

            foreach (var t in Templates)
            {
                string prompt = t.Replace("{}", text);

                long[] ids = _tokenizer.EncodeToIds(prompt);
                var inputIds = new DenseTensor<long>(ids, new[] { 1, ids.Length });

                var inputs = new List<NamedOnnxValue>
                 {
                     NamedOnnxValue.CreateFromTensor("input_ids", inputIds)
                 };

                using var results = _txt.Run(inputs);
                float[] raw = results.First().AsEnumerable<float>().ToArray();
                embeddings.Add(Normalize(raw));
            }

            // Average all prompt embeddings
            float[] avg = new float[embeddings[0].Length];
            foreach (var e in embeddings)
                for (int i = 0; i < avg.Length; i++)
                    avg[i] += e[i];

            for (int i = 0; i < avg.Length; i++)
                avg[i] /= embeddings.Count;

            return Normalize(avg);
        }

        /*
        public float[] GetTextEmbedding(string text)
        {

                 long[] ids = _tokenizer.EncodeToIds(text);
                var inputIds = new DenseTensor<long>(ids, new[] { 1, ids.Length });

                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", inputIds)
                };

                using var results = _txt.Run(inputs);
                float[] raw = results.First().AsEnumerable<float>().ToArray();
                return Normalize(raw);
        }*/

        // -----------------------------------------------------
        // IMAGE → EMBEDDING
        // -----------------------------------------------------
        public float[] GetImageEmbedding(byte[] bytes)
        {
            // Defensive: never let multiple CLIP image runs overlap
            _clipImageLock.Wait();
            try
            {
                var tensor = PreprocessImage(bytes);   // uses WinRT imaging
                var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("pixel_values", tensor)
                };

                using var results = _img.Run(inputs);
                return results.First().AsEnumerable<float>().ToArray();
            }
            finally
            {
                _clipImageLock.Release();
            }
        }


        // -----------------------------------------------------
        // IMAGE PREPROCESSING — CLIP Standard
        // -----------------------------------------------------
        private Tensor<float> PreprocessImage(byte[] bytes)
        {
            _clipImageLock.Wait();
            try
            {
                return PreprocessImageAsync(bytes).GetAwaiter().GetResult();
            }
            finally
            {
                _clipImageLock.Release();
            }
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
                BitmapPixelFormat.Rgba8,
                BitmapAlphaMode.Ignore,
                transform,
                ExifOrientationMode.IgnoreExifOrientation,
                ColorManagementMode.DoNotColorManage);

            byte[] pixels = pixelData.DetachPixelData();

            float[] data = new float[1 * 3 * size * size];

            float[] mean = { 0.481454f, 0.457828f, 0.408210f };
            float[] std = { 0.268629f, 0.261303f, 0.275777f };

            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int idxImg = (y * size + x) * 4;

                    byte r = pixels[idxImg + 0];
                    byte g = pixels[idxImg + 1];
                    byte b = pixels[idxImg + 2];

                    int idx = y * size + x;

                    data[idx] = ((r / 255f) - mean[0]) / std[0];
                    data[idx + size * size] = ((g / 255f) - mean[1]) / std[1];
                    data[idx + 2 * size * size] = ((b / 255f) - mean[2]) / std[2];
                }
            }

            return new DenseTensor<float>(data, new[] { 1, 3, size, size });
        }

        // -----------------------------------------------------
        // Built-in test for debugging
        // -----------------------------------------------------
        public async Task TestCatImageSimilarityAsync()
        {
            try
            {
                string baseDir = AppContext.BaseDirectory;
                string imagePath = Path.Combine(baseDir, "Assets", "cat.jpg");

                if (!File.Exists(imagePath))
                {
                    Debug.WriteLine("ERROR: cat.jpg not found");
                    return;
                }

                byte[] imgBytes = await File.ReadAllBytesAsync(imagePath);

                float[] img = GetImageEmbedding(imgBytes);

                float[] cat = GetTextEmbedding("cat");
                float[] dog = GetTextEmbedding("dog");
                float[] pol = GetTextEmbedding("politics");

                double c = MathService.Cosine(img, cat);
                double d = MathService.Cosine(img, dog);
                double p = MathService.Cosine(img, pol);

                Debug.WriteLine("=== CLIP Similarity Test ===");
                Debug.WriteLine($"cat:      {c:F4}");
                Debug.WriteLine($"dog:      {d:F4}");
                Debug.WriteLine($"politics: {p:F4}");
                Debug.WriteLine("=============================");
            }
            catch (Exception ex)
            {
                Debug.WriteLine("TEST ERROR: " + ex);
            }
        }
    }
}
