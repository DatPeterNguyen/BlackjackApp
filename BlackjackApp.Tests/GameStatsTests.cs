using BlackjackApp.core.Economy;

namespace BlackjackApp.Tests;

public class GameStatsTests
{
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

        stats.RecordRoundNet(50m);
        stats.RecordRoundNet(-20m);
        stats.RecordRoundNet(120m);
        stats.RecordRoundNet(-75m);

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

        stats.RecordRoundNet(0m);

        Assert.Multiple(() =>
        {
            Assert.That(stats.NetProfit, Is.EqualTo(0m));
            Assert.That(stats.BiggestWin, Is.EqualTo(0m));
            Assert.That(stats.BiggestLoss, Is.EqualTo(0m));
        });
    }

    [Test]
    public void RestoringConstructor_RehydratesEveryField()
    {
        var stats = new GameStats(wins: 5, losses: 3, pushes: 2, netProfit: 42m, biggestWin: 100m, biggestLoss: 60m);

        Assert.Multiple(() =>
        {
            Assert.That(stats.Wins, Is.EqualTo(5));
            Assert.That(stats.Losses, Is.EqualTo(3));
            Assert.That(stats.Pushes, Is.EqualTo(2));
            Assert.That(stats.HandsPlayed, Is.EqualTo(10));
            Assert.That(stats.NetProfit, Is.EqualTo(42m));
            Assert.That(stats.BiggestWin, Is.EqualTo(100m));
            Assert.That(stats.BiggestLoss, Is.EqualTo(60m));
        });
    }

    [Test]
    public void Reset_ClearsEveryFieldBackToZero()
    {
        var stats = new GameStats(wins: 5, losses: 3, pushes: 2, netProfit: 42m, biggestWin: 100m, biggestLoss: 60m);

        stats.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(stats.Wins, Is.EqualTo(0));
            Assert.That(stats.Losses, Is.EqualTo(0));
            Assert.That(stats.Pushes, Is.EqualTo(0));
            Assert.That(stats.NetProfit, Is.EqualTo(0));
            Assert.That(stats.BiggestWin, Is.EqualTo(0));
            Assert.That(stats.BiggestLoss, Is.EqualTo(0));
        });
    }
}
