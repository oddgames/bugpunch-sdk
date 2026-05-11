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
        /// Subscribe Bugpunch's purchase logger to the given service.
        /// Idempotent — re-subscription is detached first so multiple calls
        /// won't double-log. Returns the same service for chaining.
        /// </summary>
        public static IPurchaseService WithBugpunch(this IPurchaseService service)
        {
            if (service == null) return null;
            service.OnPurchaseConfirmed -= OnPurchaseConfirmed;
            service.OnPurchaseConfirmed += OnPurchaseConfirmed;
            return service;
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
