using System.Threading;
using System.Threading.Tasks;

namespace BlackjackApp.core.Services;

/// <summary>
/// Buying chips for real money.
///
/// DELIBERATELY UNIMPLEMENTED. There is no class behind this interface, so
/// nothing in the app can charge anyone. The shop screen reads
/// <see cref="ChipShop.Bundles"/> for what to display and leaves every buy
/// button disabled; <see cref="IsAvailable"/> is what it asks, and with no
/// implementation the answer is always no.
///
/// Shaped now rather than later so the screen is built against something
/// real. Whatever eventually implements it has to deal with the parts that
/// are genuinely hard and are not started: products registered with App
/// Store Connect and Google Play, server-side receipt validation (a client
/// that believes its own receipt is a client that can be lied to), restoring
/// past purchases, refunds and chargebacks, and the age rating that selling
/// into a gambling-themed app pulls in.
///
/// The previous shape was a synchronous void PurchaseChips(decimal amountUsd),
/// which could not report success, failure, or cancellation - three things
/// every real payment flow has to distinguish between.
/// </summary>
public interface IChipPurchaseService
{
    /// <summary>
    /// Whether purchasing can be offered at all. False whenever there is no
    /// store behind this, which is currently always.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Starts the platform's purchase flow for one bundle and waits for it to
    /// settle.
    /// </summary>
    /// <returns>
    /// The chips to credit, or zero if the player cancelled, the payment
    /// failed, or the receipt did not validate. Callers must credit on a
    /// positive result only - never on the flow merely having finished.
    /// </returns>
    Task<decimal> PurchaseAsync(ChipBundle bundle, CancellationToken cancellationToken = default);
}
