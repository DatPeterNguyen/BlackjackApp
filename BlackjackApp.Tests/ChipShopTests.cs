using System.Linq;
using BlackjackApp.core.Services;
using NUnit.Framework;

namespace BlackjackApp.Tests;

public class ChipShopTests
{
    [Test]
    public void TheShelfIsNotEmpty()
    {
        Assert.That(ChipShop.Bundles, Is.Not.Empty);
    }

    [Test]
    public void BundleIdsAreUnique()
    {
        // Ids are what a store product would eventually be keyed on, so a
        // duplicate would silently sell the wrong thing.
        var ids = ChipShop.Bundles.Select(b => b.Id).ToList();

        Assert.That(ids, Is.Unique);
    }

    [Test]
    public void BundlesAreListedCheapestFirst_AndMoreMoneyAlwaysBuysMoreChips()
    {
        var prices = ChipShop.Bundles.Select(b => b.PriceUsd).ToList();
        var chips = ChipShop.Bundles.Select(b => b.Chips).ToList();

        Assert.Multiple(() =>
        {
            Assert.That(prices, Is.Ordered.Ascending);
            Assert.That(chips, Is.Ordered.Ascending);
        });
    }

    [Test]
    public void ExactlyOneBundleIsBadgedBestValue()
    {
        Assert.That(ChipShop.Bundles.Count(b => b.IsBestValue), Is.EqualTo(1));
    }

    [Test]
    public void TheBestValueBadgeIsOnTheBundleThatActuallyIsTheBestValue()
    {
        // The point of this test. Shops routinely badge a middle tier while a
        // larger one beats it on chips per dollar; if anyone retunes these
        // prices, the badge has to move with them or come off.
        var badged = ChipShop.Bundles.Single(b => b.IsBestValue);
        var bestRate = ChipShop.Bundles.Max(ChipShop.ChipsPerDollar);

        Assert.That(ChipShop.ChipsPerDollar(badged), Is.EqualTo(bestRate));
    }

    [Test]
    public void EveryBundleCostsSomethingAndGrantsSomething()
    {
        Assert.Multiple(() =>
        {
            foreach (var bundle in ChipShop.Bundles)
            {
                Assert.That(bundle.PriceUsd, Is.GreaterThan(0m), bundle.Id);
                Assert.That(bundle.Chips, Is.GreaterThan(0m), bundle.Id);
                Assert.That(bundle.Name, Is.Not.Empty, bundle.Id);
            }
        });
    }
}
