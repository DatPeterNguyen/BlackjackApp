using System.Collections.Generic;

namespace BlackjackApp.core.Services;

/// <summary>
/// One purchasable bundle of chips, as shown in the shop.
/// </summary>
/// <param name="Id">Stable identifier. This is what a store product would eventually be keyed on, so it must not change once a bundle has shipped.</param>
/// <param name="Name">What the bundle is called on the shelf.</param>
/// <param name="Chips">How many chips it grants.</param>
/// <param name="PriceUsd">Indicative price in US dollars. See the remarks on <see cref="ChipShop"/> - this is a placeholder, not a real price.</param>
/// <param name="IsBestValue">Marks the one bundle worth pointing at. Exactly one bundle carries it.</param>
public readonly record struct ChipBundle(
    string Id,
    string Name,
    decimal Chips,
    decimal PriceUsd,
    bool IsBestValue);

/// <summary>
/// The shop's shelf: what a player would be able to buy, if buying were
/// switched on.
///
/// IT IS NOT. Nothing here can charge anyone anything -
/// <see cref="IChipPurchaseService"/> has no implementation, and the shop
/// page shows every bundle with its button disabled. This exists so the
/// screen, the catalogue and the wiring are in place and can be looked at
/// before any of the work that real purchasing actually needs: store
/// products registered with Apple and Google, receipt validation, restore,
/// refunds, tax, and the age rating that selling into a gambling-themed app
/// pulls in.
///
/// The prices are therefore indicative only. They are what the bundles might
/// plausibly cost, chosen to sit sensibly against the rest of the economy -
/// the smallest bundle is a couple of times the $1,000 starting stake, and
/// the largest is well beyond what the daily check-in or an ad rescue could
/// ever add up to. They are not agreed prices and nothing reads them but the
/// shop's own labels.
/// </summary>
public static class ChipShop
{
    /// <summary>The bundles on offer, cheapest first.</summary>
    public static readonly IReadOnlyList<ChipBundle> Bundles =
    [
        new("chips_2500",   "Pocket Change", 2_500m,   0.99m,  IsBestValue: false),
        new("chips_10000",  "Stack",         10_000m,  2.99m,  IsBestValue: false),
        new("chips_30000",  "Rack",          30_000m,  7.99m,  IsBestValue: false),
        new("chips_100000", "High Roller",   100_000m, 19.99m, IsBestValue: true),
    ];

    /// <summary>
    /// Chips per dollar, which is what <see cref="ChipBundle.IsBestValue"/>
    /// has to agree with.
    ///
    /// Mobile shops habitually badge a middle tier as the best value when a
    /// larger one plainly beats it on rate. That is a small lie, and a test
    /// asserts it is not told here: the flag sits on whichever bundle
    /// genuinely gives the most chips per dollar.
    /// </summary>
    public static decimal ChipsPerDollar(ChipBundle bundle) =>
        bundle.PriceUsd <= 0m ? 0m : bundle.Chips / bundle.PriceUsd;
}
