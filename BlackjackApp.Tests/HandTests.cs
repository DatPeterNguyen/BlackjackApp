using BlackjackApp.core.Models;

namespace BlackjackApp.Tests;

public class HandTests
{
    [Test]
    public void TakeSecondCardForSplit_RemovesAndReturnsTheSecondCardOnly()
    {
        var hand = new Hand();
        hand.AddCard(new Card(Suit.Spades, Rank.Eight));
        hand.AddCard(new Card(Suit.Hearts, Rank.Eight));

        var removed = hand.TakeSecondCardForSplit();

        Assert.Multiple(() =>
        {
            Assert.That(removed.Suit, Is.EqualTo(Suit.Hearts));
            Assert.That(removed.Rank, Is.EqualTo(Rank.Eight));
            Assert.That(hand.Cards, Has.Count.EqualTo(1));
            Assert.That(hand.Cards[0].Suit, Is.EqualTo(Suit.Spades));
        });
    }
}
