using BlackjackApp.core.Models;
using BlackjackApp.core.Variants;

namespace BlackjackApp.Tests;

public class DoubleDownMadnessVariantTests
{
    private readonly DoubleDownMadnessVariant _variant = new();

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
    public void DealInitialCards_GivesPlayerOneCardAndDealerTwo()
    {
        var deck = new Deck(numberOfDecks: 1, new Random(1));
        var playerHand = new Hand();
        var dealerHand = new Hand();

        _variant.DealInitialCards(deck, playerHand, dealerHand);

        Assert.Multiple(() =>
        {
            Assert.That(playerHand.Cards, Has.Count.EqualTo(1));
            Assert.That(dealerHand.Cards, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void DealDealerOpeningHand_GivesTheDealerExactlyTwoCards()
    {
        var deck = new Deck(numberOfDecks: 1, new Random(1));
        var dealerHand = new Hand();

        _variant.DealDealerOpeningHand(deck, dealerHand);

        Assert.That(dealerHand.Cards, Has.Count.EqualTo(2));
    }

    [Test]
    public void DealPlayerOpeningHand_GivesOnePlayerHandExactlyOneCard()
    {
        // Multi-hand rounds call this once per active hand slot, separately
        // from DealDealerOpeningHand which is only called once per round.
        var deck = new Deck(numberOfDecks: 1, new Random(1));
        var playerHand = new Hand();

        _variant.DealPlayerOpeningHand(deck, playerHand);

        Assert.That(playerHand.Cards, Has.Count.EqualTo(1));
    }

    [Test]
    public void CanSplit_IsAlwaysFalse()
    {
        var pairOfEights = HandOf((Suit.Spades, Rank.Eight), (Suit.Hearts, Rank.Eight));

        Assert.That(_variant.CanSplit(pairOfEights), Is.False);
    }

    [Test]
    public void CanDoubleDown_TrueEvenWithSeveralCardsAlready()
    {
        // "Double down at virtually any time" - unlike Standard, a
        // multi-card hand can still double.
        var hand = HandOf((Suit.Spades, Rank.Five), (Suit.Hearts, Rank.Six), (Suit.Clubs, Rank.Two));

        Assert.That(_variant.CanDoubleDown(hand), Is.True);
    }

    [Test]
    public void CanDoubleDown_FalseAfterAceOpenerAlreadyReceivedItsOneCard()
    {
        var hand = HandOf((Suit.Spades, Rank.Ace), (Suit.Hearts, Rank.King));

        Assert.That(_variant.CanDoubleDown(hand), Is.False);
    }

    [Test]
    public void CanHit_TrueJustAfterAnAceOpener_FalseOnceItsOneFollowUpCardArrives()
    {
        var justTheAce = HandOf((Suit.Spades, Rank.Ace));
        var aceThenOneCard = HandOf((Suit.Spades, Rank.Ace), (Suit.Hearts, Rank.Nine));

        Assert.Multiple(() =>
        {
            Assert.That(_variant.CanHit(justTheAce), Is.True);
            Assert.That(_variant.CanHit(aceThenOneCard), Is.False);
        });
    }

    [Test]
    public void CanHit_TrueForNonAceOpenerRegardlessOfCardCount()
    {
        var hand = HandOf((Suit.Clubs, Rank.Ten), (Suit.Diamonds, Rank.Seven), (Suit.Hearts, Rank.Three));

        Assert.That(_variant.CanHit(hand), Is.True);
    }

    [Test]
    public void PlayDealerHand_HitsOnSoftSeventeen()
    {
        var dealerHand = HandOf((Suit.Spades, Rank.Ace), (Suit.Hearts, Rank.Six));
        var deck = new Deck(numberOfDecks: 1, new Random(1));

        _variant.PlayDealerHand(deck, dealerHand);

        Assert.That(dealerHand.Cards, Has.Count.GreaterThan(2));
    }

    [Test]
    public void DetermineOutcome_DealerTwentyTwoExactly_IsPushEvenIfPlayerHasLowerTotal()
    {
        var playerHand = HandOf((Suit.Spades, Rank.Ten), (Suit.Hearts, Rank.Eight)); // 18
        var dealerHand = HandOf((Suit.Diamonds, Rank.King), (Suit.Clubs, Rank.Queen), (Suit.Spades, Rank.Two)); // 22

        Assert.That(_variant.DetermineOutcome(playerHand, dealerHand), Is.EqualTo(RoundOutcome.Push));
    }

    [Test]
    public void DetermineOutcome_DealerOverTwentyTwo_IsGenuineBust()
    {
        var playerHand = HandOf((Suit.Spades, Rank.Ten), (Suit.Hearts, Rank.Eight)); // 18
        var dealerHand = HandOf((Suit.Diamonds, Rank.King), (Suit.Clubs, Rank.Queen), (Suit.Spades, Rank.Five)); // 25

        Assert.That(_variant.DetermineOutcome(playerHand, dealerHand), Is.EqualTo(RoundOutcome.DealerBust));
    }

    [Test]
    public void DetermineOutcome_TwoCardTwentyOne_IsBlackjack()
    {
        var playerHand = HandOf((Suit.Spades, Rank.Ace), (Suit.Hearts, Rank.King));
        var dealerHand = HandOf((Suit.Diamonds, Rank.Ten), (Suit.Clubs, Rank.Seven));

        Assert.That(_variant.DetermineOutcome(playerHand, dealerHand), Is.EqualTo(RoundOutcome.PlayerBlackjack));
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
            HandOf((Suit.Diamonds, Rank.Ten), (Suit.Clubs, Rank.Seven))),
        RoundOutcome.PlayerWin => (
            HandOf((Suit.Spades, Rank.Ten), (Suit.Hearts, Rank.Nine)),
            HandOf((Suit.Diamonds, Rank.Ten), (Suit.Clubs, Rank.Seven))),
        RoundOutcome.DealerBust => (
            HandOf((Suit.Spades, Rank.Ten), (Suit.Hearts, Rank.Seven)),
            HandOf((Suit.Diamonds, Rank.King), (Suit.Clubs, Rank.Queen), (Suit.Spades, Rank.Five))), // 25 - genuine bust
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
