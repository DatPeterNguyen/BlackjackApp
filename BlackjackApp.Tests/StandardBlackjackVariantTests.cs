using BlackjackApp.core.Models;
using BlackjackApp.core.Variants;

namespace BlackjackApp.Tests;

public class StandardBlackjackVariantTests
{
    private readonly StandardBlackjackVariant _variant = new();

    private static Hand HandOf(params (Suit Suit, Rank Rank)[] cards)
    {
        var hand = new Hand();
        foreach (var (suit, rank) in cards)
        {
            hand.AddCard(new Card(suit, rank));
        }

        return hand;
    }

    [Test]
    public void DealInitialCards_GivesTwoCardsEachFromTheShoe()
    {
        var deck = new Deck(numberOfDecks: 1, new Random(1));
        var playerHand = new Hand();
        var dealerHand = new Hand();
        var cardsBefore = deck.CardsRemaining;

        _variant.DealInitialCards(deck, playerHand, dealerHand);

        Assert.Multiple(() =>
        {
            Assert.That(playerHand.Cards, Has.Count.EqualTo(2));
            Assert.That(dealerHand.Cards, Has.Count.EqualTo(2));
            Assert.That(deck.CardsRemaining, Is.EqualTo(cardsBefore - 4));
        });
    }

    [Test]
    public void Hit_DrawsExactlyOneCardIntoTheHand()
    {
        var deck = new Deck(numberOfDecks: 1, new Random(1));
        var hand = new Hand();

        _variant.Hit(deck, hand);

        Assert.That(hand.Cards, Has.Count.EqualTo(1));
    }

    [Test]
    public void CanDoubleDown_TrueOnlyWithExactlyTwoCards()
    {
        var twoCardHand = HandOf((Suit.Spades, Rank.Five), (Suit.Hearts, Rank.Six));
        var threeCardHand = HandOf((Suit.Spades, Rank.Five), (Suit.Hearts, Rank.Six), (Suit.Clubs, Rank.Two));

        Assert.Multiple(() =>
        {
            Assert.That(_variant.CanDoubleDown(twoCardHand), Is.True);
            Assert.That(_variant.CanDoubleDown(threeCardHand), Is.False);
        });
    }

    [Test]
    public void CanSplit_TrueForMatchingRanks()
    {
        var pairOfEights = HandOf((Suit.Spades, Rank.Eight), (Suit.Hearts, Rank.Eight));

        Assert.That(_variant.CanSplit(pairOfEights), Is.True);
    }

    [Test]
    public void CanSplit_TrueForDifferentTenValueCards()
    {
        // King + Queen: different ranks, same 10-point value - should
        // still count as a splittable pair under common casino rules.
        var kingQueen = HandOf((Suit.Spades, Rank.King), (Suit.Hearts, Rank.Queen));

        Assert.That(_variant.CanSplit(kingQueen), Is.True);
    }

    [Test]
    public void CanSplit_FalseForNonMatchingValues()
    {
        var fiveNine = HandOf((Suit.Spades, Rank.Five), (Suit.Hearts, Rank.Nine));

        Assert.That(_variant.CanSplit(fiveNine), Is.False);
    }

    [Test]
    public void PlayDealerHand_StopsOnceReachingSeventeen()
    {
        // Dealer starts on 10 + 6 = 16, must hit at least once.
        var dealerHand = HandOf((Suit.Spades, Rank.Ten), (Suit.Hearts, Rank.Six));
        var deck = new Deck(numberOfDecks: 1, new Random(1));

        _variant.PlayDealerHand(deck, dealerHand);

        Assert.That(dealerHand.GetBestValue().Value, Is.GreaterThanOrEqualTo(17));
    }

    [Test]
    public void PlayDealerHand_StandsOnSoftSeventeen_DoesNotHit()
    {
        // Ace + 6 = soft 17. S17 rule: dealer stands, does not draw again.
        var dealerHand = HandOf((Suit.Spades, Rank.Ace), (Suit.Hearts, Rank.Six));
        var deck = new Deck(numberOfDecks: 1, new Random(1));
        var cardsBefore = deck.CardsRemaining;

        _variant.PlayDealerHand(deck, dealerHand);

        Assert.Multiple(() =>
        {
            Assert.That(dealerHand.Cards, Has.Count.EqualTo(2));
            Assert.That(deck.CardsRemaining, Is.EqualTo(cardsBefore));
        });
    }

    [Test]
    public void DetermineOutcome_PlayerBust_LosesEvenIfDealerAlsoBusted()
    {
        var playerHand = HandOf((Suit.Spades, Rank.King), (Suit.Hearts, Rank.Queen), (Suit.Clubs, Rank.Five));
        var dealerHand = HandOf((Suit.Diamonds, Rank.King), (Suit.Clubs, Rank.Queen), (Suit.Spades, Rank.Five));

        Assert.That(_variant.DetermineOutcome(playerHand, dealerHand), Is.EqualTo(RoundOutcome.PlayerBust));
    }

