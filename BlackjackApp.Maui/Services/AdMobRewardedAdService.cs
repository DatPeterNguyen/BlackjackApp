using System.Threading.Tasks;
using BlackjackApp.core.Services;
using Plugin.MauiMtAdmob;

namespace BlackjackApp.Maui.Services;

/// <summary>
/// The real <see cref="IAdService"/>, backed by AdMob through
/// Plugin.MauiMTAdmob.
///
/// Wraps the plugin's event-based API - load, then wait for one of several
/// events - into the single awaitable call the rest of the app uses, so no
/// caller has to reason about which AdMob callback means "pay the player".
///
/// The distinction that matters: OnUserEarnedReward is the ONLY signal that
/// earns chips. OnRewardedClosed fires whether the player watched it through
/// or dismissed it after two seconds, so paying on close would pay for
/// skipped ads - which is both unfair to the advertiser and the kind of thing
/// ad networks treat as invalid traffic. Both are handled: the reward flag is
/// latched by OnUserEarnedReward, and close is what completes the wait.
///
/// AD UNITS ARE GOOGLE'S PUBLIC TEST UNITS. They serve real ad creatives that
/// always fill, and they earn nothing. They must be swapped for the account's
/// own unit IDs before release - see TestRewardedAdUnit below.
/// </summary>
public sealed class AdMobRewardedAdService : IAdService
{
    /// <summary>
    /// Google's public test rewarded unit, which differs per platform - the
    /// iOS one is NOT interchangeable with the Android one, and using the
    /// wrong platform's unit simply never fills, with no error to explain it.
    ///
    /// https://developers.google.com/admob/ios/test-ads
    /// https://developers.google.com/admob/android/test-ads
    /// </summary>
    private static string TestRewardedAdUnit =>
#if IOS
        "ca-app-pub-3940256099942544/1712485313";
#elif ANDROID
        "ca-app-pub-3940256099942544/5224354917";
#else
        string.Empty;
#endif

    /// <summary>
    /// How long to wait for AdMob to say the ad closed before giving up on it.
    /// Generous: a rewarded video plus its end card runs well under a minute,
    /// so this only ever fires when a terminal event genuinely never arrives.
    /// Without it a lost event would hang the awaiting round forever AND leave
    /// _showing non-null, which makes every later call return false - the
    /// rescue would be dead for the rest of the session.
    /// </summary>
    private static readonly TimeSpan PresentationTimeout = TimeSpan.FromMinutes(3);

    /// <summary>Completes when the current ad closes; null when no ad is showing.</summary>
    private TaskCompletionSource<bool>? _showing;

    /// <summary>Latched by OnUserEarnedReward, read when the ad closes.</summary>
    private bool _rewardEarned;

    public AdMobRewardedAdService()
    {
        // Discard lambdas rather than named handlers on purpose: these three
        // events carry three different argument types (the reward one has the
        // reward on it, the failure ones carry the error), and none of that is
        // needed here - only which event fired. Letting the compiler infer the
        // parameter types keeps this from having to name plugin types it does
        // not otherwise touch.
        //
        // Never unsubscribed, because there is exactly one of these and it
        // lives as long as the app does - see AppServices.
        CrossMauiMTAdmob.Current.OnUserEarnedReward += (_, _) => _rewardEarned = true;
        CrossMauiMTAdmob.Current.OnRewardedClosed += (_, _) => Complete(_rewardEarned);
        CrossMauiMTAdmob.Current.OnRewardedFailedToShow += (_, _) => Complete(false);
    }

    public bool IsRewardedAdReady
    {
        get
        {
            // The plugin talks to a native SDK that may not have initialised,
            // and on desktop there is no SDK at all. Never let "is there an
            // ad?" take the game down - no ad is always a valid answer.
            try
            {
                return !string.IsNullOrEmpty(TestRewardedAdUnit)
                    && CrossMauiMTAdmob.Current.IsRewardedLoaded();
            }
            catch
            {
                return false;
            }
        }
    }

    public void PreloadRewardedAd()
    {
        if (string.IsNullOrEmpty(TestRewardedAdUnit))
        {
            return;
        }

        try
        {
            if (!CrossMauiMTAdmob.Current.IsRewardedLoaded())
            {
                CrossMauiMTAdmob.Current.LoadRewarded(TestRewardedAdUnit);
            }
        }
        catch
        {
            // Nothing to do but leave IsRewardedAdReady false, which means
            // the offer is never made.
        }
    }

    public async Task<bool> ShowRewardedAdAsync()
    {
        if (!IsRewardedAdReady || _showing is not null)
        {
            return false;
        }

        _rewardEarned = false;
        var showing = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _showing = showing;

        try
        {
            CrossMauiMTAdmob.Current.ShowRewarded();
        }
        catch
        {
            _showing = null;
            return false;
        }

        // Never await the close event unguarded - see PresentationTimeout.
        var settled = await Task.WhenAny(showing.Task, Task.Delay(PresentationTimeout));

        if (settled != showing.Task)
        {
            // No terminal event arrived. Settle it ourselves so the service is
            // usable again, and pay nothing, since nothing confirmed a reward.
            Complete(false);
            PreloadRewardedAd();
            return false;
        }

        var earned = await showing.Task;

        // Line up the next one now rather than when the player next busts
        // out, so it has time to fill before it is needed.
        PreloadRewardedAd();

        return earned;
    }

    /// <summary>
    /// Settles the pending show exactly once. AdMob can raise more than one
    /// terminal event for a single presentation, and TrySetResult keeps the
    /// second from throwing.
    /// </summary>
    private void Complete(bool earned)
    {
        var showing = _showing;
        _showing = null;
        showing?.TrySetResult(earned);
    }
}
