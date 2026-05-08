// BugpunchAdMob.cs — Google Mobile Ads (AdMob) integration.
//
// Optional: only compiles when the game has `com.google.ads.mobile` installed.
// The `BUGPUNCH_HAS_ADMOB` symbol is set by a version-define on
// ODDGames.Bugpunch.Ads.asmdef, so when the AdMob package is absent this file
// is not part of the build — no reflection, no failed dynamic loads, no
// runtime overhead.
//
// Integration (one line per ad instance, right after Load):
//
//     using ODDGames.Bugpunch;
//     ...
//     RewardedAd.Load(adUnitId, request, (ad, err) => {
//         if (err != null) { /* handle */ return; }
//         ad.WithBugpunch(placement: "level_complete");
//         ad.Show(reward => GrantCoins(reward));
//     });
//
// The wrapper is idempotent (wrapping the same ad twice is a no-op) and
// preserves every existing project subscription — Bugpunch just adds its own
// listener to each lifecycle event that AdMob's Unity SDK exposes.
//
// Pattern mirrors `BugpunchPurchasing.WithBugpunch(this IPurchaseService service)`.
// AdMob's ad classes are sealed, so we attach listeners rather than wrapping;
// idempotency uses a ConditionalWeakTable keyed by the ad instance.

#if BUGPUNCH_HAS_ADMOB

using System.Runtime.CompilerServices;
using GoogleMobileAds.Api;

namespace ODDGames.Bugpunch
{
    /// <summary>
    /// Extension-method surface for attaching Bugpunch telemetry to existing
    /// AdMob ad instances. One call per ad — typically right after the Load
    /// callback hands you the ad. Forwards lifecycle events to
    /// <see cref="Bugpunch.LogAd"/> as the standard
    /// <c>shown / click / fail / paid / close</c> action vocabulary.
    /// </summary>
    public static class BugpunchAdMob
    {
        const string SdkName = "admob";

        // Attached-instance registry. ConditionalWeakTable holds weak keys so
        // garbage-collected ads drop out automatically — no leak even if the
        // game retains a long-lived map of placements.
        static readonly ConditionalWeakTable<object, object> s_attached =
            new ConditionalWeakTable<object, object>();
        static readonly object s_marker = new object();

        static bool TryMarkAttached(object ad)
        {
            if (ad == null) return false;
            if (s_attached.TryGetValue(ad, out _)) return false;
            s_attached.Add(ad, s_marker);
            return true;
        }

        // ─── Rewarded ───────────────────────────────────────────────────

        /// <summary>
        /// Subscribe Bugpunch telemetry to a loaded <see cref="RewardedAd"/>.
        /// Idempotent — wrapping the same ad twice is a no-op. Returns the
        /// same ad instance so the call can chain into <c>.Show(...)</c>.
        /// Pass <paramref name="placement"/> to tag events with the in-game
        /// context (level name, menu screen, button id, ...).
        ///
        /// Note: rewarded reward delivery happens via the user-supplied
        /// callback on <c>Show(Action&lt;Reward&gt;)</c>, NOT an event on the
        /// ad — Bugpunch can't see it from this wrapper. Game code should
        /// call <c>Bugpunch.LogAd("reward", "rewarded", "admob", placement)</c>
        /// inside that callback if reward attribution is needed.
        /// </summary>
        public static RewardedAd WithBugpunch(this RewardedAd ad, string placement = null)
        {
            if (!TryMarkAttached(ad)) return ad;
            string adType = "rewarded";
            ad.OnAdFullScreenContentOpened += () => Bugpunch.LogAd("shown",  adType, SdkName, placement);
            ad.OnAdFullScreenContentClosed += () => Bugpunch.LogAd("close",  adType, SdkName, placement);
            ad.OnAdFullScreenContentFailed += err => Bugpunch.LogAd("fail",  adType, SdkName, placement);
            ad.OnAdImpressionRecorded     += () => { /* impression == shown above */ };
            ad.OnAdClicked                += () => Bugpunch.LogAd("click",  adType, SdkName, placement);
            ad.OnAdPaid                   += value => Bugpunch.LogAd("paid", adType, SdkName, placement, ToUsd(value));
            return ad;
        }

        // ─── Interstitial ───────────────────────────────────────────────

