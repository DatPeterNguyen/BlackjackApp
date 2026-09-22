using System.Threading.Tasks;

namespace BlackjackApp.core.Services;

/// <summary>
/// The app's one way of showing an ad - a rewarded video the player chooses
/// to watch in exchange for chips (see <see cref="ChipRescue"/> for what that
/// is worth and how often it is allowed).
///
/// Rewarded video specifically, rather than banners or interstitials: this is
/// a fast game loop, and an ad the player opts into when they have run out of
/// chips costs them nothing they did not agree to, where an interstitial
/// between rounds would interrupt the thing they came to do.
///
/// Deliberately the only ad surface. Keeping it to one method means there is
/// exactly one place in the app an ad can appear, so it cannot quietly spread.
///
/// Lives here in core, with no MAUI or SDK types anywhere in it, so the rules
/// that decide when an ad is offered stay testable without an ad network -
/// BlackjackApp.Maui supplies the real AdMob-backed implementation.
/// </summary>
public interface IAdService
{
    /// <summary>
    /// Whether a rewarded video is loaded and can be shown this instant.
    /// False when one is still loading, failed to load, or has already been
    /// consumed - the offer should simply not be made rather than showing the
    /// player a button that stalls.
    /// </summary>
    bool IsRewardedAdReady { get; }

    /// <summary>
    /// Starts fetching the next rewarded video. Safe to call when one is
    /// already loaded or in flight; implementations no-op in that case.
    /// Called well ahead of the offer, since a video that starts loading only
    /// when the player asks for it will not be ready in time.
    /// </summary>
    void PreloadRewardedAd();

    /// <summary>
    /// Shows the loaded video and completes once it closes.
    /// </summary>
    /// <returns>
    /// True ONLY if the player watched far enough for the network to say the
    /// reward was earned. Closing early, an ad that fails to show, and no ad
    /// being loaded all return false. Callers must pay out on true alone -
    /// crediting on close would pay for skipped ads and is exactly what the
    /// networks treat as invalid traffic.
    /// </returns>
    Task<bool> ShowRewardedAdAsync();
}
