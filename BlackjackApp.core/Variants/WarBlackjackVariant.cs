using BlackjackApp.core.Models;

namespace BlackjackApp.core.Variants;

/// <summary>
/// War Blackjack: a normal blackjack hand (dealer hits soft 17, blackjack
/// pays 3:2 - identical house rules to Standard Blackjack) with a separate
/// "War" side bet layered on top, per the design doc.
///
/// Flow at a real table:
///  1. Player places a blackjack bet AND a War bet.
///  2. Each side gets one card (DealWarCards) - the War bet resolves on
///     this single card: higher card wins, Ace counts LOW, and the dealer
///     wins ties (PlayerWinsWar / ResolveWarPayout).
///  3. The player then either cashes out their War winnings, or presses
///     them into the blackjack wager (a UI/bet-management decision, not
///     game-rules logic, so it isn't modelled here).
///  4. Each side gets its second card (DealSecondCards), completing a
///     normal two-card blackjack hand, and blackjack proceeds exactly like
///     Standard Blackjack from there - which is why this class delegates
///     all of the blackjack-stage IGameVariant members to an internal
///     StandardBlackjackVariant instead of reimplementing them.
/// </summary>
public class WarBlackjackVariant : IGameVariant
{
    private readonly StandardBlackjackVariant _blackjackRules = new();

    public string Name => "War Blackjack";

    /// <summary>Deals the single "War" card each to player and dealer that the War side bet resolves against.</summary>
    public void DealWarCards(Deck deck, Hand playerHand, Hand dealerHand)
    {
        playerHand.AddCard(deck.Draw());
        dealerHand.AddCard(deck.Draw());
    }

    /// <summary>
    /// True if the player's War card beats the dealer's. Ace counts LOW
    /// (per the doc), and the dealer wins ties.
    /// </summary>
    public bool PlayerWinsWar(Hand playerHand, Hand dealerHand) =>
        WarCardValue(playerHand.Cards[0].Rank) > WarCardValue(dealerHand.Cards[0].Rank);

    /// <summary>Net win/loss on the War side bet alone: pays 1:1, and the dealer wins ties.</summary>
    public decimal ResolveWarPayout(Hand playerHand, Hand dealerHand, decimal warBet) =>
        PlayerWinsWar(playerHand, dealerHand) ? warBet : -warBet;

    /// <summary>Ace is low in War (unlike its usual 11/1 value in blackjack scoring), face cards rank in their usual order.</summary>
    private static int WarCardValue(Rank rank) => rank switch
    {
        Rank.Ace => 1,
        Rank.Jack => 11,
        Rank.Queen => 12,
        Rank.King => 13,
        _ => (int)rank,
    };

    /// <summary>Completes each hand to a normal two-card blackjack hand - call once the War bet has been resolved.</summary>
    public void DealSecondCards(Deck deck, Hand playerHand, Hand dealerHand)
    {
        playerHand.AddCard(deck.Draw());
        dealerHand.AddCard(deck.Draw());
    }

    /// <summary>
    /// Satisfies IGameVariant for a caller that just wants "deal a full
    /// hand" with no War decision point in between - deals the War card
    /// and the second blackjack card back-to-back. Real table play should
    /// call DealWarCards, resolve the War bet (letting the player choose
    /// to cash out or press it into their blackjack wager), and then call
    /// DealSecondCards separately.
    /// </summary>
    public void DealInitialCards(Deck deck, Hand playerHand, Hand dealerHand)
    {
        DealWarCards(deck, playerHand, dealerHand);
        DealSecondCards(deck, playerHand, dealerHand);
    }

    public void Hit(Deck deck, Hand hand) => _blackjackRules.Hit(deck, hand);

    public bool CanHit(Hand hand) => _blackjackRules.CanHit(hand);

    public bool CanDoubleDown(Hand hand) => _blackjackRules.CanDoubleDown(hand);

    public bool EndsTurnAfterDouble => _blackjackRules.EndsTurnAfterDouble;

    public bool CanSplit(Hand hand) => _blackjackRules.CanSplit(hand);

    public void PlayDealerHand(Deck deck, Hand dealerHand) => _blackjackRules.PlayDealerHand(deck, dealerHand);

    public RoundOutcome DetermineOutcome(Hand playerHand, Hand dealerHand) =>
        _blackjackRules.DetermineOutcome(playerHand, dealerHand);

    public decimal ResolvePayout(Hand playerHand, Hand dealerHand, decimal bet) =>
        _blackjackRules.ResolvePayout(playerHand, dealerHand, bet);
}
