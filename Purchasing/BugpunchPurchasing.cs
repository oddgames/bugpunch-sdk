// BugpunchPurchasing.cs — Unity IAP integration (v4 and v5).
//
// Optional: only compiles when the game has `com.unity.purchasing` >= 4.0.0.
// The `BUGPUNCH_HAS_UNITY_IAP` umbrella define is set by a version-define on
// the asmdef; `BUGPUNCH_HAS_UNITY_IAP_V4` / `_V5` split the implementations.
//
// v5 integration (IPurchaseService + IProductService):
//
//     using ODDGames.BugpunchSdk;
//     m_PurchaseService = UnityIAPServices.DefaultPurchase().WithBugpunch();
//     m_ProductService  = UnityIAPServices.DefaultProduct().WithBugpunch();
//
// v4 integration (IStoreController + IStoreListener):
//
//     using ODDGames.BugpunchSdk;
//     public void OnInitialized(IStoreController c, IExtensionProvider e) {
//         c.WithBugpunch();        // declares catalog SKUs for coverage
//         m_Controller = c;
//     }
//     public void OnPurchaseFailed(Product p, PurchaseFailureReason r) {
//         BugpunchPurchasing.LogPurchaseFailed(p, r);
//     }
//     public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs args) {
//         BugpunchPurchasing.LogPurchase(args);
//         return PurchaseProcessingResult.Complete;
//     }
//     // and at InitiatePurchase call sites:
//     BugpunchPurchasing.BeginPurchase(sku);
//     m_Controller.InitiatePurchase(sku);

#if BUGPUNCH_HAS_UNITY_IAP

using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Purchasing;

namespace ODDGames.BugpunchSdk
{
    /// <summary>
    /// Extension-method surface for hooking Bugpunch purchase logging into
    /// Unity IAP. v4 attaches to <see cref="IStoreController"/>; v5 attaches
    /// to <c>IPurchaseService</c>.
    /// </summary>
    public static class BugpunchPurchasing
    {
        /// <summary>
        /// Pre-declare an IAP catalog so every SKU appears on the Coverage
        /// page as an untested unit before any tester buys it.
        /// </summary>
        public static void DeclareIapCatalog(params string[] skus)
        {
            if (skus == null) return;
            foreach (var sku in skus)
                if (!string.IsNullOrEmpty(sku)) Bugpunch.DeclareIapSku(sku);
        }

        static void TryHook(Action act, string what)
        {
            try { act(); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch] WithBugpunch hook '{what}' skipped: {ex.Message}");
            }
        }

#if BUGPUNCH_HAS_UNITY_IAP_V5
        // ─── Unity IAP v5 (IPurchaseService) ────────────────────────────────

        /// <summary>
        /// One-line auto-wire for Unity IAP v5. Hooks every observable
        /// event so the game side never has to call Bugpunch APIs
        /// explicitly. Idempotent — re-subscription detaches first.
        /// </summary>
        public static IPurchaseService WithBugpunch(this IPurchaseService service)
        {
            if (service == null) return null;

            service.OnPurchaseConfirmed -= OnPurchaseConfirmed_V5;
            service.OnPurchaseConfirmed += OnPurchaseConfirmed_V5;

            TryHook(() => {
                service.OnPurchasePending -= OnPurchasePending_V5;
                service.OnPurchasePending += OnPurchasePending_V5;
            }, "OnPurchasePending");
            TryHook(() => {
                service.OnPurchaseFailed -= OnPurchaseFailed_V5;
                service.OnPurchaseFailed += OnPurchaseFailed_V5;
            }, "OnPurchaseFailed");

            return service;
        }

        /// <summary>
        /// One-line auto-wire for Unity IAP v5 <c>IProductService</c>. Subscribes to
        /// <c>OnProductsFetched</c> so every catalog SKU appears on the Coverage
        /// page as an untested unit. Idempotent — re-subscription detaches first.
        /// </summary>
        public static IProductService WithBugpunch(this IProductService service)
        {
            if (service == null) return null;

            TryHook(() => {
                service.OnProductsFetched -= OnProductsFetched_V5;
                service.OnProductsFetched += OnProductsFetched_V5;
            }, "OnProductsFetched");

            return service;
        }

