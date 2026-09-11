using BlackjackApp.core.Models;
using BlackjackApp.core.Variants;

namespace BlackjackApp.Tests;

public class WarBlackjackVariantTests
{
    private readonly WarBlackjackVariant _variant = new();

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
    public void DealWarCards_GivesOneCardEachFromTheShoe()
    {
        var deck = new Deck(numberOfDecks: 1, new Random(1));
        var playerHand = new Hand();
        var dealerHand = new Hand();

        _variant.DealWarCards(deck, playerHand, dealerHand);

        Assert.Multiple(() =>
        {
            Assert.That(playerHand.Cards, Has.Count.EqualTo(1));
            Assert.That(dealerHand.Cards, Has.Count.EqualTo(1));
        });
    }

    [Test]
    public void PlayerWinsWar_HigherCardWins()
    {
        var playerHand = HandOf((Suit.Spades, Rank.Queen));
        var dealerHand = HandOf((Suit.Hearts, Rank.Six));

        Assert.That(_variant.PlayerWinsWar(playerHand, dealerHand), Is.True);
    }

    [Test]
    public void PlayerWinsWar_AceCountsLow()
    {
        // Ace is low in War, so a 2 beats an Ace - unlike its usual
        // 11-or-1 treatment in blackjack scoring.
        var playerHand = HandOf((Suit.Spades, Rank.Ace));
        var dealerHand = HandOf((Suit.Hearts, Rank.Two));

        Assert.That(_variant.PlayerWinsWar(playerHand, dealerHand), Is.False);
    }

    [Test]
    public void PlayerWinsWar_DealerWinsTies()
    {
        var playerHand = HandOf((Suit.Spades, Rank.Eight));
        var dealerHand = HandOf((Suit.Hearts, Rank.Eight));

        Assert.That(_variant.PlayerWinsWar(playerHand, dealerHand), Is.False);
    }

    [Test]
    public void ResolveWarPayout_PaysEvenMoneyOnWin_LosesFullBetOnLossOrTie()
    {
        var win = _variant.ResolveWarPayout(HandOf((Suit.Spades, Rank.King)), HandOf((Suit.Hearts, Rank.Six)), warBet: 10m);
        var loss = _variant.ResolveWarPayout(HandOf((Suit.Spades, Rank.Six)), HandOf((Suit.Hearts, Rank.King)), warBet: 10m);
        var tie = _variant.ResolveWarPayout(HandOf((Suit.Spades, Rank.Eight)), HandOf((Suit.Hearts, Rank.Eight)), warBet: 10m);

        Assert.Multiple(() =>
        {
            Assert.That(win, Is.EqualTo(10m));
            Assert.That(loss, Is.EqualTo(-10m));
            Assert.That(tie, Is.EqualTo(-10m)); // dealer wins ties
        });
    }

