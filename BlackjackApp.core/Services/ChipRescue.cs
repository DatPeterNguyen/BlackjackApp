using System;
using BlackjackApp.core.Economy;

namespace BlackjackApp.core.Services;

/// <summary>
/// A snapshot of whether the player can trade an ad for chips right now, as
/// returned by <see cref="ChipRescue.GetStatus"/>.
/// </summary>
/// <param name="CanWatch">
/// Whether to offer the trade at all. True only when the player is genuinely
/// out of chips AND has watches left today AND an ad is actually loaded.
/// </param>
/// <param name="WatchesUsedToday">How many have already been taken today, after any day rollover.</param>
/// <param name="WatchesRemainingToday">How many are left today. Zero once the cap is reached.</param>
/// <param name="Reward">What the next watch would pay, purely so the UI can name the figure.</param>
public readonly record struct ChipRescueStatus(
    bool CanWatch,
    int WatchesUsedToday,
    int WatchesRemainingToday,
    decimal Reward);

/// <summary>
/// The bust-out rescue: when the player has no chips left, they may watch a
/// rewarded video (see <see cref="IAdService"/>) for a small stake rather
/// than being stuck at a table they cannot bet on.
///
/// Three deliberate limits keep this a rescue rather than a chip faucet:
///
///  - It is only offered when the balance genuinely cannot cover
///    <see cref="ChipWallet.TableMinimum"/>. Not "running low" - actually
///    unable to place the smallest legal bet. A player with chips can always
///    keep playing, so there is nothing to rescue them from.
///  - <see cref="MaxWatchesPerDay"/> caps it. A whole day of watching pays
///    less than the $1,000 a fresh start hands out, so grinding ads is never
///    better than simply resetting progress.
///  - It pays <see cref="RewardPerAd"/>, well above the daily check-in, since
///    it is asking for 30 seconds of attention rather than a tap - but capped
///    per day so it cannot overtake the check-in as the main way to earn.
///
/// Both figures are here, together, so the balance between the ad reward and
/// the rest of the economy can be read and retuned in one place.
///
/// Pure and date-driven in exactly the way <see cref="DailyCheckIn"/> is:
/// every method takes "today" as an argument rather than reading the clock,
/// so the daily cap and its rollover are testable without faking time.
/// Persisting the date and count is the caller's job (on MAUI,
/// GameProgressStorage).
/// </summary>
public static class ChipRescue
{
    /// <summary>What one watched video pays.</summary>
    public const decimal RewardPerAd = 250m;

    /// <summary>How many videos a player may trade in per calendar day.</summary>
    public const int MaxWatchesPerDay = 3;

    /// <summary>
    /// Whether the player literally cannot place the smallest legal bet. This
    /// is the whole trigger for the offer - see the class remarks for why it
    /// is this and not a "running low" threshold.
    /// </summary>
    public static bool IsBustedOut(decimal balance) => balance < ChipWallet.TableMinimum;

    /// <summary>
    /// Works out whether to offer the trade right now.
    /// </summary>
    /// <param name="balance">The player's current balance.</param>
    /// <param name="lastWatchDate">The day the counter below belongs to; null if they have never watched one.</param>
    /// <param name="watchesOnThatDate">How many were watched on <paramref name="lastWatchDate"/>.</param>
    /// <param name="today">Today's date.</param>
    /// <param name="adIsReady">Whether <see cref="IAdService.IsRewardedAdReady"/> says a video is loaded.</param>
    public static ChipRescueStatus GetStatus(
        decimal balance,
        DateOnly? lastWatchDate,
        int watchesOnThatDate,
        DateOnly today,
        bool adIsReady)
    {
        var usedToday = WatchesUsedOn(lastWatchDate, watchesOnThatDate, today);
        var remaining = Math.Max(0, MaxWatchesPerDay - usedToday);

        return new ChipRescueStatus(
            CanWatch: IsBustedOut(balance) && remaining > 0 && adIsReady,
            WatchesUsedToday: usedToday,
            WatchesRemainingToday: remaining,
            Reward: RewardPerAd);
    }

    /// <summary>
    /// The date and count to persist after a video has actually been watched
    /// through to its reward. Rolls the counter over when the stored date is
    /// not today.
    /// </summary>
    public static (DateOnly Date, int Count) RecordWatch(
        DateOnly? lastWatchDate,
        int watchesOnThatDate,
        DateOnly today) =>
        (today, WatchesUsedOn(lastWatchDate, watchesOnThatDate, today) + 1);

    /// <summary>
    /// How many count against today. A stored count only applies if it was
    /// stored today; any other date - including one in the future, which means
    /// the device clock moved backwards - starts today at zero. Erring toward
    /// zero here hands out at most one extra day of rescues to someone
    /// fiddling with their clock, where erring the other way would lock out an
    /// honest player whose clock is simply wrong.
    /// </summary>
    private static int WatchesUsedOn(DateOnly? lastWatchDate, int watchesOnThatDate, DateOnly today) =>
        lastWatchDate == today ? Math.Clamp(watchesOnThatDate, 0, MaxWatchesPerDay) : 0;
}
