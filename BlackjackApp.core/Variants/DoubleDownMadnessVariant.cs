using BlackjackApp.core.Models;

namespace BlackjackApp.core.Variants;

/// <summary>
/// Black Double Down Madness, per the design doc's researched rules for the
/// real casino variant of this name. Very different shape from Standard
/// Blackjack:
///  - The player starts with only ONE card (not two); the dealer still
///    gets two.
///  - Doubling down is allowed at virtually any time and multiple times in
///    a row, with a hit allowed after each double.
///  - The one exception: if the player's very first card is an Ace, they
///    get exactly one more card and then the hand is locked - no further
///    hitting or doubling.
///  - Splitting isn't offered at all.
///  - Dealer hits on soft 17, same as Standard.
///  - The signature twist: a dealer total of exactly 22 is a push, not a
///    bust - active wagers are returned rather than paid. Anything higher
///    than 22 is still a genuine bust. This offsets the player's extra
///    doubling power.
///  - Blackjack still pays 3:2.
/// </summary>
public class DoubleDownMadnessVariant : IGameVariant
{
    public string Name => "Black Double Down Madness";

    /// <summary>Deals the opening cards: ONE card to the player, two to the dealer.</summary>
    public void DealInitialCards(Deck deck, Hand playerHand, Hand dealerHand)
    {
        playerHand.AddCard(deck.Draw());
        dealerHand.AddCard(deck.Draw());
        dealerHand.AddCard(deck.Draw());
    }

    /// <summary>Deals just the dealer's two-card opening hand - call once per round, shared across every player hand.</summary>
    public void DealDealerOpeningHand(Deck deck, Hand dealerHand)
    {
        dealerHand.AddCard(deck.Draw());
        dealerHand.AddCard(deck.Draw());
    }

    /// <summary>Deals just one player hand's ONE-card opening hand - call once per active hand slot.</summary>
    public void DealPlayerOpeningHand(Deck deck, Hand playerHand) => playerHand.AddCard(deck.Draw());

    public void Hit(Deck deck, Hand hand) => hand.AddCard(deck.Draw());

    /// <summary>
    /// Double down is allowed at virtually any time, including multiple
    /// times in a row - the only hand that can't double further is one
    /// that opened with an Ace and has already received its one allowed
    /// follow-up card.
    /// </summary>
    public bool CanDoubleDown(Hand hand) => CanContinue(hand);

    /// <summary>The same "opened with an Ace" lock applies to a plain hit, not just doubling.</summary>
    public bool CanHit(Hand hand) => CanContinue(hand);

    private static bool CanContinue(Hand hand)
    {
        if (hand.IsBust)
        {
            return false;
        }

        var openedWithAce = hand.Cards.Count > 0 && hand.Cards[0].Rank == Rank.Ace;
        return !(openedWithAce && hand.Cards.Count >= 2);
    }

    /// <summary>Doubling does NOT end the turn - "double multiple times" and "hit after doubling" are both allowed.</summary>
    public bool EndsTurnAfterDouble => false;

    /// <summary>No insurance in Double Down Madness - not part of its rule set.</summary>
    public bool OffersInsurance => false;

    /// <summary>Splitting isn't offered in this variant at all.</summary>
    public bool CanSplit(Hand hand) => false;

    /// <summary>Dealer hits on soft 17, same rule as Standard Blackjack.</summary>
    public void PlayDealerHand(Deck deck, Hand dealerHand)
    {
        while (true)
        {
            var (value, isSoft) = dealerHand.GetBestValue();
            if (value < 17 || (value == 17 && isSoft))
            {
                dealerHand.AddCard(deck.Draw());
            }
            else
            {
                break;
            }
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

        var dealerValue = dealerHand.GetBestValue().Value;

        // The signature Double Down Madness twist: dealer 22 exactly is a
        // push, not a bust.
        if (dealerValue == 22)
        {
            return RoundOutcome.Push;
        }

        if (dealerValue > 21)
        {
            return RoundOutcome.DealerBust;
        }

        var playerValue = playerHand.GetBestValue().Value;

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
