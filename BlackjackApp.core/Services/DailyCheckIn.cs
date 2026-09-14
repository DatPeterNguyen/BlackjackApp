namespace BlackjackApp.core.Services;

/// <summary>How a particular streak day pays out.</summary>
public enum CheckInRewardKind
{
    /// <summary>Days 1-6: a flat <see cref="DailyCheckIn.DailyReward"/> payout, no interaction beyond claiming it.</summary>
    Flat,

    /// <summary>Day 7: the wheel spin, paying whichever of <see cref="DailyCheckIn.WheelPrizes"/> it lands on.</summary>
    WheelSpin,
}

/// <summary>
/// A snapshot of where the player stands on today's check-in, as returned by
/// <see cref="DailyCheckIn.GetStatus"/>.
/// </summary>
/// <param name="CanClaim">Whether there's an unclaimed reward waiting right now.</param>
/// <param name="StreakDay">
/// The 1-based day of the 7-day cycle. When CanClaim is true this is the day
/// about to be claimed; when it's false it's the day already claimed today.
/// </param>
/// <param name="Kind">Whether <see cref="StreakDay"/> is a flat payout or the wheel.</param>
/// <param name="StreakWasBroken">
/// True when the player missed at least one day and this claim restarts the
/// cycle at day 1 - purely so the UI can say so. A brand new player (who has
/// never claimed) hasn't broken anything, so this stays false for them.
/// </param>
public readonly record struct CheckInStatus(
    bool CanClaim,
    int StreakDay,
    CheckInRewardKind Kind,
    bool StreakWasBroken);

/// <summary>
/// The daily check-in reward (item 14 on the polish list): $100 a day for
/// days 1-6 of a streak, and on day 7 a wheel spin worth $100 - $1,000.
/// Claiming on consecutive calendar days advances the streak; missing a day
/// drops it back to day 1. Finishing day 7 starts a fresh cycle at day 1 the
/// next day rather than parking on 7 forever.
///
/// Deliberately pure and date-driven - every method takes "today" as an
/// argument rather than reading the clock, so the whole thing is testable
/// without faking time. Persisting the last claim date and streak day is the
/// caller's job (on MAUI that's GameProgressStorage).
/// </summary>
public static class DailyCheckIn
{
    /// <summary>What days 1-6 of a streak pay.</summary>
    public const decimal DailyReward = 100m;

    /// <summary>How many days a full streak cycle runs before the wheel day.</summary>
    public const int StreakLength = 7;

    /// <summary>The day-7 wheel's wedges, in the order they're drawn.</summary>
    public static readonly decimal[] WheelPrizes = [100m, 250m, 500m, 750m, 1_000m];

    /// <summary>
    /// Works out whether there's a reward to claim today and which streak day
    /// it would be. lastClaimed is null for a player who has never claimed.
    /// </summary>
    public static CheckInStatus GetStatus(DateOnly? lastClaimed, int lastStreakDay, DateOnly today)
    {
        // Already claimed today. (A last-claim date in the future means the
        // device clock moved backwards - treat it as claimed rather than
        // handing out a second reward for the same day.)
        if (lastClaimed is { } claimed && claimed >= today)
        {
            var currentDay = Math.Clamp(lastStreakDay, 1, StreakLength);
            return new CheckInStatus(CanClaim: false, currentDay, KindFor(currentDay), StreakWasBroken: false);
        }

        var isConsecutive = lastClaimed is { } previous && previous.AddDays(1) == today;

        // Claiming the day after a completed day 7 rolls into a brand new
        // cycle - that's a continuation, not a broken streak.
        var completedCycle = isConsecutive && lastStreakDay >= StreakLength;

        var nextDay = isConsecutive && !completedCycle
            ? Math.Clamp(lastStreakDay, 0, StreakLength - 1) + 1
            : 1;

        return new CheckInStatus(
            CanClaim: true,
            nextDay,
            KindFor(nextDay),
            StreakWasBroken: lastClaimed is not null && !isConsecutive);
    }

    /// <summary>Whether a given streak day is a flat payout or the day-7 wheel.</summary>
    public static CheckInRewardKind KindFor(int streakDay) =>
        streakDay >= StreakLength ? CheckInRewardKind.WheelSpin : CheckInRewardKind.Flat;

    /// <summary>
    /// Picks a winning wedge, returned as an index into <see cref="WheelPrizes"/>
    /// rather than an amount so the UI can animate the wheel onto that exact
    /// wedge before paying out what it landed on.
    /// </summary>
    public static int SpinWheel(Random random) => random.Next(WheelPrizes.Length);

    /// <summary>The amount a given wheel wedge pays. Out-of-range indexes clamp rather than throw.</summary>
    public static decimal PrizeAt(int wheelIndex) => WheelPrizes[Math.Clamp(wheelIndex, 0, WheelPrizes.Length - 1)];
}
