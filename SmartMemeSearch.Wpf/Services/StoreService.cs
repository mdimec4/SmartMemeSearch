using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Interop;
using Windows.Services.Store;

namespace SmartMemeSearch.Wpf.Services
{
    public class StoreService
    {
        private StoreContext? _context;
        private readonly Window _window;

        public StoreService(Window mainWindow)
        {
            _window = mainWindow ?? throw new ArgumentNullException(nameof(mainWindow));
        }

        /// <summary>
        /// Returns true if the app is running with package identity (MSIX) — required for Store APIs.
        /// </summary>
        public static bool IsPackaged
        {
            get
            {
                try
                {
                    _ = Windows.ApplicationModel.Package.Current;
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private void EnsureContext()
        {
            if (_context != null)
                return;

            if (!IsPackaged)
                throw new InvalidOperationException("Store APIs can only be used when the app is MSIX-packaged.");

            _context = StoreContext.GetDefault();

            // Attach StoreContext to the WPF window
            var hwnd = new WindowInteropHelper(_window).Handle;
            if (hwnd == IntPtr.Zero)
                throw new InvalidOperationException("Failed to obtain WPF window handle.");

            InitializeWithWindow.Initialize(_context, hwnd);
        }

        public async Task<bool> PurchaseRemoveAdsAsync()
        {
            EnsureContext();

            StorePurchaseResult result =
                await _context!.RequestPurchaseAsync("remove_ads");

            return result.Status == StorePurchaseStatus.Succeeded;
        }

        public async Task<bool> IsPremiumAsync()
        {
            EnsureContext();

            StoreAppLicense lic = await _context!.GetAppLicenseAsync();

            return lic.AddOnLicenses.TryGetValue("remove_ads", out var addon)
                   && addon.IsActive;
        }
    }
}
