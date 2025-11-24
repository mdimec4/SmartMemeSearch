using System;
using System.Collections.Concurrent;
using System.IO;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Media.Imaging;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace SmartMemeSearch.Services
{
    public static class ThumbnailCache
    {
        private static readonly ConcurrentDictionary<string, BitmapImage> _memoryCache
            = new ConcurrentDictionary<string, BitmapImage>();

        private static string _thumbDir = "";
        private static DispatcherQueue? _dispatcher;

        // Global lock to ensure WinRT imaging (decoder/encoder/BitmapImage) runs one at a time
        private static readonly SemaphoreSlim _thumbLock = new SemaphoreSlim(1, 1);

        private const int THUMB_SIZE = 200;

        public static void Initialize(DispatcherQueue dispatcher)
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

        // Thumbnail filename based on SHA256(path)
        private static string GetThumbPath(string imagePath)
        {
            using var sha = SHA256.Create();
            byte[] hash = sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(imagePath));
            string hex = BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            return Path.Combine(_thumbDir, hex + ".jpg");
        }

        /// <summary>
        /// Remove thumbnail for given original path (memory + disk).
        /// </summary>
        public static void Delete(string originalPath)
        {
            EnsureInitialized();

            _memoryCache.TryRemove(originalPath, out _);

            string thumbPath = GetThumbPath(originalPath);
            if (File.Exists(thumbPath))
            {
                try { File.Delete(thumbPath); } catch { /* ignore */ }
            }
        }

        /// <summary>
        /// Pre-generate thumbnail (called by importer).
        /// This always regenerates the on-disk thumbnail.
        /// </summary>
        public static async Task PreGenerateAsync(string originalPath)
        {
            EnsureInitialized();

            string thumbPath = GetThumbPath(originalPath);
            var bmp = await CreateAndSaveThumbnailAsync(originalPath, thumbPath);
            _memoryCache[originalPath] = bmp;
        }

        /// <summary>
        /// Load a thumbnail for an image, generating it if needed.
        /// Uses memory cache → disk cache → generate.
        /// </summary>
        public static async Task<BitmapImage> LoadAsync(string originalPath)
        {
            EnsureInitialized();

            if (_memoryCache.TryGetValue(originalPath, out var mem))
                return mem;

            string thumbPath = GetThumbPath(originalPath);

            if (File.Exists(thumbPath))
            {
                var bmpDisk = await LoadFromDiskAsync(thumbPath);
                _memoryCache[originalPath] = bmpDisk;
                return bmpDisk;
            }

            var bmp = await CreateAndSaveThumbnailAsync(originalPath, thumbPath);
            _memoryCache[originalPath] = bmp;
            return bmp;
        }

        // ----------------------------------------------------
        // Internal helpers (all imaging serialized via _thumbLock)
        // ----------------------------------------------------
        private static async Task<BitmapImage> LoadFromDiskAsync(string thumbPath)
        {
            EnsureInitialized();

            await _thumbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                BitmapImage? bmp = null;

                using var file = File.OpenRead(thumbPath);
                var stream = file.AsRandomAccessStream();

                // Create BitmapImage on UI thread
                await _dispatcher!.EnqueueAsync(() =>
                {
                    bmp = new BitmapImage();
                    return Task.CompletedTask;
                }).ConfigureAwait(false);

                // Set source on UI thread as well
                await _dispatcher!.EnqueueAsync(async () =>
                {
                    stream.Seek(0);
                    await bmp!.SetSourceAsync(stream);
                }).ConfigureAwait(false);

                return bmp!;
            }
            finally
            {
                _thumbLock.Release();
            }
        }

        private static async Task<BitmapImage> CreateAndSaveThumbnailAsync(string originalPath, string thumbPath)
        {
            EnsureInitialized();

            await _thumbLock.WaitAsync().ConfigureAwait(false);
            try
            {
                using var file = File.OpenRead(originalPath);
                var ras = file.AsRandomAccessStream();
                var decoder = await _dispatcher.EnqueueAsync(() =>
                    BitmapDecoder.CreateAsync(ras).AsTask()
                );


                uint width = decoder.PixelWidth;
                uint height = decoder.PixelHeight;

                double scale = (double)THUMB_SIZE / Math.Max(width, height);
                uint newW = (uint)(width * scale);
                uint newH = (uint)(height * scale);

                var transform = new BitmapTransform
                {
                    ScaledWidth = newW,
                    ScaledHeight = newH,
                    InterpolationMode = BitmapInterpolationMode.Fant
                };

                var data = await decoder.GetPixelDataAsync(
                    BitmapPixelFormat.Rgba8,
                    BitmapAlphaMode.Ignore,
                    transform,
                    ExifOrientationMode.IgnoreExifOrientation,
                    ColorManagementMode.DoNotColorManage);

                byte[] thumbPixels = data.DetachPixelData();

                // Encode JPEG thumbnail
                using var outFile = File.Open(thumbPath, FileMode.Create);
                var outStream = outFile.AsRandomAccessStream();

                var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, outStream);
                encoder.SetPixelData(
                    BitmapPixelFormat.Rgba8,
                    BitmapAlphaMode.Ignore,
                    newW,
                    newH,
                    96,
                    96,
                    thumbPixels);

                await encoder.FlushAsync();

                BitmapImage? bmp = null;

                // Create BitmapImage on UI thread
                await _dispatcher!.EnqueueAsync(() =>
                {
                    bmp = new BitmapImage();
                    return Task.CompletedTask;
                }).ConfigureAwait(false);

                // Set source on UI thread
                await _dispatcher!.EnqueueAsync(async () =>
                {
                    outStream.Seek(0);
                    await bmp!.SetSourceAsync(outStream);
                }).ConfigureAwait(false);

                return bmp!;
            }
            finally
            {
                _thumbLock.Release();
            }
        }

        public static BitmapImage? TryGetMemory(string filePath)
        {
            // No lock needed – ConcurrentDictionary is thread-safe for reads
            return _memoryCache.TryGetValue(filePath, out var bmp)
                ? bmp
                : null;
        }

    }

    // Small helper to await DispatcherQueue callbacks
    public static class DispatcherQueueExtensions
    {
        public static Task EnqueueAsync(this DispatcherQueue d, Func<Task> action)
        {
            var tcs = new TaskCompletionSource<object?>();

            d.TryEnqueue(async () =>
            {
                try
                {
                    await action();
                    tcs.SetResult(null);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return tcs.Task;
        }


        public static Task<T> EnqueueAsync<T>(this DispatcherQueue? d, Func<Task<T>> func)
        {
            var tcs = new TaskCompletionSource<T>();

            d?.TryEnqueue(async () =>
            {
                try
                {
                    T result = await func();
                    tcs.SetResult(result);
                }
                catch (Exception ex)
                {
                    tcs.SetException(ex);
                }
            });

            return tcs.Task;
        }
    }
}
