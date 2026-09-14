using BlackjackApp.core.Economy;

namespace BlackjackApp.Tests;

public class GameStatsTests
{
    // An arbitrary fixed Wednesday, so week/month/year math in these tests
    // doesn't depend on when they happen to run.
    private static readonly DateOnly SomeWednesday = new(2026, 9, 9);

    [Test]
    public void NewStats_StartAtZero()
    {
        var stats = new GameStats();

        Assert.Multiple(() =>
        {
            Assert.That(stats.Wins, Is.EqualTo(0));
            Assert.That(stats.Losses, Is.EqualTo(0));
            Assert.That(stats.Pushes, Is.EqualTo(0));
            Assert.That(stats.HandsPlayed, Is.EqualTo(0));
            Assert.That(stats.NetProfit, Is.EqualTo(0));
            Assert.That(stats.BiggestWin, Is.EqualTo(0));
            Assert.That(stats.BiggestLoss, Is.EqualTo(0));
            Assert.That(stats.WeeklyNetProfit, Is.EqualTo(0));
            Assert.That(stats.MonthlyNetProfit, Is.EqualTo(0));
            Assert.That(stats.YearToDateNetProfit, Is.EqualTo(0));
        });
    }

    [Test]
    public void RecordHand_IncrementsTheMatchingCounterOnly()
    {
        var stats = new GameStats();

        stats.RecordHand(HandResult.Win);
        stats.RecordHand(HandResult.Win);
        stats.RecordHand(HandResult.Loss);
        stats.RecordHand(HandResult.Push);

        Assert.Multiple(() =>
        {
            Assert.That(stats.Wins, Is.EqualTo(2));
            Assert.That(stats.Losses, Is.EqualTo(1));
            Assert.That(stats.Pushes, Is.EqualTo(1));
            Assert.That(stats.HandsPlayed, Is.EqualTo(4));
        });
    }

    [Test]
    public void RecordRoundNet_AccumulatesLifetimeProfitAndTracksBiggestWinAndLoss()
    {
        var stats = new GameStats();

        stats.RecordRoundNet(50m, SomeWednesday);
        stats.RecordRoundNet(-20m, SomeWednesday);
        stats.RecordRoundNet(120m, SomeWednesday);
        stats.RecordRoundNet(-75m, SomeWednesday);

        Assert.Multiple(() =>
        {
            Assert.That(stats.NetProfit, Is.EqualTo(75m)); // 50 - 20 + 120 - 75
            Assert.That(stats.BiggestWin, Is.EqualTo(120m));
            Assert.That(stats.BiggestLoss, Is.EqualTo(75m));
        });
    }

    [Test]
    public void RecordRoundNet_OfZeroCountsTowardNetButNotEitherBiggestTracker()
    {
        var stats = new GameStats();

        stats.RecordRoundNet(0m, SomeWednesday);

        Assert.Multiple(() =>
        {
            Assert.That(stats.NetProfit, Is.EqualTo(0m));
            Assert.That(stats.BiggestWin, Is.EqualTo(0m));
            Assert.That(stats.BiggestLoss, Is.EqualTo(0m));
        });
    }

    [Test]
    public void RecordRoundNet_AccumulatesAllThreePeriodTotalsAlongsideLifetime()
    {
        var stats = new GameStats();

        stats.RecordRoundNet(30m, SomeWednesday);
        stats.RecordRoundNet(-10m, SomeWednesday);

        Assert.Multiple(() =>
        {
            Assert.That(stats.NetProfit, Is.EqualTo(20m));
            Assert.That(stats.WeeklyNetProfit, Is.EqualTo(20m));
            Assert.That(stats.MonthlyNetProfit, Is.EqualTo(20m));
            Assert.That(stats.YearToDateNetProfit, Is.EqualTo(20m));
        });
    }

    [Test]
    public void RecordRoundNet_RollsWeeklyTotalOverOnANewWeekButKeepsMonthlyYtdAndLifetime()
    {
        var stats = new GameStats();
        stats.RecordRoundNet(100m, SomeWednesday); // week of 2026-09-07 (Mon) - 2026-09-13 (Sun)

        stats.RecordRoundNet(40m, SomeWednesday.AddDays(7)); // the following Wednesday - a new week, same month

        Assert.Multiple(() =>
        {
            Assert.That(stats.WeeklyNetProfit, Is.EqualTo(40m), "new week should start fresh");
            Assert.That(stats.MonthlyNetProfit, Is.EqualTo(140m), "still the same month");
            Assert.That(stats.YearToDateNetProfit, Is.EqualTo(140m), "still the same year");
            Assert.That(stats.NetProfit, Is.EqualTo(140m), "lifetime never rolls over");
        });
    }