    [Test]
    public void DetermineOutcome_PlayerBlackjackBeatsDealerNonBlackjack()
    {
        var playerHand = HandOf((Suit.Spades, Rank.Ace), (Suit.Hearts, Rank.King));
        var dealerHand = HandOf((Suit.Diamonds, Rank.King), (Suit.Clubs, Rank.Nine));

        Assert.That(_variant.DetermineOutcome(playerHand, dealerHand), Is.EqualTo(RoundOutcome.PlayerBlackjack));
    }

    [Test]
    public void DetermineOutcome_BothBlackjack_IsPush()
    {
        var playerHand = HandOf((Suit.Spades, Rank.Ace), (Suit.Hearts, Rank.King));
        var dealerHand = HandOf((Suit.Diamonds, Rank.Ace), (Suit.Clubs, Rank.Queen));

        Assert.That(_variant.DetermineOutcome(playerHand, dealerHand), Is.EqualTo(RoundOutcome.Push));
    }

    [Test]
    public void DetermineOutcome_DealerBustsAndPlayerDidNot_PlayerWinsViaDealerBust()
    {
        var playerHand = HandOf((Suit.Spades, Rank.Ten), (Suit.Hearts, Rank.Seven));
        var dealerHand = HandOf((Suit.Diamonds, Rank.King), (Suit.Clubs, Rank.Queen), (Suit.Spades, Rank.Five));

        Assert.That(_variant.DetermineOutcome(playerHand, dealerHand), Is.EqualTo(RoundOutcome.DealerBust));
    }

    [Test]
    public void DetermineOutcome_HigherTotalWins()
    {
        var playerHand = HandOf((Suit.Spades, Rank.Ten), (Suit.Hearts, Rank.Nine));
        var dealerHand = HandOf((Suit.Diamonds, Rank.Ten), (Suit.Clubs, Rank.Seven));

        Assert.That(_variant.DetermineOutcome(playerHand, dealerHand), Is.EqualTo(RoundOutcome.PlayerWin));
    }

    [Test]
    public void DetermineOutcome_EqualTotals_IsPush()
    {
        var playerHand = HandOf((Suit.Spades, Rank.Ten), (Suit.Hearts, Rank.Eight));
        var dealerHand = HandOf((Suit.Diamonds, Rank.Nine), (Suit.Clubs, Rank.Nine));

        Assert.That(_variant.DetermineOutcome(playerHand, dealerHand), Is.EqualTo(RoundOutcome.Push));
    }

    [TestCase("PlayerBlackjack", 15.0)]
    [TestCase("PlayerWin", 10.0)]
    [TestCase("DealerBust", 10.0)]
    [TestCase("Push", 0.0)]
    [TestCase("DealerWin", -10.0)]
    [TestCase("PlayerBust", -10.0)]
    public void ResolvePayout_MatchesExpectedNetAmountForEachOutcome(string outcomeName, double expectedNet)
    {
        var (playerHand, dealerHand) = BuildHandsForOutcome(Enum.Parse<RoundOutcome>(outcomeName));

        var payout = _variant.ResolvePayout(playerHand, dealerHand, bet: 10m);

        Assert.That(payout, Is.EqualTo((decimal)expectedNet));
    }

    private static (Hand Player, Hand Dealer) BuildHandsForOutcome(RoundOutcome outcome) => outcome switch
    {
        RoundOutcome.PlayerBlackjack => (
            HandOf((Suit.Spades, Rank.Ace), (Suit.Hearts, Rank.King)),
            HandOf((Suit.Diamonds, Rank.King), (Suit.Clubs, Rank.Nine))),
        RoundOutcome.PlayerWin => (
            HandOf((Suit.Spades, Rank.Ten), (Suit.Hearts, Rank.Nine)),
            HandOf((Suit.Diamonds, Rank.Ten), (Suit.Clubs, Rank.Seven))),
        RoundOutcome.DealerBust => (
            HandOf((Suit.Spades, Rank.Ten), (Suit.Hearts, Rank.Seven)),
            HandOf((Suit.Diamonds, Rank.King), (Suit.Clubs, Rank.Queen), (Suit.Spades, Rank.Five))),
        RoundOutcome.Push => (
            HandOf((Suit.Spades, Rank.Ten), (Suit.Hearts, Rank.Eight)),
            HandOf((Suit.Diamonds, Rank.Nine), (Suit.Clubs, Rank.Nine))),
        RoundOutcome.DealerWin => (
            HandOf((Suit.Spades, Rank.Ten), (Suit.Hearts, Rank.Six)),
            HandOf((Suit.Diamonds, Rank.Ten), (Suit.Clubs, Rank.Eight))),
        RoundOutcome.PlayerBust => (
            HandOf((Suit.Spades, Rank.King), (Suit.Hearts, Rank.Queen), (Suit.Clubs, Rank.Five)),
            HandOf((Suit.Diamonds, Rank.Ten), (Suit.Clubs, Rank.Seven))),
        _ => throw new ArgumentOutOfRangeException(nameof(outcome)),
    };
}
