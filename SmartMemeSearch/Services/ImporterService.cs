using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using SmartMemeSearch.Models;

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

        public async Task ImportFolderAsync(string folder)
        {
            string[] images = Directory.GetFiles(folder, "*.*", SearchOption.AllDirectories);

            foreach (var file in images)
            {
                if (!IsImage(file)) continue;

                var info = new FileInfo(file);
                long lastMod = info.LastWriteTimeUtc.Ticks;

                byte[] bytes = await File.ReadAllBytesAsync(file);

                // CLIP embedding
                float[] embedding = _clip.GetImageEmbedding(bytes);

                // OCR text
                string ocrText = await _ocr.ExtractTextAsync(bytes);

                // Save
                _db.InsertOrUpdate(new MemeEmbedding
                {
                    FilePath = file,
                    Vector = embedding,
                    OcrText = ocrText,
                    LastModified = lastMod
                });
            }
        }

        private bool IsImage(string path)
        {
            string ext = Path.GetExtension(path).ToLower();
            return ext == ".jpg" || ext == ".jpeg" || ext == ".png" || ext == ".bmp" || ext == ".gif";
        }
    }
}
