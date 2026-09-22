using System;
using System.Collections.Generic;
using BlackjackApp.core.Services;
using NUnit.Framework;

namespace BlackjackApp.Tests;

public class DailyCheckInTests
{
    /// <summary>An arbitrary but fixed instant. Deliberately mid-evening: under the old calendar-day rule a claim here and another twenty minutes later were two separate days, which is precisely what the 24-hour clock exists to stop.</summary>
    private static readonly DateTime Now = new(2026, 9, 11, 23, 50, 00, DateTimeKind.Utc);

    private static DateTime Ago(TimeSpan span) => Now - span;

    private static DateTime Ago(double hours) => Now - TimeSpan.FromHours(hours);

    [Test]
    public void BrandNewPlayer_CanClaimDayOne_AndHasNotBrokenAnything()
    {
        var status = DailyCheckIn.GetStatus(lastClaimedUtc: null, lastStreakDay: 0, Now);

        Assert.Multiple(() =>
        {
            Assert.That(status.CanClaim, Is.True);
            Assert.That(status.StreakDay, Is.EqualTo(1));
            Assert.That(status.Kind, Is.EqualTo(CheckInRewardKind.Flat));
            Assert.That(status.StreakWasBroken, Is.False);
            Assert.That(status.TimeUntilNextClaim, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void JustClaimed_CannotClaimAgain_AndCountsDownAFullDay()
    {
        var status = DailyCheckIn.GetStatus(Ago(TimeSpan.Zero), lastStreakDay: 3, Now);

        Assert.Multiple(() =>
        {
            Assert.That(status.CanClaim, Is.False);
            Assert.That(status.StreakDay, Is.EqualTo(3));
            Assert.That(status.TimeUntilNextClaim, Is.EqualTo(DailyCheckIn.ClaimInterval));
        });
    }

    [Test]
    public void TwentyMinutesLater_IsStillTheSameClaim()
    {
        // The whole point of the change. Under calendar days this crossed
        // midnight into "a new day" and paid out again.
        var status = DailyCheckIn.GetStatus(Ago(TimeSpan.FromMinutes(20)), lastStreakDay: 3, Now);

        Assert.Multiple(() =>
        {
            Assert.That(status.CanClaim, Is.False);
            Assert.That(status.TimeUntilNextClaim, Is.EqualTo(TimeSpan.FromHours(23) + TimeSpan.FromMinutes(40)));
        });
    }

    [Test]
    public void OneMinuteShortOfTheGate_StillCannotClaim()
    {
        var status = DailyCheckIn.GetStatus(
            Ago(DailyCheckIn.ClaimInterval - TimeSpan.FromMinutes(1)), lastStreakDay: 2, Now);

        Assert.Multiple(() =>
        {
            Assert.That(status.CanClaim, Is.False);
            Assert.That(status.TimeUntilNextClaim, Is.EqualTo(TimeSpan.FromMinutes(1)));
        });
    }

    [Test]
    public void ExactlyAtTheGate_Unlocks()
    {
        // The boundary is inclusive - at exactly 24 hours the reward is due.
        var status = DailyCheckIn.GetStatus(Ago(DailyCheckIn.ClaimInterval), lastStreakDay: 2, Now);

        Assert.Multiple(() =>
        {
            Assert.That(status.CanClaim, Is.True);
            Assert.That(status.StreakDay, Is.EqualTo(3));
            Assert.That(status.StreakWasBroken, Is.False);
            Assert.That(status.TimeUntilNextClaim, Is.EqualTo(TimeSpan.Zero));
        });
    }

    [Test]
    public void ClaimingLateInTheWindow_StillAdvancesTheStreak()
    {
        // 40 hours: past the gate, inside the expiry. This is the window that
        // stops a player losing a streak because the reward unlocked while
        // they were asleep.
        var status = DailyCheckIn.GetStatus(Ago(40), lastStreakDay: 3, Now);

        Assert.Multiple(() =>
        {
            Assert.That(status.CanClaim, Is.True);
            Assert.That(status.StreakDay, Is.EqualTo(4));
            Assert.That(status.StreakWasBroken, Is.False);
        });
    }

    [Test]
    public void OneMinuteShortOfExpiry_StillHoldsTheStreak()
    {
        var status = DailyCheckIn.GetStatus(
            Ago(DailyCheckIn.StreakExpiry - TimeSpan.FromMinutes(1)), lastStreakDay: 3, Now);

        Assert.Multiple(() =>
        {
            Assert.That(status.CanClaim, Is.True);
            Assert.That(status.StreakDay, Is.EqualTo(4));
            Assert.That(status.StreakWasBroken, Is.False);
        });
    }

    [Test]
    public void ExactlyAtExpiry_BreaksTheStreak()
    {
        var status = DailyCheckIn.GetStatus(Ago(DailyCheckIn.StreakExpiry), lastStreakDay: 5, Now);

        Assert.Multiple(() =>
        {
            Assert.That(status.CanClaim, Is.True);
            Assert.That(status.StreakDay, Is.EqualTo(1));
            Assert.That(status.StreakWasBroken, Is.True);
        });
    }

    [Test]
    public void ALongAbsence_RestartsAtDayOne()
    {
        var status = DailyCheckIn.GetStatus(Ago(TimeSpan.FromDays(30)), lastStreakDay: 6, Now);

        Assert.Multiple(() =>
        {
            Assert.That(status.CanClaim, Is.True);
            Assert.That(status.StreakDay, Is.EqualTo(1));
            Assert.That(status.StreakWasBroken, Is.True);
        });
    }

    [Test]
    public void DaySixClaim_UnlocksTheWheelNext()
    {
        var status = DailyCheckIn.GetStatus(Ago(25), lastStreakDay: 6, Now);

        Assert.Multiple(() =>
        {
            Assert.That(status.StreakDay, Is.EqualTo(DailyCheckIn.StreakLength));
            Assert.That(status.Kind, Is.EqualTo(CheckInRewardKind.WheelSpin));
        });
    }

    [Test]
    public void AfterTheWheel_AFreshCycleStartsWithoutCountingAsBroken()
    {
        var status = DailyCheckIn.GetStatus(Ago(25), lastStreakDay: DailyCheckIn.StreakLength, Now);

        Assert.Multiple(() =>
        {
            Assert.That(status.StreakDay, Is.EqualTo(1));
            Assert.That(status.Kind, Is.EqualTo(CheckInRewardKind.Flat));
            Assert.That(status.StreakWasBroken, Is.False, "completing a cycle is a continuation, not a lapse");
        });
    }

    [Test]
    public void AClockMovedBackwards_NeitherPaysOutTwiceNorLocksOutForLonger()
    {
        // A timestamp in the future. It must not unlock, and the countdown
        // must not exceed one interval however far ahead the clock was set.
        var status = DailyCheckIn.GetStatus(Now + TimeSpan.FromDays(3), lastStreakDay: 2, Now);

        Assert.Multiple(() =>
        {
            Assert.That(status.CanClaim, Is.False);
            Assert.That(status.TimeUntilNextClaim, Is.EqualTo(DailyCheckIn.ClaimInterval));
        });
    }

    [Test]
    public void TimeUntilNextClaim_MatchesTheStatus_AndIsZeroOnceDue()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DailyCheckIn.TimeUntilNextClaim(null, Now), Is.EqualTo(TimeSpan.Zero));
            Assert.That(DailyCheckIn.TimeUntilNextClaim(Ago(DailyCheckIn.ClaimInterval), Now), Is.EqualTo(TimeSpan.Zero));
            Assert.That(DailyCheckIn.TimeUntilNextClaim(Ago(100), Now), Is.EqualTo(TimeSpan.Zero));
            Assert.That(DailyCheckIn.TimeUntilNextClaim(Ago(6), Now), Is.EqualTo(TimeSpan.FromHours(18)));
        });
    }

