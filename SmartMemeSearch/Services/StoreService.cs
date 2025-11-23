using System;
using System.Diagnostics;
using System.Threading.Tasks;
using Windows.Services.Store;

namespace SmartMemeSearch.Services
{
    public class StoreService
    {
        private readonly StoreContext _context;

        // TODO: replace this with your real Store ID for the add-on
        // Example: "9NBLGGH12345"
        private const string RemoveAdsStoreId = "9MVJSQGWRV2X";

        public StoreService()
        {
            _context = StoreContext.GetDefault();
        }

        /// <summary>
        /// Check if the user already owns the Remove Ads add-on.
        /// This is called at startup (auto-restore).
        /// </summary>
        public async Task<bool> IsPremiumAsync()
        {
            try
            {
                StoreAppLicense license = await _context.GetAppLicenseAsync();

                if (license == null)
                    return false;

                if (license.AddOnLicenses != null &&
                    license.AddOnLicenses.TryGetValue(RemoveAdsStoreId, out StoreLicense addOn))
                {
                    return addOn.IsActive;
                }

                return false;
            }
            catch (Exception ex)
            {
                Debug.WriteLine("IsPremiumAsync error: " + ex);
                return false;
            }
        }

        /// <summary>
        /// Try to purchase the Remove Ads add-on.
        /// Returns true if purchase succeeded or was already purchased.
        /// </summary>
        public async Task<bool> PurchaseRemoveAdsAsync()
        {
            try
            {
                StorePurchaseResult result = await _context.RequestPurchaseAsync(RemoveAdsStoreId);

                Debug.WriteLine("Purchase status: " + result.Status);

                switch (result.Status)
                {
                    case StorePurchaseStatus.Succeeded:
                    case StorePurchaseStatus.AlreadyPurchased:
                        return true;

                    case StorePurchaseStatus.NotPurchased:
                    case StorePurchaseStatus.NetworkError:
                    case StorePurchaseStatus.ServerError:
                    default:
                        return false;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("PurchaseRemoveAdsAsync error: " + ex);
                return false;
            }
        }
    }
}
