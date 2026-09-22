using System.Threading.Tasks;
using BlackjackApp.core.Services;

namespace BlackjackApp.Maui.Services;

/// <summary>
/// An ad service that has no ads. Never ready, so ChipRescue never offers the
/// trade and the player simply never sees it.
///
/// This is the default in <see cref="AppServices"/>, and the failure mode for
/// the whole feature: if the ad SDK is missing, fails to initialise, or is
/// switched off, the game carries on exactly as it did before ads existed
/// rather than showing a button that does nothing.
/// </summary>
public sealed class NoOpAdService : IAdService
{
    public bool IsRewardedAdReady => false;

    public void PreloadRewardedAd()
    {
    }

    public Task<bool> ShowRewardedAdAsync() => Task.FromResult(false);
}
