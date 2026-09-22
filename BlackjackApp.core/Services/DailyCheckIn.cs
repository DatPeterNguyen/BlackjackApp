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
/// True when the player let the streak expire and this claim restarts the
/// cycle at day 1 - purely so the UI can say so. A brand new player (who has
/// never claimed) hasn't broken anything, so this stays false for them.
/// </param>
/// <param name="TimeUntilNextClaim">
/// How long until the next reward unlocks. <see cref="TimeSpan.Zero"/>
/// whenever <paramref name="CanClaim"/> is true, so a UI can show this
/// unconditionally and it simply reads as "now".
/// </param>
public readonly record struct CheckInStatus(
    bool CanClaim,
    int StreakDay,
    CheckInRewardKind Kind,
    bool StreakWasBroken,
    TimeSpan TimeUntilNextClaim);

/// <summary>
/// The daily check-in reward (item 14 on the polish list): $100 a day for
/// days 1-6 of a streak, and on day 7 a wheel spin worth $100 - $1,000.
///
/// Runs on a rolling 24-hour clock, per the design doc's "clock resets every
/// 24hrs", NOT on calendar days. The difference is real: on calendar days a
/// claim at 11:55pm and another at 12:05am are two claims ten minutes apart,
/// and the streak advances for both. Here the next reward unlocks exactly
/// <see cref="ClaimInterval"/> after the last one was taken, whenever that was.
///
/// The streak then survives up to <see cref="StreakExpiry"/> - claim any time
/// in that second 24-hour window and it advances; leave it longer and it
/// restarts at day 1. That window is what "consecutive days" has to become
/// once the gate is a rolling clock: without it, a player whose reward
/// unlocked while they were asleep would lose the streak for not claiming in
/// the same instant it became available.
///
/// Finishing day 7 starts a fresh cycle at day 1 rather than parking on 7.
///
/// Everything is in UTC, so crossing a timezone or a DST boundary can neither
/// hand out an extra reward nor lock anyone out.
///
/// Deliberately pure and clock-injected - every method takes "now" as an
/// argument rather than reading the clock, so the whole thing is testable
/// without faking time. Persisting the last claim timestamp and streak day is
/// the caller's job (on MAUI that's GameProgressStorage).
/// </summary>
public static class DailyCheckIn
{
    /// <summary>What days 1-6 of a streak pay.</summary>
    public const decimal DailyReward = 100m;

    /// <summary>How many days a full streak cycle runs before the wheel day.</summary>
    public const int StreakLength = 7;

    /// <summary>How long after a claim the next reward unlocks - the design doc's "clock resets every 24hrs".</summary>
    public static readonly TimeSpan ClaimInterval = TimeSpan.FromHours(24);

    /// <summary>
    /// How long after a claim the streak lapses. One extra ClaimInterval past
    /// the unlock, so there is a full 24-hour window to actually take the
    /// reward once it becomes available - see the class remarks.
    /// </summary>
    public static readonly TimeSpan StreakExpiry = TimeSpan.FromHours(48);

    /// <summary>The day-7 wheel's wedges, in the order they're drawn.</summary>
    public static readonly decimal[] WheelPrizes = [100m, 250m, 500m, 750m, 1_000m];

    /// <summary>
    /// Works out whether there's a reward to claim right now and which streak
    /// day it would be. lastClaimedUtc is null for a player who has never
    /// claimed. Both timestamps are UTC.
    /// </summary>
    public static CheckInStatus GetStatus(DateTime? lastClaimedUtc, int lastStreakDay, DateTime nowUtc)
    {
        // Never claimed - the first reward is waiting.
        if (lastClaimedUtc is not { } claimedAt)
        {
            return new CheckInStatus(CanClaim: true, StreakDay: 1, KindFor(1), StreakWasBroken: false, TimeSpan.Zero);
        }

        var elapsed = nowUtc - claimedAt;

        // Still inside the 24-hour gate. A negative elapsed means the device
        // clock moved backwards since the claim; the remaining time is clamped
        // to a single interval so the countdown can never show more than 24
        // hours, and so winding the clock back cannot lock the player out for
        // longer than waiting it out honestly would.
        if (elapsed < ClaimInterval)
        {
            var currentDay = Math.Clamp(lastStreakDay, 1, StreakLength);
            var remaining = ClaimInterval - elapsed;

            if (remaining > ClaimInterval)
            {
                remaining = ClaimInterval;
            }

            return new CheckInStatus(CanClaim: false, currentDay, KindFor(currentDay), StreakWasBroken: false, remaining);
        }

        // Past the gate. Whether the streak survives depends on how far past.
        var streakHeld = elapsed < StreakExpiry;

        // Claiming after a completed day 7 rolls into a brand new cycle -
        // that's a continuation, not a broken streak.
        var completedCycle = streakHeld && lastStreakDay >= StreakLength;

        var nextDay = streakHeld && !completedCycle
            ? Math.Clamp(lastStreakDay, 0, StreakLength - 1) + 1
            : 1;

        return new CheckInStatus(
            CanClaim: true,
            nextDay,
            KindFor(nextDay),
            StreakWasBroken: !streakHeld,
            TimeSpan.Zero);
    }

    /// <summary>
    /// How long until the next reward unlocks, for a UI ticking a countdown
    /// that should not re-read storage every second. Zero once it is claimable.
    /// </summary>
    public static TimeSpan TimeUntilNextClaim(DateTime? lastClaimedUtc, DateTime nowUtc)
    {
        if (lastClaimedUtc is not { } claimedAt)
        {
            return TimeSpan.Zero;
        }

        var remaining = ClaimInterval - (nowUtc - claimedAt);

        if (remaining <= TimeSpan.Zero)
        {
            return TimeSpan.Zero;
        }

        // Same backwards-clock clamp as GetStatus.
        return remaining > ClaimInterval ? ClaimInterval : remaining;
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
