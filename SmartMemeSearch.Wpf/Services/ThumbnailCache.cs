using System;
using System.Collections.Concurrent;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace SmartMemeSearch.Wpf.Services
{
    public static class ThumbnailCache
    {
        private static readonly ConcurrentDictionary<string, BitmapImage> _memoryCache
            = new ConcurrentDictionary<string, BitmapImage>();

        private static string _thumbDir = "";
        private static Dispatcher? _dispatcher;

        // Serialize image IO + bitmap encoding/decoding
        private static readonly SemaphoreSlim _thumbLock = new SemaphoreSlim(1, 1);

        private const int THUMB_SIZE = 200;

        // ------------------------------------------------------------
        // Initialization
        // ------------------------------------------------------------
        public static void Initialize(Dispatcher dispatcher)
        {
            _dispatcher = dispatcher ?? throw new ArgumentNullException(nameof(dispatcher));

            string localApp = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            _thumbDir = Path.Combine(localApp, "SmartMemeSearch", "thumbs");

            Directory.CreateDirectory(_thumbDir);
        }

        private static void EnsureInitialized()
        {
            if (_dispatcher == null || string.IsNullOrEmpty(_thumbDir))
                throw new InvalidOperationException("ThumbnailCache.Initialize(dispatcher) must be called before use.");
        }

        // ------------------------------------------------------------
        // Hash the path → thumbnail filename
        // ------------------------------------------------------------
        private static string GetThumbPath(string imagePath)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(imagePath));
            string hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return Path.Combine(_thumbDir, hex + ".jpg");
        }

        // ------------------------------------------------------------
        // Public API
        // ------------------------------------------------------------
        public static void Delete(string originalPath)
        {
            EnsureInitialized();

            _memoryCache.TryRemove(originalPath, out _);

            string thumb = GetThumbPath(originalPath);
            if (File.Exists(thumb))
            {
                try { File.Delete(thumb); } catch { }
            }
        }

        public static async Task PreGenerateAsync(string originalPath)
        {
            EnsureInitialized();

            string thumb = GetThumbPath(originalPath);
            var bmp = await CreateAndSaveThumbnailAsync(originalPath, thumb);
            _memoryCache[originalPath] = bmp;
        }

        public static async Task<BitmapImage> LoadAsync(string originalPath)
        {
            EnsureInitialized();

            // memory cache
            if (_memoryCache.TryGetValue(originalPath, out var cached))
                return cached;

            string thumb = GetThumbPath(originalPath);

            // disk cache
            if (File.Exists(thumb))
            {
                var bmp = await LoadFromDiskAsync(thumb);
                _memoryCache[originalPath] = bmp;
                return bmp;
            }

            // generate
            var created = await CreateAndSaveThumbnailAsync(originalPath, thumb);
            _memoryCache[originalPath] = created;
            return created;
        }

        public static BitmapImage? TryGetMemory(string path)
        {
            return _memoryCache.TryGetValue(path, out var bmp) ? bmp : null;
        }

        // ------------------------------------------------------------
        // Load thumbnail from disk (UI thread for BitmapImage)
        // ------------------------------------------------------------
        private static async Task<BitmapImage> LoadFromDiskAsync(string thumbPath)
        {
            EnsureInitialized();

            await _thumbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                byte[] bytes = await File.ReadAllBytesAsync(thumbPath).ConfigureAwait(false);

                return await _dispatcher!.InvokeAsync(() =>
                {
                    using var ms = new MemoryStream(bytes);
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.StreamSource = ms;
                    img.EndInit();
                    img.Freeze();
                    return img;
                });
            }
            finally
            {
                _thumbLock.Release();
            }
        }

        // ------------------------------------------------------------
        // Create thumbnail (System.Drawing resize + JPG)
        // ------------------------------------------------------------
        private static async Task<BitmapImage> CreateAndSaveThumbnailAsync(string originalPath, string thumbPath)
        {
            EnsureInitialized();

            await _thumbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                // create thumbnail on background thread
                await Task.Run(() =>
                {
                    using var src = new Bitmap(originalPath);

                    int width = src.Width;
                    int height = src.Height;

                    double scale = (double)THUMB_SIZE / Math.Max(width, height);
                    int newW = Math.Max(1, (int)(width * scale));
                    int newH = Math.Max(1, (int)(height * scale));

                    using var thumb = new Bitmap(newW, newH);

                    using (var g = Graphics.FromImage(thumb))
                    {
                        g.CompositingQuality = CompositingQuality.HighQuality;
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.SmoothingMode = SmoothingMode.HighQuality;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;

                        g.DrawImage(src, 0, 0, newW, newH);
                    }

                    // ensure directory exists
                    Directory.CreateDirectory(Path.GetDirectoryName(thumbPath)!);

                    // save as JPEG
                    thumb.Save(thumbPath, ImageFormat.Jpeg);
                }).ConfigureAwait(false);

                // load thumbnail into BitmapImage (must be on UI thread)
                return await _dispatcher!.InvokeAsync(() =>
                {
                    var img = new BitmapImage();
                    img.BeginInit();
                    img.CacheOption = BitmapCacheOption.OnLoad;
                    img.UriSource = new Uri(thumbPath, UriKind.Absolute);
                    img.EndInit();
                    img.Freeze();
                    return img;
                });
            }
            finally
            {
                _thumbLock.Release();
            }
        }
    }
}
