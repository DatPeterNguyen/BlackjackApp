using BlackjackApp.core.Economy;

namespace BlackjackApp.Tests;

public class ChipWalletTests
{
    [Test]
    public void Balance_StartsAtTheGivenAmount()
    {
        var wallet = new ChipWallet(1000m);

        Assert.That(wallet.Balance, Is.EqualTo(1000m));
    }

    [Test]
    public void Add_IncreasesBalance()
    {
        var wallet = new ChipWallet(500m);

        wallet.Add(250m);

        Assert.That(wallet.Balance, Is.EqualTo(750m));
    }

    [Test]
    public void TryDeduct_SucceedsAndReducesBalance_WhenAffordable()
    {
        var wallet = new ChipWallet(500m);

        var succeeded = wallet.TryDeduct(200m);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.True);
            Assert.That(wallet.Balance, Is.EqualTo(300m));
        });
    }

    [Test]
    public void TryDeduct_FailsAndLeavesBalanceUnchanged_WhenNotAffordable()
    {
        var wallet = new ChipWallet(100m);

        var succeeded = wallet.TryDeduct(150m);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.False);
            Assert.That(wallet.Balance, Is.EqualTo(100m));
        });
    }

    [Test]
    public void TryDeduct_ExactBalance_Succeeds()
    {
        var wallet = new ChipWallet(100m);

        var succeeded = wallet.TryDeduct(100m);

        Assert.Multiple(() =>
        {
            Assert.That(succeeded, Is.True);
            Assert.That(wallet.Balance, Is.EqualTo(0m));
        });
    }

    [TestCase(1, true)]
    [TestCase(10_000, true)]
    [TestCase(5_000, true)]
    [TestCase(0.5, false)]
    [TestCase(0, false)]
    [TestCase(10_000.01, false)]
    [TestCase(50_000, false)]
    public void IsWithinTableLimits_EnforcesOneDollarToTenThousandDollarRange(decimal wager, bool expected)
    {
        Assert.That(ChipWallet.IsWithinTableLimits(wager), Is.EqualTo(expected));
    }

    [Test]
    public void RemainingRoomUnderMax_ReturnsDistanceToTableMaximum()
    {
        Assert.That(ChipWallet.RemainingRoomUnderMax(9_000m), Is.EqualTo(1_000m));
    }

    [Test]
    public void RemainingRoomUnderMax_NeverGoesNegativeOnceAlreadyOverMax()
    {
        // Double Down Madness can escalate a wager past the table max through
        // repeated doubling - room remaining should floor at zero, not go negative.
        Assert.That(ChipWallet.RemainingRoomUnderMax(15_000m), Is.EqualTo(0m));
    }

    [Test]
    public void Denominations_MatchTheDesignDocsChipSet()
    {
        Assert.That(ChipWallet.Denominations, Is.EqualTo(new[] { 1m, 5m, 10m, 25m, 100m, 1_000m, 10_000m }));
    }
}
