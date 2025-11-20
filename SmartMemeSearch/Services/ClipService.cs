
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

        public ClipService()
        {
            string baseDir = AppContext.BaseDirectory;

            string imageModelPath = Path.Combine(baseDir, "Assets", "clip_image.onnx");
            string textModelPath = Path.Combine(baseDir, "Assets", "clip_text.onnx");
            string tokenizerPath = Path.Combine(baseDir, "Assets", "tokenizer.json");


            _img = new InferenceSession(imageModelPath);
            _txt = new InferenceSession(textModelPath);

            _tokenizer = new JsonClipTokenizer(tokenizerPath);

            Task.Run(async () =>
            {
                /*var ids = _tokenizer.EncodeToIds("cat");
                System.Diagnostics.Debug.WriteLine("Tokens for 'cat':");
                System.Diagnostics.Debug.WriteLine(string.Join(", ", ids.Take(10)));*/

                await TestCatImageSimilarityAsync();

                /* foreach (var kvp in _img.InputMetadata)
                 {
                     System.Diagnostics.Debug.WriteLine(
                         $"IMG Input: {kvp.Key}, Type={kvp.Value.ElementType}, Shape={string.Join(",", kvp.Value.Dimensions)}");
                 }
                 foreach (var kvp in _img.OutputMetadata)
                 {
                     System.Diagnostics.Debug.WriteLine(
                         $"IMG Output: {kvp.Key}, Type={kvp.Value.ElementType}, Shape={string.Join(",", kvp.Value.Dimensions)}");
                 }*/

                /*foreach (var meta in _img.ModelMetadata.CustomMetadataMap)
                {
                    System.Diagnostics.Debug.WriteLine($"{meta.Key}: {meta.Value}");
                }*/
                /* foreach (var inp in _img.InputMetadata)
                 {
                     var meta = inp.Value;
                     System.Diagnostics.Debug.WriteLine(
                         $"Input '{inp.Key}': Type={meta.ElementType}, Dims={string.Join(",", meta.Dimensions)}");
                 }*/
                Debug.WriteLine("TEXT INPUTS:");
                foreach (var m in _txt.InputMetadata)
                    Debug.WriteLine($" - {m.Key}");

                Debug.WriteLine("IMAGE INPUTS:");
                foreach (var m in _img.InputMetadata)
                    Debug.WriteLine($" - {m.Key}");


            });
        }

        /// <summary>
        public async Task TestCatImageSimilarityAsync()
        {
            try
            {
                string baseDir = AppContext.BaseDirectory;
                string imagePath = Path.Combine(baseDir, "Assets", "cat.jpg");

                if (!File.Exists(imagePath))
                {
                    System.Diagnostics.Debug.WriteLine("ERROR: cat.jpg not found at " + imagePath);
                    return;
                }

                // Load the image bytes
                byte[] imageBytes = await File.ReadAllBytesAsync(imagePath);

                System.Diagnostics.Debug.WriteLine("Loaded image: " + imagePath);

                // 1. Compute image embedding
                float[] imgVec = GetImageEmbedding(imageBytes);
                System.Diagnostics.Debug.WriteLine("Image embedding length: " + imgVec.Length);

                // 2. Compute text embeddings
                float[] catVec = GetTextEmbedding("a photo of a cat");
                float[] dogVec = GetTextEmbedding("a photo of a dog");
                float[] politicsVec = GetTextEmbedding("a photo of politics");

                System.Diagnostics.Debug.WriteLine("Text embedding length: " + catVec.Length);

                // 3. Compute cosine similarities
                double catScore = Services.MathService.Cosine(imgVec, catVec);
                double dogScore = Services.MathService.Cosine(imgVec, dogVec);
                double polScore = Services.MathService.Cosine(imgVec, politicsVec);

                // 4. Show results
                System.Diagnostics.Debug.WriteLine("=== CLIP Similarity Test ===");
                System.Diagnostics.Debug.WriteLine("Image: cat.jpg");
                System.Diagnostics.Debug.WriteLine($"Similarity to \"a photo of a cat\":      {catScore:F4}");
                System.Diagnostics.Debug.WriteLine($"Similarity to \"a photo of a dog\":      {dogScore:F4}");
                System.Diagnostics.Debug.WriteLine($"Similarity to \"p photo of politics\": {polScore:F4}");
                System.Diagnostics.Debug.WriteLine("=============================");
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("TEST ERROR: " + ex.ToString());
            }
        }
        /// </summary>
        /// <param name="text"></param>
        /// <returns></returns>

        // -----------------------------------------------------
        // TEXT → EMBEDDING
        // -----------------------------------------------------
        public float[] GetTextEmbedding(string text)
        {
            // Use the tokenizer to get 77 token ids (Int64)
            long[] ids = _tokenizer.EncodeToIds(text);

            var inputIds = new DenseTensor<long>(ids, new[] { 1, ids.Length });

            var inputs = new List<NamedOnnxValue>
            {
                NamedOnnxValue.CreateFromTensor("input_ids", inputIds)
            };

            using var results = _txt.Run(inputs);
            return results.First().AsEnumerable<float>().ToArray();
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
                BitmapPixelFormat.Rgba8,
                BitmapAlphaMode.Ignore,
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
                    byte r = pixels[pixelIndex + 0];
                    byte g = pixels[pixelIndex + 1];
                    byte b = pixels[pixelIndex + 2];

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
