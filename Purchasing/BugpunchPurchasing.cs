// BugpunchPurchasing.cs — Unity IAP integration.
//
// Optional: only compiles when the game has `com.unity.purchasing` installed.
// The `BUGPUNCH_HAS_UNITY_IAP` symbol is set by a version-define on
// ODDGames.Bugpunch.asmdef, so when Unity IAP is absent this file is just
// not part of the build — no reflection, no failed dynamic loads, no runtime
// overhead.
//
// Integration (one line at the dev's UnityPurchasing.Initialize call site):
//
//     using ODDGames.Bugpunch;
//     ...
//     UnityPurchasing.Initialize(myListener.WithBugpunch(), builder);
//
// The wrapper forwards every callback to the game's listener unchanged, and
// side-channels successful purchases to `Bugpunch.LogPurchase(...)` so they
// land in the analytics pipeline without the game having to duplicate the
// call inside ProcessPurchase.
//
// Pattern cribbed from Unity IAP's own StoreListenerProxy (which is `internal`
// and therefore not a supported extension point — we wrap *outside* their
// proxy, not alongside it).

#if BUGPUNCH_HAS_UNITY_IAP

using System;
using UnityEngine;
using UnityEngine.Purchasing;
using UnityEngine.Purchasing.Extension;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// Extension-method surface for wrapping an existing <see cref="IStoreListener"/>
    /// so purchases are automatically logged to Bugpunch analytics.
    /// </summary>
    public static class BugpunchPurchasing
    {
        /// <summary>
        /// Wrap an existing listener so Bugpunch gets notified of every
        /// successful purchase. The inner listener's <c>ProcessPurchase</c>
        /// runs first; Bugpunch logs only if that returned a result (so a
        /// listener that throws won't silently double-count).
        /// </summary>
        public static IStoreListener WithBugpunch(this IStoreListener inner)
        {
            if (inner == null) return null;
            if (inner is BugpunchStoreListener) return inner;  // idempotent
            return new BugpunchStoreListener(inner);
        }
    }

    /// <summary>
    /// Decorator around the game's <see cref="IStoreListener"/>. Implements
    /// <see cref="IDetailedStoreListener"/> so it sits cleanly under Unity
    /// IAP's internal <c>StoreListenerProxy</c> on both the old (reason-only)
    /// and new (detailed) failure paths.
    /// </summary>
    internal sealed class BugpunchStoreListener : IDetailedStoreListener
    {
        readonly IStoreListener m_Inner;

        public BugpunchStoreListener(IStoreListener inner)
        {
            m_Inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public PurchaseProcessingResult ProcessPurchase(PurchaseEventArgs e)
        {
            // Game's handler runs first — Bugpunch is a side-channel, never
            // in the critical path of fulfilment.
            var result = m_Inner.ProcessPurchase(e);
            try { LogPurchase(e?.purchasedProduct); }
            catch (Exception ex) { Debug.LogWarning($"[Bugpunch] purchase log failed: {ex.Message}"); }
            return result;
        }

        static void LogPurchase(Product product)
        {
            if (product == null || product.definition == null || product.metadata == null) return;
            if (product.metadata.isoCurrencyCode == null) return;  // mirrors Unity IAP's own guard

            var sku = !string.IsNullOrEmpty(product.definition.id)
                ? product.definition.id
                : product.definition.storeSpecificId;
            if (string.IsNullOrEmpty(sku)) return;

            Bugpunch.LogPurchase(
                sku: sku,
                price: (double)product.metadata.localizedPrice,
                currency: product.metadata.isoCurrencyCode,
                transactionId: product.transactionID
            );
        }

        // ── Pass-through plumbing ────────────────────────────────────────

        public void OnInitialized(IStoreController controller, IExtensionProvider extensions)
            => m_Inner.OnInitialized(controller, extensions);

#pragma warning disable 0618  // Obsolete — kept because IStoreListener still declares it.
        public void OnInitializeFailed(InitializationFailureReason error)
            => m_Inner.OnInitializeFailed(error);
#pragma warning restore 0618

        public void OnInitializeFailed(InitializationFailureReason error, string message)
            => m_Inner.OnInitializeFailed(error, message);

#pragma warning disable 0618  // Obsolete reason-only overload — IStoreListener requires it.
        public void OnPurchaseFailed(Product product, PurchaseFailureReason failureReason)
            => m_Inner.OnPurchaseFailed(product, failureReason);
#pragma warning restore 0618

        // Newer failure callback from IDetailedStoreListener. If the inner
        // listener is also detailed, forward to its detailed overload;
        // otherwise collapse to the deprecated reason-only one so the game
        // still sees the failure.
        public void OnPurchaseFailed(Product product, PurchaseFailureDescription failureDescription)
        {
            if (m_Inner is IDetailedStoreListener detailed)
            {
                detailed.OnPurchaseFailed(product, failureDescription);
            }
            else
            {
#pragma warning disable 0618
                m_Inner.OnPurchaseFailed(product, failureDescription.reason);
#pragma warning restore 0618
            }
        }
    }
}

#endif