        public static InterstitialAd WithBugpunch(this InterstitialAd ad, string placement = null)
        {
            if (!TryMarkAttached(ad)) return ad;
            string adType = "interstitial";
            ad.OnAdFullScreenContentOpened += () => Bugpunch.LogAd("shown",  adType, SdkName, placement);
            ad.OnAdFullScreenContentClosed += () => Bugpunch.LogAd("close",  adType, SdkName, placement);
            ad.OnAdFullScreenContentFailed += err => Bugpunch.LogAd("fail",  adType, SdkName, placement);
            ad.OnAdClicked                += () => Bugpunch.LogAd("click",  adType, SdkName, placement);
            ad.OnAdPaid                   += value => Bugpunch.LogAd("paid", adType, SdkName, placement, ToUsd(value));
            return ad;
        }

        // ─── App Open ───────────────────────────────────────────────────

        public static AppOpenAd WithBugpunch(this AppOpenAd ad, string placement = null)
        {
            if (!TryMarkAttached(ad)) return ad;
            string adType = "app_open";
            ad.OnAdFullScreenContentOpened += () => Bugpunch.LogAd("shown",  adType, SdkName, placement);
            ad.OnAdFullScreenContentClosed += () => Bugpunch.LogAd("close",  adType, SdkName, placement);
            ad.OnAdFullScreenContentFailed += err => Bugpunch.LogAd("fail",  adType, SdkName, placement);
            ad.OnAdClicked                += () => Bugpunch.LogAd("click",  adType, SdkName, placement);
            ad.OnAdPaid                   += value => Bugpunch.LogAd("paid", adType, SdkName, placement, ToUsd(value));
            return ad;
        }

        // ─── Rewarded Interstitial ──────────────────────────────────────

        public static RewardedInterstitialAd WithBugpunch(this RewardedInterstitialAd ad, string placement = null)
        {
            if (!TryMarkAttached(ad)) return ad;
            string adType = "rewarded_interstitial";
            ad.OnAdFullScreenContentOpened += () => Bugpunch.LogAd("shown",  adType, SdkName, placement);
            ad.OnAdFullScreenContentClosed += () => Bugpunch.LogAd("close",  adType, SdkName, placement);
            ad.OnAdFullScreenContentFailed += err => Bugpunch.LogAd("fail",  adType, SdkName, placement);
            ad.OnAdClicked                += () => Bugpunch.LogAd("click",  adType, SdkName, placement);
            ad.OnAdPaid                   += value => Bugpunch.LogAd("paid", adType, SdkName, placement, ToUsd(value));
            return ad;
        }

        // ─── Banner ─────────────────────────────────────────────────────

        /// <summary>
        /// Subscribe Bugpunch telemetry to a <see cref="BannerView"/>. Banner
        /// ads don't open / close as full-screen content; the lifecycle
        /// covered here is load (after the ad-loaded event), impression,
        /// click, and paid revenue.
        /// </summary>
        public static BannerView WithBugpunch(this BannerView ad, string placement = null)
        {
            if (!TryMarkAttached(ad)) return ad;
            string adType = "banner";
            ad.OnBannerAdLoaded            += () => Bugpunch.LogAd("shown", adType, SdkName, placement);
            ad.OnBannerAdLoadFailed        += err => Bugpunch.LogAd("fail",  adType, SdkName, placement);
            ad.OnAdImpressionRecorded     += () => { /* impression covered by load */ };
            ad.OnAdClicked                += () => Bugpunch.LogAd("click",  adType, SdkName, placement);
            ad.OnAdPaid                   += value => Bugpunch.LogAd("paid", adType, SdkName, placement, ToUsd(value));
            return ad;
        }

        // ─── Helpers ────────────────────────────────────────────────────

        // AdMob reports paid revenue in micro-units of the network's reporting
        // currency (`AdValue.Value` is int64 micros, `.CurrencyCode` is the
        // ISO code). LogAd takes USD; non-USD networks get reported with
        // their micro value coerced to a USD double — server-side conversion
        // can apply a more precise rate later if needed. Returning null when
        // the value is missing keeps the typed event without an amount.
        static double? ToUsd(AdValue value)
        {
            if (value == null) return null;
            return value.Value / 1_000_000.0;
        }
    }
}

#endif
