namespace BlackjackApp.core.Variants;

/// <summary>
/// How a single resolved hand turned out against the dealer, from the
/// player's perspective. Used to decide payout and to tell the UI what
/// message/animation to show.
/// </summary>
public enum RoundOutcome
{
    /// <summary>Player busted (over 21) - loses regardless of the dealer's hand.</summary>
    PlayerBust,

    /// <summary>Player drew a natural 21 (two cards) and the dealer didn't - pays 3:2.</summary>
    PlayerBlackjack,

    /// <summary>Dealer busted and the player didn't - player wins even money.</summary>
    DealerBust,

    /// <summary>Both hands total the same, or both have a natural blackjack - bet returned.</summary>
    Push,

    /// <summary>Neither side busted or blackjacked; player's total beat the dealer's.</summary>
    PlayerWin,

    /// <summary>Neither side busted or blackjacked; dealer's total beat the player's.</summary>
    DealerWin,
}
