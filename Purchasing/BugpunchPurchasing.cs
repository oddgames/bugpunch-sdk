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

        /// <summary>
        /// Blocking interceptor for Unity IAP v5. Auto-wires <see cref="WithBugpunch"/>
        /// (coverage), activates <see cref="BugpunchBlockingPurchases"/> — which
        /// auto-registers the QA "IAP" Debug Tools entry the first time blocking is
        /// used — and hooks the order events so the blocking state spans
        /// pending → confirmed/failed. Idempotent. Returns the same service.
        /// </summary>
        public static IPurchaseService WithBugpunchBlocking(this IPurchaseService service)
        {
            if (service == null) return null;
            service.WithBugpunch();
            BugpunchBlockingPurchases.Activate();
            TryHook(() => {
                service.OnPurchasePending -= OnBlockingPending_V5;
                service.OnPurchasePending += OnBlockingPending_V5;
            }, "blocking OnPurchasePending");
            TryHook(() => {
                service.OnPurchaseConfirmed -= OnBlockingConfirmed_V5;
                service.OnPurchaseConfirmed += OnBlockingConfirmed_V5;
            }, "blocking OnPurchaseConfirmed");
            TryHook(() => {
                service.OnPurchaseFailed -= OnBlockingFailed_V5;
                service.OnPurchaseFailed += OnBlockingFailed_V5;
            }, "blocking OnPurchaseFailed");
            return service;
        }

        static void OnBlockingPending_V5(PendingOrder order)
        {
            string sku = "";
            try
            {
                var items = order?.CartOrdered?.Items();
                if (items != null)
                    foreach (var item in items)
                    {
                        sku = item?.Product?.definition?.id ?? "";
                        if (!string.IsNullOrEmpty(sku)) break;
                    }
            }
            catch { /* best effort */ }
            // v5 owns its own confirm flow, so the QA tool can't force-resolve the
            // store generically — observe-only (overlay + log). AutoComplete still
            // surfaces; the v4 lane is where force-complete drives ConfirmPending.
            BugpunchBlockingPurchases.Begin(sku, null);
        }

        static void OnBlockingConfirmed_V5(Order order)
        {
            if (order is ConfirmedOrder) BugpunchBlockingPurchases.End(true);
        }

        static void OnBlockingFailed_V5(FailedOrder order)
        {
            BugpunchBlockingPurchases.End(false);
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
                BugpunchBlockingPurchases.End(false);
            }
            catch (Exception ex) { Debug.LogWarning($"[Bugpunch] IAP failure log failed: {ex.Message}"); }
        }

        /// <summary>
        /// Blocking interceptor for Unity IAP v4 — the generic, drop-in override.
        /// Wrap the controller Unity hands you once:
        /// <code>m_Controller = c.WithBugpunch().WithBugpunchBlocking();</code>
        /// Every <c>InitiatePurchase</c> then routes through the gate (no
        /// per-call-site change), and the first use auto-registers the QA "IAP"
        /// Debug Tools entry. For the gate to actually <i>hold</i> a purchase,
        /// return <see cref="PurchaseProcessingResult.Pending"/> from
        /// <c>ProcessPurchase</c> while <see cref="BugpunchBlockingPurchases.IsBlocking"/>;
        /// the QA tool's "Complete" then drives <c>ConfirmPendingPurchase</c>.
        /// </summary>
        public static IStoreController WithBugpunchBlocking(this IStoreController controller)
        {
            if (controller == null) return null;
            controller.WithBugpunch();
            BugpunchBlockingPurchases.Activate();
            return new BugpunchBlockingStoreController(controller);
        }

        /// <summary>Decorator over <see cref="IStoreController"/> that begins a
        /// blocking gate on each <c>InitiatePurchase</c> and resolves it (on the
        /// QA tool's "Complete") by confirming the pending purchase.</summary>
        sealed class BugpunchBlockingStoreController : IStoreController
        {
            readonly IStoreController _inner;
            public BugpunchBlockingStoreController(IStoreController inner) { _inner = inner; }

            public ProductCollection products => _inner.products;

            public void InitiatePurchase(Product product) { BeginFor(product?.definition?.id); _inner.InitiatePurchase(product); }
            public void InitiatePurchase(string productId) { BeginFor(productId); _inner.InitiatePurchase(productId); }
            public void InitiatePurchase(Product product, string payload) { BeginFor(product?.definition?.id); _inner.InitiatePurchase(product, payload); }
            public void InitiatePurchase(string productId, string payload) { BeginFor(productId); _inner.InitiatePurchase(productId, payload); }

            public void ConfirmPendingPurchase(Product product) { _inner.ConfirmPendingPurchase(product); }

            public void FetchAdditionalProducts(HashSet<ProductDefinition> additionalProducts,
                Action successCallback, Action<InitializationFailureReason> failCallback)
                => _inner.FetchAdditionalProducts(additionalProducts, successCallback, failCallback);

            void BeginFor(string sku)
            {
                BugpunchBlockingPurchases.Begin(sku, success =>
                {
                    if (!success) return;
                    try
                    {
                        var p = _inner.products?.WithID(sku);
                        if (p != null) _inner.ConfirmPendingPurchase(p);
                    }
                    catch (Exception ex) { Debug.LogWarning($"[Bugpunch] confirm pending failed: {ex.Message}"); }
                });
            }
        }
