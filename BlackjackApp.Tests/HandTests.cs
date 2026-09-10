using BlackjackApp.core.Models;

namespace BlackjackApp.Tests;

public class HandTests
{
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
    public void TwoCardTwentyOne_WithAceAndTen_IsBlackjack()
    {
        var hand = HandOf((Suit.Spades, Rank.Ace), (Suit.Hearts, Rank.King));

        Assert.Multiple(() =>
        {
            Assert.That(hand.GetBestValue().Value, Is.EqualTo(21));
            Assert.That(hand.IsBlackjack, Is.True);
            Assert.That(hand.IsBust, Is.False);
        });
    }

    [Test]
    public void ThreeCardTwentyOne_IsNotBlackjack()
    {
        // 7 + 7 + 7 = 21, but with three cards this is NOT a natural
        // blackjack and should not get the 3:2 payout treatment.
        var hand = HandOf((Suit.Clubs, Rank.Seven), (Suit.Diamonds, Rank.Seven), (Suit.Hearts, Rank.Seven));

        Assert.Multiple(() =>
        {
            Assert.That(hand.GetBestValue().Value, Is.EqualTo(21));
            Assert.That(hand.IsBlackjack, Is.False);
        });
    }

    [Test]
    public void TwoAces_CountsAsTwelveSoft()
    {
        // A-A: counting both as 11 gives 22 (bust), so one Ace must
        // downgrade to 1, giving 12. One Ace is still "soft" (counted as 11).
        var hand = HandOf((Suit.Spades, Rank.Ace), (Suit.Hearts, Rank.Ace));
        var (value, isSoft) = hand.GetBestValue();

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo(12));
            Assert.That(isSoft, Is.True);
            Assert.That(hand.IsBust, Is.False);
        });
    }

    [Test]
    public void FiveSixAce_DowngradesAceToAvoidBust()
    {
        // 5 + 6 = 11. Adding an Ace as 11 would make 22 (bust), so it
        // downgrades to 1, giving a hard 12 instead.
        var hand = HandOf((Suit.Clubs, Rank.Five), (Suit.Diamonds, Rank.Six), (Suit.Hearts, Rank.Ace));
        var (value, isSoft) = hand.GetBestValue();

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo(12));
            Assert.That(isSoft, Is.False);
            Assert.That(hand.IsBust, Is.False);
        });
    }

    [Test]
    public void FaceCardsCountAsTen()
    {
        var hand = HandOf((Suit.Spades, Rank.King), (Suit.Hearts, Rank.Queen), (Suit.Clubs, Rank.Jack));

        // K + Q + J = 30, well over 21 - confirms face cards are all
        // worth 10, not their ordinal enum values (13 + 12 + 11).
        Assert.Multiple(() =>
        {
            Assert.That(hand.GetBestValue().Value, Is.EqualTo(30));
            Assert.That(hand.IsBust, Is.True);
        });
    }

    [Test]
    public void HandOverTwentyOne_IsBust()
    {
        var hand = HandOf((Suit.Spades, Rank.King), (Suit.Hearts, Rank.Nine), (Suit.Clubs, Rank.Five));

        Assert.Multiple(() =>
        {
            Assert.That(hand.GetBestValue().Value, Is.EqualTo(24));
            Assert.That(hand.IsBust, Is.True);
            Assert.That(hand.IsBlackjack, Is.False);
        });
    }

    [Test]
    public void ThreeAcesAndAnEight_DowngradesAsManyAcesAsNeeded()
    {
        // A + A + A + 8: all Aces as 11 gives 8 + 33 = 41. Needs to downgrade
        // two Aces (41 - 20 = 21) to land on a valid, non-busted total, and
        // one Ace should remain soft.
        var hand = HandOf(
            (Suit.Spades, Rank.Ace), (Suit.Hearts, Rank.Ace),
            (Suit.Diamonds, Rank.Ace), (Suit.Clubs, Rank.Eight));
        var (value, isSoft) = hand.GetBestValue();

        Assert.Multiple(() =>
        {
            Assert.That(value, Is.EqualTo(21));
            Assert.That(isSoft, Is.True);
            Assert.That(hand.IsBust, Is.False);
            // Four cards, not two - even though it totals 21, it is not a
            // natural blackjack.
            Assert.That(hand.IsBlackjack, Is.False);
        });
    }
}
