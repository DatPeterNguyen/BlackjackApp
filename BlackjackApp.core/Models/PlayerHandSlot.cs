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
}