#endif // BUGPUNCH_HAS_UNITY_IAP_V4
    }

    /// <summary>
    /// Coordinator for the blocking-IAP interceptor (<c>WithBugpunchBlocking</c>).
    /// Holds the blocking state for the in-flight purchase and — the first time
    /// any blocking is used — auto-registers a QA "IAP" entry in the Debug Tools
    /// panel so an internal tester can resolve a sandbox purchase without real
    /// money: an <b>Auto-complete purchases</b> toggle plus <b>Complete</b> /
    /// <b>Fail</b> buttons. No Unity IAP types here — the interceptor extensions
    /// feed it skus + a resolve callback.
    /// </summary>
    internal static class BugpunchBlockingPurchases
    {
        static bool s_active;
        static bool s_autoComplete;
        static System.Action<bool> s_pendingResolve;
        static string s_pendingSku;

        /// <summary>True while a purchase is held by the gate — the game's
        /// <c>ProcessPurchase</c> can read this to return <c>Pending</c>.</summary>
        public static bool IsBlocking => s_pendingResolve != null || !string.IsNullOrEmpty(s_pendingSku);

        /// <summary>First-use activation — registers the QA tool once.</summary>
        public static void Activate()
        {
            if (s_active) return;
            s_active = true;
            RegisterTool();
        }

        /// <summary>A purchase entered the gate. <paramref name="resolve"/> (may be
        /// null on lanes that can't force the store) is invoked with the QA
        /// decision. Auto-complete resolves it as success immediately.</summary>
        public static void Begin(string sku, System.Action<bool> resolve)
        {
            s_pendingSku = sku ?? "";
            s_pendingResolve = resolve;
            UnityEngine.Debug.Log($"[Bugpunch] blocking purchase begin: \"{s_pendingSku}\"");
            if (s_autoComplete) Resolve(true);
        }

        /// <summary>The purchase left the gate (store confirmed/failed).</summary>
        public static void End(bool success)
        {
            if (!IsBlocking) return;
            UnityEngine.Debug.Log($"[Bugpunch] blocking purchase end ({(success ? "confirmed" : "failed")}): \"{s_pendingSku}\"");
            s_pendingResolve = null;
            s_pendingSku = null;
        }

        static void Resolve(bool success)
        {
            var r = s_pendingResolve;
            var sku = s_pendingSku;
            s_pendingResolve = null;
            s_pendingSku = null;
            UnityEngine.Debug.Log($"[Bugpunch] QA {(success ? "completed" : "failed")} blocked purchase: \"{sku}\"");
            if (r != null)
            {
                try { r(success); }
                catch (System.Exception e) { UnityEngine.Debug.LogWarning($"[Bugpunch] blocking resolve failed: {e.Message}"); }
            }
        }

        static void RegisterTool()
        {
            DebugToolsBridge.RegisterDynamicTool(
                "bugpunch.iap.autocomplete", "IAP", "Auto-complete purchases",
                "Instantly resolve every blocked purchase as success.", "bolt",
                "toggle", adminOnly: true,
                onToggle: v => s_autoComplete = v, toggleGet: () => s_autoComplete);
            DebugToolsBridge.RegisterDynamicTool(
                "bugpunch.iap.complete", "IAP", "Complete purchase",
                "Force-resolve the in-flight blocked purchase as success.", "check_circle",
                "button", adminOnly: true, onClick: () => Resolve(true));
            DebugToolsBridge.RegisterDynamicTool(
                "bugpunch.iap.fail", "IAP", "Fail purchase",
                "Force-resolve the in-flight blocked purchase as failure.", "cancel",
                "button", adminOnly: true, onClick: () => Resolve(false));
        }
    }
}

#endif // BUGPUNCH_HAS_UNITY_IAP