        static void OnProductsFetched_V5(List<Product> products)
        {
            try
            {
                if (products == null) return;
                foreach (var p in products)
                {
                    if (p?.definition == null) continue;
                    var sku = !string.IsNullOrEmpty(p.definition.id)
                        ? p.definition.id : p.definition.storeSpecificId;
                    if (!string.IsNullOrEmpty(sku)) Bugpunch.DeclareIapSku(sku);
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[Bugpunch] DeclareIapSku batch failed: {ex.Message}"); }
        }

        static void OnPurchasePending_V5(PendingOrder order)
        {
            try
            {
                var items = order?.CartOrdered?.Items();
                if (items == null) return;
                foreach (var item in items)
                {
                    var sku = item?.Product?.definition?.id;
                    if (!string.IsNullOrEmpty(sku)) Bugpunch.BeginPurchase(sku);
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[Bugpunch] BeginPurchase failed: {ex.Message}"); }
        }

        static void OnPurchaseFailed_V5(FailedOrder order)
        {
            try
            {
                var items = order?.CartOrdered?.Items();
                var sku = "";
                if (items != null)
                {
                    foreach (var item in items)
                    {
                        sku = item?.Product?.definition?.id ?? "";
                        if (!string.IsNullOrEmpty(sku)) break;
                    }
                }
                var props = new Dictionary<string, object>(2)
                {
                    ["sku"] = sku,
                    ["reason"] = order?.FailureReason.ToString() ?? "",
                };
                Bugpunch.LogDesign("coverage.iap_failed", props);
            }
            catch (Exception ex) { Debug.LogWarning($"[Bugpunch] IAP failure log failed: {ex.Message}"); }
        }

        static void OnPurchaseConfirmed_V5(Order order)
        {
            // OnPurchaseConfirmed can fire with a FailedOrder per the docs;
            // only ConfirmedOrder counts toward analytics.
            if (order is not ConfirmedOrder) return;
            try { LogConfirmedOrder_V5(order); }
            catch (Exception ex) { Debug.LogWarning($"[Bugpunch] purchase log failed: {ex.Message}"); }
        }

        static void LogConfirmedOrder_V5(Order order)
        {
            var transactionId = order?.Info?.TransactionID ?? string.Empty;
            var items = order?.CartOrdered?.Items();
            if (items == null) return;

            foreach (var item in items)
            {
                var product = item?.Product;
                if (product?.definition == null || product.metadata == null) continue;
                if (product.metadata.isoCurrencyCode == null) continue;

                var sku = !string.IsNullOrEmpty(product.definition.id)
                    ? product.definition.id
                    : product.definition.storeSpecificId;
                if (string.IsNullOrEmpty(sku)) continue;

                Bugpunch.LogPurchase(
                    sku: sku,
                    price: (double)product.metadata.localizedPrice,
                    currency: product.metadata.isoCurrencyCode,
                    transactionId: transactionId
                );
            }
        }
#endif // BUGPUNCH_HAS_UNITY_IAP_V5

#if BUGPUNCH_HAS_UNITY_IAP_V4
        // ─── Unity IAP v4 (IStoreController + IStoreListener) ───────────────

        /// <summary>
        /// Call from <c>IStoreListener.OnInitialized</c> with the controller
        /// Unity hands you. Declares every catalog SKU so they appear on
        /// the Coverage page as untested before any tester buys.
        /// </summary>
        public static IStoreController WithBugpunch(this IStoreController controller)
        {
            if (controller == null) return null;
            try
            {
                var all = controller.products?.all;
                if (all == null) return controller;
                foreach (var p in all)
                {
                    if (p?.definition == null) continue;
                    var sku = !string.IsNullOrEmpty(p.definition.id)
                        ? p.definition.id : p.definition.storeSpecificId;
                    if (!string.IsNullOrEmpty(sku)) Bugpunch.DeclareIapSku(sku);
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[Bugpunch] DeclareIapSku batch failed: {ex.Message}"); }
            return controller;
        }

        /// <summary>
        /// Call before <c>IStoreController.InitiatePurchase(sku)</c> so the
        /// Coverage page's IAP call-site axis registers the attempt.
        /// </summary>
        public static void BeginPurchase(string sku)
        {
            if (!string.IsNullOrEmpty(sku)) Bugpunch.BeginPurchase(sku);
        }

        /// <summary>
        /// Call from <c>IStoreListener.ProcessPurchase(PurchaseEventArgs)</c>
        /// to emit the typed business event (revenue / ARPPU / ARPDAU).
        /// Safe to call before returning <c>PurchaseProcessingResult.Complete</c>.
        /// </summary>
        public static void LogPurchase(PurchaseEventArgs args)
        {
            try
            {
                var product = args?.purchasedProduct;
                if (product?.definition == null || product.metadata == null) return;
                if (product.metadata.isoCurrencyCode == null) return;

                var sku = !string.IsNullOrEmpty(product.definition.id)
                    ? product.definition.id
                    : product.definition.storeSpecificId;
                if (string.IsNullOrEmpty(sku)) return;

                Bugpunch.DeclareIapSku(sku);
                Bugpunch.LogPurchase(
                    sku: sku,
                    price: (double)product.metadata.localizedPrice,
                    currency: product.metadata.isoCurrencyCode,
                    transactionId: product.transactionID ?? string.Empty
                );
            }
            catch (Exception ex) { Debug.LogWarning($"[Bugpunch] purchase log failed: {ex.Message}"); }
        }

        /// <summary>
        /// Call from <c>IStoreListener.OnPurchaseFailed(Product, PurchaseFailureReason)</c>
        /// so failed attempts surface as a separate signal on the Coverage page.
        /// </summary>
        public static void LogPurchaseFailed(Product product, PurchaseFailureReason reason)
        {
            try
            {
                var sku = product?.definition?.id ?? string.Empty;
                var props = new Dictionary<string, object>(2)
                {
                    ["sku"] = sku,
                    ["reason"] = reason.ToString(),
                };
                Bugpunch.LogDesign("coverage.iap_failed", props);
            }
            catch (Exception ex) { Debug.LogWarning($"[Bugpunch] IAP failure log failed: {ex.Message}"); }
        }
#endif // BUGPUNCH_HAS_UNITY_IAP_V4
    }
}

#endif // BUGPUNCH_HAS_UNITY_IAP
