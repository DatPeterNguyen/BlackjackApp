using BlackjackApp.core.Models;

namespace BlackjackApp.core.Variants;

// Contract every table variant (Standard, Double Down Madness, War) will
// implement. Standard Blackjack has a full implementation; the other two
// still throw NotImplementedException until their own rules get built.
public interface IGameVariant
{
    string Name { get; }

    /// <summary>Deals the opening two cards each to the player hand and the dealer hand, alternating, from the given shoe.</summary>
    void DealInitialCards(Deck deck, Hand playerHand, Hand dealerHand);

    /// <summary>Draws one card from the shoe into the given hand.</summary>
    void Hit(Deck deck, Hand hand);

    bool CanDoubleDown(Hand hand);

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
