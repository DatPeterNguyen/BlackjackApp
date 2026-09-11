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

    /// <summary>Deals just the dealer's single shared War card - call once per round, compared against every player hand's own War card.</summary>
    public void DealDealerWarCard(Deck deck, Hand dealerHand) => dealerHand.AddCard(deck.Draw());

    /// <summary>Deals one player hand's own War card - call once per active hand slot.</summary>
    public void DealPlayerWarCard(Deck deck, Hand playerHand) => playerHand.AddCard(deck.Draw());

    /// <summary>Deals the single "War" card each to player and dealer that the War side bet resolves against (single-hand convenience wrapper around DealPlayerWarCard/DealDealerWarCard).</summary>
    public void DealWarCards(Deck deck, Hand playerHand, Hand dealerHand)
    {
        DealPlayerWarCard(deck, playerHand);
        DealDealerWarCard(deck, dealerHand);
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

    /// <summary>Deals just the dealer's second (blackjack) card - call once the War bet has been resolved for every hand.</summary>
    public void DealDealerSecondCard(Deck deck, Hand dealerHand) => dealerHand.AddCard(deck.Draw());

    /// <summary>Deals one player hand's second (blackjack) card - call once per active hand slot, once the War bet has been resolved.</summary>
    public void DealPlayerSecondCard(Deck deck, Hand playerHand) => playerHand.AddCard(deck.Draw());

    /// <summary>Completes each hand to a normal two-card blackjack hand - call once the War bet has been resolved (single-hand convenience wrapper).</summary>
    public void DealSecondCards(Deck deck, Hand playerHand, Hand dealerHand)
    {
        DealPlayerSecondCard(deck, playerHand);
        DealDealerSecondCard(deck, dealerHand);
    }

    /// <summary>
    /// Satisfies IGameVariant for a caller that just wants "deal a full
    /// hand" with no War decision point in between - deals the War card
    /// and the second blackjack card back-to-back, with the War bet
    /// resolved automatically (no press-or-cash-out choice). Real table
    /// play (see MainPage's War-specific flow) instead calls
    /// DealDealerWarCard/DealPlayerWarCard, resolves the War bet - letting
    /// the player choose to cash out or press it into their blackjack
    /// wager - and only then calls DealDealerSecondCard/DealPlayerSecondCard.
    /// </summary>
    public void DealInitialCards(Deck deck, Hand playerHand, Hand dealerHand)
    {
        DealWarCards(deck, playerHand, dealerHand);
        DealSecondCards(deck, playerHand, dealerHand);
    }

    /// <summary>
    /// Deals just the dealer's opening hand for a multi-hand round: one
    /// shared War card plus the second blackjack card, dealt once per
    /// round. Every player hand's own War card (from DealPlayerOpeningHand)
    /// is compared against this same dealer hand's first card. This is the
    /// no-decision-point convenience path (see DealInitialCards); real
    /// table play deals these two cards separately, with a War decision in
    /// between - see DealDealerWarCard/DealDealerSecondCard.
    /// </summary>
    public void DealDealerOpeningHand(Deck deck, Hand dealerHand)
    {
        DealDealerWarCard(deck, dealerHand);
        DealDealerSecondCard(deck, dealerHand);
    }

    /// <summary>
    /// Deals one player hand's own War card plus its second blackjack card
    /// - call once per active hand slot. This is the no-decision-point
    /// convenience path; real table play deals these separately - see
    /// DealPlayerWarCard/DealPlayerSecondCard.
    /// </summary>
    public void DealPlayerOpeningHand(Deck deck, Hand playerHand)
    {
        DealPlayerWarCard(deck, playerHand);
        DealPlayerSecondCard(deck, playerHand);
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
