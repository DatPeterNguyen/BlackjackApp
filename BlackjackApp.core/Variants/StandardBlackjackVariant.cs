using BlackjackApp.core.Models;

namespace BlackjackApp.core.Variants;

/// <summary>
/// Standard Blackjack using the common Vegas defaults the doc's scope
/// settled on: dealer stands on all 17s (S17), blackjack pays 3:2, double
/// down allowed on any two cards, double after split allowed (DAS is
/// enforced by the caller letting CanDoubleDown apply post-split too - this
/// class only checks "does this hand qualify right now").
/// </summary>
public class StandardBlackjackVariant : IGameVariant
{
    public string Name => "Standard Blackjack";

    public void DealInitialCards(Deck deck, Hand playerHand, Hand dealerHand)
    {
        // Real casinos deal one card at a time, alternating player/dealer,
        // twice around - matters for card-counting realism, not for the
        // math, but it's cheap to do it the "real" way.
        playerHand.AddCard(deck.Draw());
        dealerHand.AddCard(deck.Draw());
        playerHand.AddCard(deck.Draw());
        dealerHand.AddCard(deck.Draw());
    }

    public void Hit(Deck deck, Hand hand) => hand.AddCard(deck.Draw());

    /// <summary>Double down is allowed on any first two cards (Vegas default), before any hit.</summary>
    public bool CanDoubleDown(Hand hand) => hand.Cards.Count == 2;

    /// <summary>
    /// Splittable if the hand is exactly two cards of equal point value
    /// (so 10/J/Q/K count as a pair, matching common casino "any 10-value"
    /// split rules, not just identical ranks).
    /// </summary>
    public bool CanSplit(Hand hand) =>
        hand.Cards.Count == 2 &&
        Hand.PointValue(hand.Cards[0].Rank) == Hand.PointValue(hand.Cards[1].Rank);

    /// <summary>Dealer hits on 16 or below, stands on 17 or above (hard or soft) - S17.</summary>
    public void PlayDealerHand(Deck deck, Hand dealerHand)
    {
        while (dealerHand.GetBestValue().Value < 17)
        {
            dealerHand.AddCard(deck.Draw());
        }
    }

    public RoundOutcome DetermineOutcome(Hand playerHand, Hand dealerHand)
    {
        if (playerHand.IsBust)
        {
            return RoundOutcome.PlayerBust;
        }

        if (playerHand.IsBlackjack)
        {
            return dealerHand.IsBlackjack ? RoundOutcome.Push : RoundOutcome.PlayerBlackjack;
        }

        if (dealerHand.IsBlackjack)
        {
            return RoundOutcome.DealerWin;
        }

        if (dealerHand.IsBust)
        {
            return RoundOutcome.DealerBust;
        }

        var playerValue = playerHand.GetBestValue().Value;
        var dealerValue = dealerHand.GetBestValue().Value;

        if (playerValue > dealerValue)
        {
            return RoundOutcome.PlayerWin;
        }

        return playerValue < dealerValue ? RoundOutcome.DealerWin : RoundOutcome.Push;
    }

    /// <summary>
    /// Net win/loss relative to the bet: blackjack pays 3:2, a plain win or
    /// a dealer bust pays 1:1, a push returns exactly the bet (net 0), and
    /// any loss is the full bet.
    /// </summary>
    public decimal ResolvePayout(Hand playerHand, Hand dealerHand, decimal bet) =>
        DetermineOutcome(playerHand, dealerHand) switch
        {
            RoundOutcome.PlayerBlackjack => bet * 1.5m,
            RoundOutcome.PlayerWin => bet,
            RoundOutcome.DealerBust => bet,
            RoundOutcome.Push => 0m,
            RoundOutcome.DealerWin => -bet,
            RoundOutcome.PlayerBust => -bet,
            _ => 0m,
        };
}