    [Test]
    public void RecordRoundNet_RollsMonthlyTotalOverOnANewMonthButKeepsYtdAndLifetime()
    {
        var stats = new GameStats();
        stats.RecordRoundNet(100m, new DateOnly(2026, 9, 30));

        stats.RecordRoundNet(40m, new DateOnly(2026, 10, 1));

        Assert.Multiple(() =>
        {
            Assert.That(stats.MonthlyNetProfit, Is.EqualTo(40m), "new month should start fresh");
            Assert.That(stats.YearToDateNetProfit, Is.EqualTo(140m), "still the same year");
            Assert.That(stats.NetProfit, Is.EqualTo(140m), "lifetime never rolls over");
        });
    }

    [Test]
    public void RecordRoundNet_RollsYearToDateTotalOverOnANewYearButKeepsLifetime()
    {
        var stats = new GameStats();
        stats.RecordRoundNet(100m, new DateOnly(2026, 12, 31));

        stats.RecordRoundNet(40m, new DateOnly(2027, 1, 1));

        Assert.Multiple(() =>
        {
            Assert.That(stats.YearToDateNetProfit, Is.EqualTo(40m), "new year should start fresh");
            Assert.That(stats.NetProfit, Is.EqualTo(140m), "lifetime never rolls over");
        });
    }

    [Test]
    public void RestoringConstructor_RehydratesEveryField()
    {
        var weekAnchor = new DateOnly(2026, 9, 7);
        var monthAnchor = new DateOnly(2026, 9, 1);
        var yearAnchor = new DateOnly(2026, 1, 1);

        var stats = new GameStats(
            wins: 5,
            losses: 3,
            pushes: 2,
            netProfit: 42m,
            biggestWin: 100m,
            biggestLoss: 60m,
            weeklyNetProfit: 15m,
            weekAnchor: weekAnchor,
            monthlyNetProfit: 25m,
            monthAnchor: monthAnchor,
            yearToDateNetProfit: 35m,
            yearAnchor: yearAnchor);

        Assert.Multiple(() =>
        {
            Assert.That(stats.Wins, Is.EqualTo(5));
            Assert.That(stats.Losses, Is.EqualTo(3));
            Assert.That(stats.Pushes, Is.EqualTo(2));
            Assert.That(stats.HandsPlayed, Is.EqualTo(10));
            Assert.That(stats.NetProfit, Is.EqualTo(42m));
            Assert.That(stats.BiggestWin, Is.EqualTo(100m));
            Assert.That(stats.BiggestLoss, Is.EqualTo(60m));
            Assert.That(stats.WeeklyNetProfit, Is.EqualTo(15m));
            Assert.That(stats.WeekAnchor, Is.EqualTo(weekAnchor));
            Assert.That(stats.MonthlyNetProfit, Is.EqualTo(25m));
            Assert.That(stats.MonthAnchor, Is.EqualTo(monthAnchor));
            Assert.That(stats.YearToDateNetProfit, Is.EqualTo(35m));
            Assert.That(stats.YearAnchor, Is.EqualTo(yearAnchor));
        });
    }

    [Test]
    public void Reset_ClearsEveryFieldBackToZero()
    {
        var stats = new GameStats(
            wins: 5,
            losses: 3,
            pushes: 2,
            netProfit: 42m,
            biggestWin: 100m,
            biggestLoss: 60m,
            weeklyNetProfit: 15m,
            weekAnchor: new DateOnly(2026, 9, 7),
            monthlyNetProfit: 25m,
            monthAnchor: new DateOnly(2026, 9, 1),
            yearToDateNetProfit: 35m,
            yearAnchor: new DateOnly(2026, 1, 1));

        stats.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(stats.Wins, Is.EqualTo(0));
            Assert.That(stats.Losses, Is.EqualTo(0));
            Assert.That(stats.Pushes, Is.EqualTo(0));
            Assert.That(stats.NetProfit, Is.EqualTo(0));
            Assert.That(stats.BiggestWin, Is.EqualTo(0));
            Assert.That(stats.BiggestLoss, Is.EqualTo(0));
            Assert.That(stats.WeeklyNetProfit, Is.EqualTo(0));
            Assert.That(stats.WeekAnchor, Is.EqualTo(default(DateOnly)));
            Assert.That(stats.MonthlyNetProfit, Is.EqualTo(0));
            Assert.That(stats.MonthAnchor, Is.EqualTo(default(DateOnly)));
            Assert.That(stats.YearToDateNetProfit, Is.EqualTo(0));
            Assert.That(stats.YearAnchor, Is.EqualTo(default(DateOnly)));
        });
    }
}