    [Test]
    public void TheStreakWindowIsExactlyOneIntervalWiderThanTheGate()
    {
        // The relationship the whole design rests on. If either constant is
        // retuned, this is what has to survive.
        Assert.That(DailyCheckIn.StreakExpiry - DailyCheckIn.ClaimInterval,
            Is.EqualTo(DailyCheckIn.ClaimInterval));
    }

    [Test]
    public void KindFor_TreatsOnlyDaySevenAsTheWheel()
    {
        Assert.Multiple(() =>
        {
            for (var day = 1; day < DailyCheckIn.StreakLength; day++)
            {
                Assert.That(DailyCheckIn.KindFor(day), Is.EqualTo(CheckInRewardKind.Flat), $"day {day}");
            }

            Assert.That(DailyCheckIn.KindFor(DailyCheckIn.StreakLength), Is.EqualTo(CheckInRewardKind.WheelSpin));
        });
    }

    [Test]
    public void TheWheelHoldsTheFiveAgreedPrizes()
    {
        Assert.That(DailyCheckIn.WheelPrizes, Is.EqualTo(new[] { 100m, 250m, 500m, 750m, 1_000m }));
    }

    [Test]
    public void SpinWheel_AlwaysLandsOnARealWedge()
    {
        var random = new Random(12345);

        for (var i = 0; i < 500; i++)
        {
            var index = DailyCheckIn.SpinWheel(random);
            Assert.That(index, Is.InRange(0, DailyCheckIn.WheelPrizes.Length - 1));
        }
    }

    [Test]
    public void PrizeAt_ClampsRatherThanThrowingOnAnOutOfRangeWedge()
    {
        Assert.Multiple(() =>
        {
            Assert.That(DailyCheckIn.PrizeAt(0), Is.EqualTo(100m));
            Assert.That(DailyCheckIn.PrizeAt(4), Is.EqualTo(1_000m));
            Assert.That(DailyCheckIn.PrizeAt(-3), Is.EqualTo(100m));
            Assert.That(DailyCheckIn.PrizeAt(99), Is.EqualTo(1_000m));
        });
    }

    [Test]
    public void AFullSevenDayRun_WalksDayOneThroughTheWheelAndBackToDayOne()
    {
        DateTime? lastClaimed = null;
        var streakDay = 0;
        var seen = new List<int>();
        var clock = Now;

        for (var claim = 0; claim < 8; claim++)
        {
            var status = DailyCheckIn.GetStatus(lastClaimed, streakDay, clock);

            Assert.That(status.CanClaim, Is.True, $"claim {claim}");
            seen.Add(status.StreakDay);

            lastClaimed = clock;
            streakDay = status.StreakDay;

            // Claim promptly each time, a few minutes after it unlocks.
            clock += DailyCheckIn.ClaimInterval + TimeSpan.FromMinutes(5);
        }

        Assert.That(seen, Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6, 7, 1 }));
    }
}
