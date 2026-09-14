namespace BlackjackApp.core.Models;

/// <summary>
/// One of up to 5 simultaneous hands a player can have active in a single
/// round (per the design doc's 1-5 hand setting). Each slot has its own
/// cards and its own independently-sized bet, but every slot is played and
/// resolved against the SAME shared dealer hand - there's only ever one
/// dealer hand per round, regardless of how many player hand slots are active.
/// </summary>
public class PlayerHandSlot
{
    public Hand Hand { get; } = new();

    /// <summary>This hand's own bet, sized independently of every other active hand slot.</summary>
    public int Bet { get; set; }

    /// <summary>
    /// This hand's optional War side bet (War Blackjack only) - resolved
    /// against the shared dealer War card before the blackjack hand
    /// continues. Zero means this hand isn't playing the side bet at all.
    /// </summary>
    public int WarBet { get; set; }

    /// <summary>True once this hand is done acting for the round (busted, stood, doubled-and-done, or auto-resolved on a natural blackjack) and shouldn't receive further Hit/Stand/Double input.</summary>
    public bool IsFinished { get; set; }

    /// <summary>
    /// True for a hand that came from splitting a pair (on either half of
    /// the split), or one that has already been split once itself. Blocks
    /// splitting it again - re-splitting isn't offered, so a hand only ever
    /// splits one level deep.
    /// </summary>
    public bool HasBeenSplit { get; set; }

    /// <summary>Set once the round resolves - the outcome message for just this hand, shown in its own slot.</summary>
    public string ResultText { get; set; } = "";

    /// <summary>
    /// True once this hand's payout has already been settled early - a
    /// natural blackjack pays out the moment it's dealt, rather than
    /// waiting for every other hand and the dealer's own play to finish,
    /// as long as the dealer's up card ruled out a dealer blackjack of
    /// their own. EndRound skips any hand already marked this way instead
    /// of resolving (and paying) it a second time.
    /// </summary>
    public bool ResolvedEarly { get; set; }

    /// <summary>
    /// True for a natural blackjack that couldn't be paid the instant it was
    /// dealt because the dealer's up card could still turn into a dealer
    /// blackjack of their own (an Ace or a 10-value card) - the payout has
    /// to wait until the hole card is revealed at EndRound. Lets the UI show
    /// something other than a blank result while it waits (see
    /// MainPage.TryPayEarlyBlackjack/EndRound), so the deferral reads as
    /// "waiting on the dealer", not as the payout having silently vanished.
    /// </summary>
    public bool HasPendingBlackjack { get; set; }
}
