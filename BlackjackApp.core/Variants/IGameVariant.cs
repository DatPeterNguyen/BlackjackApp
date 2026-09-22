using System.Collections.Generic;
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
    /// The house rules this variant actually enforces, worded the way a real
    /// table has them printed on its felt - the UI paints these across the
    /// table (see MainPage's TableRulesDrawable).
    ///
    /// Lives on the variant rather than in the UI so the printing can't
    /// drift away from the rules the same class goes on to enforce: the
    /// design doc's reference photos print "INSURANCE PAYS 2 TO 1" on the
    /// Double Down Madness and War tables, but neither of those variants
    /// offers insurance here (OffersInsurance), so neither prints it.
    ///
    /// First line is the headline and is painted largest.
    /// </summary>
    IReadOnlyList<string> TableRules { get; }

    /// <summary>
    /// Deals each variant's own opening hand shape from the given shoe -
    /// this differs per variant (Standard: 2 cards each; Double Down
    /// Madness: 1 to the player, 2 to the dealer; War: a War card each
    /// plus, once that side bet resolves, a second blackjack card each).
    /// </summary>
    void DealInitialCards(Deck deck, Hand playerHand, Hand dealerHand);

    /// <summary>
    /// Deals ONLY the dealer's opening hand for this variant. Call this
    /// exactly once per round, regardless of how many player hands are in
    /// play (1-5) - there's always a single shared dealer hand that every
    /// player hand is played and resolved against.
    /// </summary>
    void DealDealerOpeningHand(Deck deck, Hand dealerHand);

    /// <summary>
    /// Deals ONLY one player hand's opening cards for this variant (2 cards
    /// for Standard/War, 1 for Double Down Madness). Call this once per
    /// active player hand slot (1-5), after DealDealerOpeningHand has
    /// already dealt the round's single shared dealer hand.
    /// </summary>
    void DealPlayerOpeningHand(Deck deck, Hand playerHand);

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

    /// <summary>
    /// Whether this variant offers the insurance side bet (up to half the
    /// original wager, paying 2:1 if the dealer's hole card completes a
    /// blackjack) whenever the dealer's up card is an Ace. Standard
    /// Blackjack only, per the design doc - War Blackjack already protects
    /// against a dealer blackjack via its own War side bet, and Double Down
    /// Madness's spec never mentions insurance at all.
    /// </summary>
    bool OffersInsurance { get; }

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
