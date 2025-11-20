using SmartMemeSearch.Models;
using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace SmartMemeSearch.Services
{
    public class ImporterService
    {
        private readonly ClipService _clip;
        private readonly OcrService _ocr;
        private readonly DatabaseService _db;

        public ImporterService(ClipService clip, OcrService ocr, DatabaseService db)
        {
            _clip = clip;
            _ocr = ocr;
            _db = db;
        }

        public async Task ImportFolderAsync(
            string folder,
            Action<string> onFile,
            Action<double> onProgress)
        {
            var dbFiles = _db.GetAllEmbeddings().ToDictionary(e => e.FilePath, e => e.LastModified);

            string[] images = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories);
            var imgList = images.Where(IsImage).ToArray();
            int total = imgList.Length;
            int index = 0;

            foreach (var file in imgList)
            {
                onFile?.Invoke(file);
                onProgress?.Invoke((double)index / total);

                await ImportSingleAsync(file);

                index++;
            }

            onProgress?.Invoke(1.0);
        }

        public async Task ImportSingleAsync(string file)
        {
            if (!IsImage(file))
                return;

            byte[] bytes = await File.ReadAllBytesAsync(file);

            float[] embedding;

            try
            {
                embedding = _clip.GetImageEmbedding(bytes);
            }
            catch (Exception ex)
            {
                // Log and skip this file
                System.Diagnostics.Debug.WriteLine($"CLIP image embedding failed for {file}: {ex}");
                return; // do not insert into DB
            }

            string ocrText;
            try
            {
                ocrText = await _ocr.ExtractTextAsync(bytes);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OCR failed for {file}: {ex}");
                ocrText = "";
            }


            _db.InsertOrUpdate(new MemeEmbedding
            {
                FilePath = file,
                Vector = embedding,
                OcrText = ocrText,
                LastModified = File.GetLastWriteTimeUtc(file).Ticks
            });

            // Pre-generate thumbnail so search feels instant
            await ThumbnailCache.PreGenerateAsync(file);
        }



        public bool IsImage(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            return ext == ".jpg" || ext == ".jpeg" ||
                   ext == ".png" || ext == ".bmp" ||
                   ext == ".gif" || ext == ".webp" ||
                   ext == ".tif" || ext == ".tiff" ||
                   ext == ".heic";
        }
    }
}
