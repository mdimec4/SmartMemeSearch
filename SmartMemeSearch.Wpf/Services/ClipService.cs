// --------------------------------------------------------
// Services/ClipService.cs — Improved for Xenova CLIP ONNX
// --------------------------------------------------------
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Diagnostics;
using System.IO;
using Image = SixLabors.ImageSharp.Image;
using Path = System.IO.Path;
using Size = SixLabors.ImageSharp.Size;

namespace SmartMemeSearch.Wpf.Services
{
    public class ClipService
    {
        private readonly InferenceSession _img;
        private readonly InferenceSession _txt;
        private readonly JsonClipTokenizer _tokenizer;

        private static readonly SemaphoreSlim _clipImageLock = new SemaphoreSlim(1, 1);
        private static readonly SemaphoreSlim _gpuLock = new SemaphoreSlim(1, 1);

        // Strong prompt templates (OpenAI used 80, but these already help a LOT)
        /*private static readonly string[] Templates = new[]
        {
            "a photo of a {}",
            "a close-up photo of a {}",
            "a cropped photo of a {}",
            "a jpeg photo of a {}",
            "a good photo of a {}",
            "a low-resolution photo of a {}"
        };*/

        public ClipService()
        {
            string baseDir = AppContext.BaseDirectory;

            string imageModelPath = Path.Combine(baseDir, "Assets", "clip_image.onnx");
            string textModelPath = Path.Combine(baseDir, "Assets", "clip_text.onnx");
            string tokenizerPath = Path.Combine(baseDir, "Assets", "tokenizer.json");
            
            var opts = new SessionOptions();
            opts.LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_WARNING;

            try
            {
                opts.AppendExecutionProvider_DML();
                Debug.WriteLine("Using DirectML GPU provider");
            }
            catch
            {
                Debug.WriteLine("DirectML not available, falling back to CPU");
            }

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

        // templeate avreaging performs bad, so we disabe it
        // -----------------------------------------------------
        // TEXT → EMBEDDING (via prompt templates + averaging)
        // -----------------------------------------------------
        /* public float[] GetTextEmbedding(string text)
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

                 _gpuLock.Wait();
                 try
                 {
                     using var results = _txt.Run(inputs);
                     float[] raw = results.First().AsEnumerable<float>().ToArray();
                     embeddings.Add(Normalize(raw));
                 }
                 finally
                 {
                     _gpuLock.Release();
                 }
             }

                 // Average all prompt embeddings
                 float[] avg = new float[embeddings[0].Length];
                 foreach (var e in embeddings)
                     for (int i = 0; i < avg.Length; i++)
                         avg[i] += e[i];

                 for (int i = 0; i < avg.Length; i++)
                     avg[i] /= embeddings.Count;

                 return Normalize(avg);
         }*/


        public float[] GetTextEmbedding(string text)
        {

            long[] ids = _tokenizer.EncodeToIds(text);
            var inputIds = new DenseTensor<long>(ids, new[] { 1, ids.Length });

            var inputs = new List<NamedOnnxValue>
                {
                    NamedOnnxValue.CreateFromTensor("input_ids", inputIds)
                };
            float[] raw;
            _gpuLock.Wait();
            try
            {
                using var results = _txt.Run(inputs);
                raw = results.First().AsEnumerable<float>().ToArray();
            }
            finally
            {
                _gpuLock.Release();
            }
            return Normalize(raw);
        }

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

                _gpuLock.Wait();
                try
                {
                    using var results = _img.Run(inputs);
                    return results.First().AsEnumerable<float>().ToArray();
                }
                finally
                {
                    _gpuLock.Release();
                }
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
            using Image<Rgba32> img = Image.Load<Rgba32>(bytes);

            const int target = 224;

            // Resize to CLIP resolution
            img.Mutate(x => x.Resize(new ResizeOptions
            {
                Size = new Size(target, target),
                Mode = ResizeMode.Crop // Or ResizeMode.Stretch, but CLIP prefers Crop
            }));

            float[] mean = { 0.481454f, 0.457828f, 0.408210f };
            float[] std = { 0.268629f, 0.261303f, 0.275777f };

            float[] data = new float[1 * 3 * target * target];

            img.ProcessPixelRows(accessor =>
            {
                for (int y = 0; y < target; y++)
                {
                    var row = accessor.GetRowSpan(y);
                    for (int x = 0; x < target; x++)
                    {
                        Rgba32 px = row[x];

                        int idx = y * target + x;

                        float r = px.R / 255f;
                        float g = px.G / 255f;
                        float b = px.B / 255f;

                        data[idx] = (r - mean[0]) / std[0];
                        data[idx + target * target] = (g - mean[1]) / std[1];
                        data[idx + 2 * target * target] = (b - mean[2]) / std[2];
                    }
                }
            });

            return new DenseTensor<float>(data, new[] { 1, 3, target, target });
        }




        // -----------------------------------------------------
        // Built-in test for debugging
        // -----------------------------------------------------
        public async Task TestCatImageSimilarityAsync()
        {
            try
            {
                string baseDir = AppContext.BaseDirectory;
                string imagePath = System.IO.Path.Combine(baseDir, "Assets", "cat.jpg");

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
