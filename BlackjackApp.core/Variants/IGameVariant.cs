using BlackjackApp.core.Models;

namespace BlackjackApp.core.Variants;

// Contract every table variant (Standard, Double Down Madness, War)
// implements, so the table UI can hold a single IGameVariant reference and
// stay entirely variant-agnostic - it never needs to know it's looking at
// Standard vs. Double Down Madness vs. War.
public interface IGameVariant
{
    string Name { get; }

    /// <summary>
    /// Deals each variant's own opening hand shape from the given shoe -
    /// this differs per variant (Standard: 2 cards each; Double Down
    /// Madness: 1 to the player, 2 to the dealer; War: a War card each
    /// plus, once that side bet resolves, a second blackjack card each).
    /// </summary>
    void DealInitialCards(Deck deck, Hand playerHand, Hand dealerHand);

    /// <summary>Draws one card from the shoe into the given hand.</summary>
    void Hit(Deck deck, Hand hand);

    /// <summary>Whether this hand may still be hit right now, per this variant's rules (e.g. Double Down Madness locks a hand after an Ace-opener's one follow-up card).</summary>
    bool CanHit(Hand hand);

    bool CanDoubleDown(Hand hand);

    /// <summary>
    /// Whether doubling down ends the player's turn immediately (Standard,
    /// War: yes - exactly one card, then done) or the player may keep
    /// acting afterward (Double Down Madness: doubling can repeat).
    /// </summary>
    bool EndsTurnAfterDouble { get; }

    bool CanSplit(Hand hand);

    /// <summary>Plays out the dealer's hand per this variant's house rules (e.g. hit until 17).</summary>
    void PlayDealerHand(Deck deck, Hand dealerHand);

    /// <summary>Determines the outcome of a finished player hand against the dealer's finished hand.</summary>
    RoundOutcome DetermineOutcome(Hand playerHand, Hand dealerHand);

    /// <summary>
    /// Net amount the player wins (positive) or loses (negative) on this
    /// hand relative to their bet. A push returns 0.
    /// </summary>
    decimal ResolvePayout(Hand playerHand, Hand dealerHand, decimal bet);
}
