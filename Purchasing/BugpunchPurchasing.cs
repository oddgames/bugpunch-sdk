// BugpunchPurchasing.cs — Unity IAP v5 integration.
//
// Optional: only compiles when the game has `com.unity.purchasing` >= 5.0.0.
// The `BUGPUNCH_HAS_UNITY_IAP` symbol is set by a version-define on the
// asmdef, so when Unity IAP is absent (or below 5.0.0) this file is just
// not part of the build — no reflection, no failed dynamic loads, no
// runtime overhead.
//
// Integration (one line at the dev's IPurchaseService creation site):
//
//     using ODDGames.BugpunchSdk;
//     ...
//     m_PurchaseService = UnityIAPServices.DefaultPurchase().WithBugpunch();
//
// Bugpunch subscribes to `OnPurchaseConfirmed` and side-channels every
// confirmed purchase to `Bugpunch.LogPurchase(...)` so they land in the
// analytics pipeline without the game having to duplicate the call.
//
// Note: v4 IStoreListener support was removed when Unity IAP shipped v5,
// since `IStoreListener` / `IDetailedStoreListener` no longer exist.

#if BUGPUNCH_HAS_UNITY_IAP

using System;
using UnityEngine;
using UnityEngine.Purchasing;

namespace ODDGames.BugpunchSdk
{
    /// <summary>
    /// Extension-method surface for hooking Bugpunch purchase logging into
    /// a Unity IAP v5 <see cref="IPurchaseService"/>.
    /// </summary>
    public static class BugpunchPurchasing
    {
        /// <summary>
        /// One-line auto-wire for Unity IAP v5. Hooks every observable
        /// event so the game side never has to call Bugpunch APIs
        /// explicitly. Idempotent — re-subscription detaches first.
        ///
        /// Wires:
        ///   • <c>OnProductsFetched</c>  → <see cref="Bugpunch.DeclareIapSku"/>
        ///     for every catalog product so SKUs appear on the Coverage
        ///     page as untested *before* any tester buys.
        ///   • <c>OnPurchasePending</c>  → <see cref="Bugpunch.BeginPurchase"/>
        ///     marks the initiation event so the Coverage page's IAP
        ///     call-site axis registers the attempt.
        ///   • <c>OnPurchaseConfirmed</c> → <see cref="Bugpunch.LogPurchase"/>
        ///     emits the typed business event (revenue / ARPPU / ARPDAU)
        ///     and also seeds <c>iap_sku</c> coverage via
        ///     <see cref="Bugpunch.DeclareIapSku"/> in case
        ///     <c>OnProductsFetched</c> wasn't observed.
        ///   • <c>OnPurchaseFailed</c>   → emits a <c>coverage.iap_failed</c>
        ///     design event so failed attempts surface as a separate signal
        ///     on the Coverage page without polluting the revenue stream.
        /// </summary>
        public static IPurchaseService WithBugpunch(this IPurchaseService service)
        {
            if (service == null) return null;

            service.OnPurchaseConfirmed -= OnPurchaseConfirmed;
            service.OnPurchaseConfirmed += OnPurchaseConfirmed;

            // The remaining events are optional — Unity IAP v5 ships them
            // but specific minor versions may rename. Hook them in a
            // try/catch so a version skew doesn't break WithBugpunch.
            TryHook(() => {
                service.OnProductsFetched -= OnProductsFetched;
                service.OnProductsFetched += OnProductsFetched;
            }, nameof(service.OnProductsFetched));
            TryHook(() => {
                service.OnPurchasePending -= OnPurchasePending;
                service.OnPurchasePending += OnPurchasePending;
            }, "OnPurchasePending");
            TryHook(() => {
                service.OnPurchaseFailed -= OnPurchaseFailed;
                service.OnPurchaseFailed += OnPurchaseFailed;
            }, "OnPurchaseFailed");

            return service;
        }

        static void OnProductsFetched(Products products)
        {
            try
            {
                if (products == null) return;
                var all = products.all;
                if (all == null) return;
                foreach (var p in all)
                {
                    if (p?.definition == null) continue;
                    var sku = !string.IsNullOrEmpty(p.definition.id)
                        ? p.definition.id : p.definition.storeSpecificId;
                    if (!string.IsNullOrEmpty(sku)) Bugpunch.DeclareIapSku(sku);
                }
            }
            catch (Exception ex) { Debug.LogWarning($"[Bugpunch] DeclareIapSku batch failed: {ex.Message}"); }
        }

        static void OnPurchasePending(PendingOrder order)
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

        static void OnPurchaseFailed(FailedOrder order)
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
                var props = new System.Collections.Generic.Dictionary<string, object>(2)
                {
                    ["sku"] = sku,
                    ["reason"] = order?.FailureReason.ToString() ?? "",
                };
                Bugpunch.LogDesign("coverage.iap_failed", props);
            }
            catch (Exception ex) { Debug.LogWarning($"[Bugpunch] IAP failure log failed: {ex.Message}"); }
        }

        static void TryHook(Action act, string what)
        {
            try { act(); }
            catch (Exception ex)
            {
                Debug.LogWarning($"[Bugpunch] WithBugpunch hook '{what}' skipped: {ex.Message}");
            }
        }

        /// <summary>
        /// Pre-declare an IAP catalog so every SKU appears on the Coverage
        /// page as an untested unit before any tester buys it. Pass the
        /// catalog the game registers with Unity IAP (typically a
        /// <c>ProductCatalog.LoadDefaultCatalog()</c> result).
        /// </summary>
        public static void DeclareIapCatalog(params string[] skus)
        {
            if (skus == null) return;
            foreach (var sku in skus)
                if (!string.IsNullOrEmpty(sku)) Bugpunch.DeclareIapSku(sku);
        }

        static void OnPurchaseConfirmed(Order order)
        {
            // OnPurchaseConfirmed can fire with a FailedOrder per the docs;
            // only ConfirmedOrder counts toward analytics.
            if (order is not ConfirmedOrder) return;
            try { LogPurchase(order); }
            catch (Exception ex) { Debug.LogWarning($"[Bugpunch] purchase log failed: {ex.Message}"); }
        }

        static void LogPurchase(Order order)
        {
            var transactionId = order?.Info?.TransactionID ?? string.Empty;
            var items = order?.CartOrdered?.Items();
            if (items == null) return;

            foreach (var item in items)
            {
                var product = item?.Product;
                if (product?.definition == null || product.metadata == null) continue;
                if (product.metadata.isoCurrencyCode == null) continue;  // mirrors Unity IAP's own guard

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
    }
}

#endif
