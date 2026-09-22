using System;
using BlackjackApp.core.Services;
using NUnit.Framework;

namespace BlackjackApp.Tests;

public class ChipRescueTests
{
    private static readonly DateOnly Today = new(2026, 9, 18);

    [TestCase(0, true)]
    [TestCase(0.5, true)]
    [TestCase(1, false)]
    [TestCase(5, false)]
    public void IsBustedOut_OnlyWhenTheSmallestLegalBetIsOutOfReach(decimal balance, bool expected)
    {
        Assert.That(ChipRescue.IsBustedOut(balance), Is.EqualTo(expected));
    }

    [Test]
    public void GetStatus_OffersTheTrade_WhenBrokeWithAnAdReady()
    {
        var status = ChipRescue.GetStatus(0m, lastWatchDate: null, watchesOnThatDate: 0, Today, adIsReady: true);

        Assert.Multiple(() =>
        {
            Assert.That(status.CanWatch, Is.True);
            Assert.That(status.WatchesUsedToday, Is.Zero);
            Assert.That(status.WatchesRemainingToday, Is.EqualTo(ChipRescue.MaxWatchesPerDay));
            Assert.That(status.Reward, Is.EqualTo(ChipRescue.RewardPerAd));
        });
    }

    [Test]
    public void GetStatus_DoesNotOffer_WhenThePlayerCanStillBet()
    {
        // A player with chips can keep playing - there is nothing to rescue.
        var status = ChipRescue.GetStatus(1m, lastWatchDate: null, watchesOnThatDate: 0, Today, adIsReady: true);

        Assert.That(status.CanWatch, Is.False);
    }

    [Test]
    public void GetStatus_DoesNotOffer_WhenNoAdIsLoaded()
    {
        // Better to say nothing than to offer a button that stalls.
        var status = ChipRescue.GetStatus(0m, lastWatchDate: null, watchesOnThatDate: 0, Today, adIsReady: false);

        Assert.That(status.CanWatch, Is.False);
    }

    [Test]
    public void GetStatus_DoesNotOffer_OnceTheDailyCapIsReached()
    {
        var status = ChipRescue.GetStatus(
            0m, Today, ChipRescue.MaxWatchesPerDay, Today, adIsReady: true);

        Assert.Multiple(() =>
        {
            Assert.That(status.CanWatch, Is.False);
            Assert.That(status.WatchesRemainingToday, Is.Zero);
        });
    }

    [Test]
    public void GetStatus_AllowsTheLastWatchOfTheDay()
    {
        var status = ChipRescue.GetStatus(
            0m, Today, ChipRescue.MaxWatchesPerDay - 1, Today, adIsReady: true);

        Assert.Multiple(() =>
        {
            Assert.That(status.CanWatch, Is.True);
            Assert.That(status.WatchesRemainingToday, Is.EqualTo(1));
        });
    }

    [Test]
    public void GetStatus_ResetsTheCap_OnANewDay()
    {
        var yesterday = Today.AddDays(-1);

        var status = ChipRescue.GetStatus(
            0m, yesterday, ChipRescue.MaxWatchesPerDay, Today, adIsReady: true);

        Assert.Multiple(() =>
        {
            Assert.That(status.CanWatch, Is.True);
            Assert.That(status.WatchesUsedToday, Is.Zero);
            Assert.That(status.WatchesRemainingToday, Is.EqualTo(ChipRescue.MaxWatchesPerDay));
        });
    }

    [Test]
    public void GetStatus_TreatsAFutureStoredDateAsANewDay()
    {
        // The device clock moved backwards. Erring toward offering costs at
        // most an extra day of rescues; erring the other way would lock out a
        // player whose clock is simply wrong.
        var tomorrow = Today.AddDays(1);

        var status = ChipRescue.GetStatus(
            0m, tomorrow, ChipRescue.MaxWatchesPerDay, Today, adIsReady: true);

        Assert.That(status.CanWatch, Is.True);
    }

    [Test]
    public void GetStatus_ClampsAStoredCountAboveTheCap()
    {
        var status = ChipRescue.GetStatus(0m, Today, watchesOnThatDate: 99, Today, adIsReady: true);

        Assert.Multiple(() =>
        {
            Assert.That(status.WatchesUsedToday, Is.EqualTo(ChipRescue.MaxWatchesPerDay));
            Assert.That(status.WatchesRemainingToday, Is.Zero);
        });
    }

    [Test]
    public void RecordWatch_CountsUpWithinTheSameDay()
    {
        var (date, count) = ChipRescue.RecordWatch(Today, 1, Today);

        Assert.Multiple(() =>
        {
            Assert.That(date, Is.EqualTo(Today));
            Assert.That(count, Is.EqualTo(2));
        });
    }

    [Test]
    public void RecordWatch_StartsAtOne_OnANewDay()
    {
        var (date, count) = ChipRescue.RecordWatch(Today.AddDays(-1), ChipRescue.MaxWatchesPerDay, Today);

        Assert.Multiple(() =>
        {
            Assert.That(date, Is.EqualTo(Today));
            Assert.That(count, Is.EqualTo(1));
        });
    }

    [Test]
    public void RecordWatch_StartsAtOne_ForAPlayerWhoHasNeverWatchedOne()
    {
        var (date, count) = ChipRescue.RecordWatch(lastWatchDate: null, watchesOnThatDate: 0, Today);

        Assert.Multiple(() =>
        {
            Assert.That(date, Is.EqualTo(Today));
            Assert.That(count, Is.EqualTo(1));
        });
    }

    [Test]
    public void ADayOfRescues_PaysLessThanStartingOver()
    {
        // The cap exists so grinding ads is never better than simply resetting
        // progress, which hands back the $1,000 starting stake. If either
        // figure is retuned, this is the relationship that has to survive.
        var mostPerDay = ChipRescue.RewardPerAd * ChipRescue.MaxWatchesPerDay;

        Assert.That(mostPerDay, Is.LessThan(1_000m));
    }
}
