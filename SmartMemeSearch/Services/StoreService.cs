using System;
using System.Threading.Tasks;
using Windows.Services.Store;
using WinRT.Interop;

namespace SmartMemeSearch.Services
{
    public class StoreService
    {
        private StoreContext? _context;

        public static bool WindowReady { get; private set; }

        public static void NotifyWindowReady() => WindowReady = true;

        private void EnsureContext()
        {
            if (_context != null)
                return;

            if (!StoreService.WindowReady)
                throw new InvalidOperationException("Window not ready for Store API.");

            _context = StoreContext.GetDefault();


            // SAFETY CHECK: ensure window exists
            if (App.Window is null)
                throw new InvalidOperationException("Main window not initialized yet.");

            // get window handle
            var hwnd = WindowNative.GetWindowHandle(App.Window);

            // attach the StoreContext to window
            InitializeWithWindow.Initialize(_context, hwnd);
        }

        public async Task<bool> PurchaseRemoveAdsAsync()
        {
            EnsureContext();

            StorePurchaseResult result =
                await _context?.RequestPurchaseAsync("remove_ads");

            return result.Status == StorePurchaseStatus.Succeeded;
        }

        public async Task<bool> IsPremiumAsync()
        {
            EnsureContext();

            StoreAppLicense lic = await _context?.GetAppLicenseAsync();
            return lic.AddOnLicenses.TryGetValue("remove_ads", out var addon)
                   && addon.IsActive;
        }
    }
}
