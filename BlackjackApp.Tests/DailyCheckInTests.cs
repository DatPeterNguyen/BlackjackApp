using BlackjackApp.core.Services;

namespace BlackjackApp.Tests;

public class DailyCheckInTests
{
    private static readonly DateOnly Today = new(2026, 9, 11);
    private static readonly DateOnly Yesterday = Today.AddDays(-1);

    [Test]
    public void BrandNewPlayer_CanClaimDayOne_AndHasNotBrokenAnything()
    {
        var status = DailyCheckIn.GetStatus(lastClaimed: null, lastStreakDay: 0, Today);

        Assert.Multiple(() =>
        {
            Assert.That(status.CanClaim, Is.True);
            Assert.That(status.StreakDay, Is.EqualTo(1));
            Assert.That(status.Kind, Is.EqualTo(CheckInRewardKind.Flat));
            Assert.That(status.StreakWasBroken, Is.False);
        });
    }

    [Test]
    public void AlreadyClaimedToday_CannotClaimAgain_AndReportsTheDayItClaimed()
    {
        var status = DailyCheckIn.GetStatus(lastClaimed: Today, lastStreakDay: 3, Today);

        Assert.Multiple(() =>
        {
            Assert.That(status.CanClaim, Is.False);
            Assert.That(status.StreakDay, Is.EqualTo(3));
            Assert.That(status.StreakWasBroken, Is.False);
        });
    }

    [Test]
    public void ClaimedYesterday_AdvancesToTheNextStreakDay()
    {
        var status = DailyCheckIn.GetStatus(lastClaimed: Yesterday, lastStreakDay: 3, Today);

        Assert.Multiple(() =>
        {
            Assert.That(status.CanClaim, Is.True);
            Assert.That(status.StreakDay, Is.EqualTo(4));
            Assert.That(status.Kind, Is.EqualTo(CheckInRewardKind.Flat));
            Assert.That(status.StreakWasBroken, Is.False);
        });
    }

    [Test]
    public void SixthDayRollsIntoTheDaySevenWheel()
    {
        var status = DailyCheckIn.GetStatus(lastClaimed: Yesterday, lastStreakDay: 6, Today);

        Assert.Multiple(() =>
        {
            Assert.That(status.StreakDay, Is.EqualTo(DailyCheckIn.StreakLength));
            Assert.That(status.Kind, Is.EqualTo(CheckInRewardKind.WheelSpin));
        });
    }

    [Test]
    public void TheDayAfterCompletingDaySeven_StartsAFreshCycleWithoutBreakingTheStreak()
    {
        var status = DailyCheckIn.GetStatus(lastClaimed: Yesterday, lastStreakDay: DailyCheckIn.StreakLength, Today);

        Assert.Multiple(() =>
        {
            Assert.That(status.CanClaim, Is.True);
            Assert.That(status.StreakDay, Is.EqualTo(1));
            Assert.That(status.Kind, Is.EqualTo(CheckInRewardKind.Flat));
            Assert.That(status.StreakWasBroken, Is.False);
        });
    }

    [Test]
    public void MissingADay_ResetsToDayOneAndFlagsTheBrokenStreak()
    {
        var status = DailyCheckIn.GetStatus(lastClaimed: Today.AddDays(-2), lastStreakDay: 5, Today);

        Assert.Multiple(() =>
        {
            Assert.That(status.CanClaim, Is.True);
            Assert.That(status.StreakDay, Is.EqualTo(1));
            Assert.That(status.StreakWasBroken, Is.True);
        });
    }

    [Test]
    public void AClaimDateInTheFuture_IsTreatedAsAlreadyClaimed()
    {
        // Device clock moved backwards - claiming again would hand out a
        // second reward for a day that was already paid.
        var status = DailyCheckIn.GetStatus(lastClaimed: Today.AddDays(3), lastStreakDay: 2, Today);

        Assert.That(status.CanClaim, Is.False);
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
        DateOnly? lastClaimed = null;
        var streakDay = 0;
        var seen = new List<int>();

        for (var offset = 0; offset < 8; offset++)
        {
            var day = Today.AddDays(offset);
            var status = DailyCheckIn.GetStatus(lastClaimed, streakDay, day);

            Assert.That(status.CanClaim, Is.True, $"offset {offset}");
            seen.Add(status.StreakDay);

            lastClaimed = day;
            streakDay = status.StreakDay;
        }

        Assert.That(seen, Is.EqualTo(new[] { 1, 2, 3, 4, 5, 6, 7, 1 }));
    }
}