    [Test]
    public void DealSecondCards_CompletesEachHandToTwoCards()
    {
        var deck = new Deck(numberOfDecks: 1, new Random(1));
        var playerHand = HandOf((Suit.Spades, Rank.Queen));
        var dealerHand = HandOf((Suit.Hearts, Rank.Six));

        _variant.DealSecondCards(deck, playerHand, dealerHand);

        Assert.Multiple(() =>
        {
            Assert.That(playerHand.Cards, Has.Count.EqualTo(2));
            Assert.That(dealerHand.Cards, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void DealInitialCards_DealsWarCardThenSecondCard_EndingWithTwoCardsEach()
    {
        var deck = new Deck(numberOfDecks: 1, new Random(1));
        var playerHand = new Hand();
        var dealerHand = new Hand();

        _variant.DealInitialCards(deck, playerHand, dealerHand);

        Assert.Multiple(() =>
        {
            Assert.That(playerHand.Cards, Has.Count.EqualTo(2));
            Assert.That(dealerHand.Cards, Has.Count.EqualTo(2));
        });
    }

    [Test]
    public void BlackjackStage_UsesStandardBlackjackHouseRules()
    {
        // Dealer hits soft 17 - same rule as Standard, confirming War's
        // blackjack portion delegates to the same house rules rather than
        // reimplementing them.
        var dealerHand = HandOf((Suit.Spades, Rank.Ace), (Suit.Hearts, Rank.Six));
        var deck = new Deck(numberOfDecks: 1, new Random(1));

        _variant.PlayDealerHand(deck, dealerHand);

        Assert.That(dealerHand.Cards, Has.Count.GreaterThan(2));
    }

    [Test]
    public void DealDealerOpeningHand_GivesTheDealerExactlyTwoCards()
    {
        // The dealer's War card plus their second blackjack card, dealt
        // together and shared across every player hand in a multi-hand round.
        var deck = new Deck(numberOfDecks: 1, new Random(1));
        var dealerHand = new Hand();

        _variant.DealDealerOpeningHand(deck, dealerHand);

        Assert.That(dealerHand.Cards, Has.Count.EqualTo(2));
    }

    [Test]
    public void DealPlayerOpeningHand_GivesOnePlayerHandExactlyTwoCards()
    {
        // This hand's own War card plus its second blackjack card - called
        // once per active hand slot, separately from DealDealerOpeningHand.
        var deck = new Deck(numberOfDecks: 1, new Random(1));
        var playerHand = new Hand();

        _variant.DealPlayerOpeningHand(deck, playerHand);

        Assert.That(playerHand.Cards, Has.Count.EqualTo(2));
    }

    [Test]
    public void DealDealerWarCard_GivesTheDealerExactlyOneCard()
    {
        var deck = new Deck(numberOfDecks: 1, new Random(1));
        var dealerHand = new Hand();

        _variant.DealDealerWarCard(deck, dealerHand);

        Assert.That(dealerHand.Cards, Has.Count.EqualTo(1));
    }

    [Test]
    public void DealPlayerWarCard_GivesOnePlayerHandExactlyOneCard()
    {
        var deck = new Deck(numberOfDecks: 1, new Random(1));
        var playerHand = new Hand();

        _variant.DealPlayerWarCard(deck, playerHand);

        Assert.That(playerHand.Cards, Has.Count.EqualTo(1));
    }

    [Test]
    public void DealDealerSecondCard_AddsExactlyOneCardToAnExistingWarCard()
    {
        var deck = new Deck(numberOfDecks: 1, new Random(1));
        var dealerHand = HandOf((Suit.Hearts, Rank.Six));

        _variant.DealDealerSecondCard(deck, dealerHand);

        Assert.That(dealerHand.Cards, Has.Count.EqualTo(2));
    }

    [Test]
    public void DealPlayerSecondCard_AddsExactlyOneCardToAnExistingWarCard()
    {
        var deck = new Deck(numberOfDecks: 1, new Random(1));
        var playerHand = HandOf((Suit.Spades, Rank.Queen));

        _variant.DealPlayerSecondCard(deck, playerHand);

        Assert.That(playerHand.Cards, Has.Count.EqualTo(2));
    }

    [Test]
    public void EndsTurnAfterDouble_DelegatesToStandardBlackjackRules()
    {
        Assert.That(_variant.EndsTurnAfterDouble, Is.True);
    }

    [Test]
    public void CanHit_DelegatesToStandardBlackjackRules()
    {
        var bustHand = HandOf((Suit.Spades, Rank.King), (Suit.Hearts, Rank.Queen), (Suit.Clubs, Rank.Five));

        Assert.That(_variant.CanHit(bustHand), Is.False);
    }

    [Test]
    public void ResolvePayout_BlackjackPaysThreeToTwo()
    {
        var playerHand = HandOf((Suit.Spades, Rank.Ace), (Suit.Hearts, Rank.King));
        var dealerHand = HandOf((Suit.Diamonds, Rank.Ten), (Suit.Clubs, Rank.Seven));

        var payout = _variant.ResolvePayout(playerHand, dealerHand, bet: 10m);

        Assert.That(payout, Is.EqualTo(15m));
    }
}
