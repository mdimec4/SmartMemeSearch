using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace SmartMemeSearch.Services
{
    public class AutoSyncService
    {
        private readonly ImporterService _importer;
        private readonly DatabaseService _db;

        public AutoSyncService(ImporterService importer, DatabaseService db)
        {
            _importer = importer;
            _db = db;
        }

        public async Task SyncAllAsync(
            Action<string> onFile,
            Action<double> onProgress)
        {
            var folders = _db.GetFolders();

            foreach (var folder in folders)
            {
                if (!Directory.Exists(folder))
                    continue;

                await SyncFolderAsync(folder, onFile, onProgress);
            }
        }

        private async Task SyncFolderAsync(
            string folder,
            Action<string> onFile,
            Action<double> onProgress)
        {
            var allFiles = Directory
                .GetFiles(folder, "*.*", SearchOption.AllDirectories);

            var images = new List<string>();
            foreach (var f in allFiles)
            {
                if (_importer.IsImage(f))
                    images.Add(f);
            }

            // DB currently known files
            var dbItems = new Dictionary<string, long>();
            foreach (var item in _db.GetAllEmbeddings())
                dbItems[item.FilePath] = item.LastModified;

            // Remove deleted files
            _db.RemoveMissingFiles(images);

            // Process new/changed files
            int total = images.Count;
            int ix = 0;

            foreach (var img in images)
            {
                ix++;
                onProgress((double)ix / total);
                onFile(img);

                long mod = File.GetLastWriteTimeUtc(img).Ticks;
                if (dbItems.TryGetValue(img, out long oldMod) && oldMod == mod)
                {
                    // unchanged → skip
                    continue;
                }

                await _importer.ImportSingleAsync(img);
                /*await Task.Delay(10); // throttle
                await ThumbnailCache.PreGenerateAsync(img);*/
            }

            // final progress
            onProgress(1.0);
        }
    }
}
