using BlackjackApp.core.Models;

namespace BlackjackApp.Tests;

public class DeckTests
{
    [Test]
    public void NewDeck_StartsWithFullShoeAndEmptyDiscard()
    {
        var deck = new Deck(numberOfDecks: 1, new Random(1));

        Assert.Multiple(() =>
        {
            Assert.That(deck.CardsRemaining, Is.EqualTo(52));
            Assert.That(deck.DiscardCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void Discard_MovesCardsIntoTheDiscardPileWithoutTouchingTheShoe()
    {
        var deck = new Deck(numberOfDecks: 1, new Random(1));
        var played = new[] { deck.Draw(), deck.Draw(), deck.Draw() };

        deck.Discard(played);

        Assert.Multiple(() =>
        {
            Assert.That(deck.CardsRemaining, Is.EqualTo(49));
            Assert.That(deck.DiscardCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void NeedsReshuffle_IsTrueOnlyAtOrBelowTheCutCardThreshold()
    {
        var deck = new Deck(numberOfDecks: 1, new Random(1));

        Assert.That(deck.NeedsReshuffle(52), Is.True); // at threshold
        Assert.That(deck.NeedsReshuffle(51), Is.False); // still above it

        for (var i = 0; i < 10; i++)
        {
            deck.Draw();
        }

        Assert.Multiple(() =>
        {
            Assert.That(deck.NeedsReshuffle(42), Is.True);
            Assert.That(deck.NeedsReshuffle(41), Is.False);
        });
    }

    [Test]
    public void ReshuffleDiscardIntoShoe_MovesEveryDiscardedCardBackAndClearsTheDiscardPile()
    {
        var deck = new Deck(numberOfDecks: 1, new Random(1));
        var played = Enumerable.Range(0, 20).Select(_ => deck.Draw()).ToList();
        deck.Discard(played);

        Assert.That(deck.CardsRemaining, Is.EqualTo(32));

        deck.ReshuffleDiscardIntoShoe();

        Assert.Multiple(() =>
        {
            Assert.That(deck.CardsRemaining, Is.EqualTo(52));
            Assert.That(deck.DiscardCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void ReshuffleDiscardIntoShoe_NeverDuplicatesOrDropsCards()
    {
        // The reshuffled shoe should contain exactly the same 52 cards it
        // started with - nothing invented, nothing lost.
        var deck = new Deck(numberOfDecks: 1, new Random(1));
        var played = Enumerable.Range(0, 52).Select(_ => deck.Draw()).ToList();
        deck.Discard(played);
        deck.ReshuffleDiscardIntoShoe();

        var drawnAfterReshuffle = Enumerable.Range(0, 52).Select(_ => deck.Draw()).ToList();

        var expected = played.OrderBy(c => c.Suit).ThenBy(c => c.Rank).ToList();
        var actual = drawnAfterReshuffle.OrderBy(c => c.Suit).ThenBy(c => c.Rank).ToList();

        Assert.That(actual, Is.EqualTo(expected));
    }

    [Test]
    public void BuildAndShuffle_ClearsAnyExistingDiscardPile()
    {
        var deck = new Deck(numberOfDecks: 1, new Random(1));
        deck.Discard(new[] { deck.Draw(), deck.Draw() });

        deck.BuildAndShuffle();

        Assert.Multiple(() =>
        {
            Assert.That(deck.CardsRemaining, Is.EqualTo(52));
            Assert.That(deck.DiscardCount, Is.EqualTo(0));
        });
    }
}
