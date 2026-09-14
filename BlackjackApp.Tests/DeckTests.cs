using System;
using System.Linq;
using BlackjackApp.core.Models;
using NUnit.Framework;

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

    [Test]
    public void Restore_RebuildsTheExactRemainingAndDiscardedCardsGiven()
    {
        // Restore is what lets a mid-round save (see InProgressRoundState
        // in BlackjackApp.Maui) come back exactly as it was left, rather
        // than as a fresh, differently-ordered shoe - so it has to
        // reproduce the exact remaining/discard lists given, not just the
        // right counts.
        var original = new Deck(numberOfDecks: 2, new Random(7));
        var played = new[] { original.Draw(), original.Draw(), original.Draw() };
        original.Discard(played);
        var remainingBefore = original.RemainingCardsSnapshot();
        var discardedBefore = original.DiscardedCardsSnapshot();

        var restored = Deck.Restore(original.NumberOfDecks, remainingBefore, discardedBefore);

        Assert.Multiple(() =>
        {
            Assert.That(restored.NumberOfDecks, Is.EqualTo(original.NumberOfDecks));
            Assert.That(restored.CardsRemaining, Is.EqualTo(remainingBefore.Count));
            Assert.That(restored.DiscardCount, Is.EqualTo(discardedBefore.Count));
            Assert.That(restored.RemainingCardsSnapshot(), Is.EqualTo(remainingBefore));
            Assert.That(restored.DiscardedCardsSnapshot(), Is.EqualTo(discardedBefore));
        });
    }

    [Test]
    public void Restore_DrawsCardsInTheSameOrderTheOriginalShoeWould()
    {
        // Not just the same cards - the same TOP-of-shoe order, so drawing
        // after a restore continues exactly where the original left off.
        var original = new Deck(numberOfDecks: 1, new Random(3));
        var expectedNextDraws = new[] { original.Draw(), original.Draw() };
        // Put those two back on top in the same order before snapshotting,
        // so Restore starts from a shoe that still has them next up.
        var remaining = original.RemainingCardsSnapshot()
            .Concat(expectedNextDraws)
            .ToList();

        var restored = Deck.Restore(original.NumberOfDecks, remaining, original.DiscardedCardsSnapshot());

        Assert.Multiple(() =>
        {
            Assert.That(restored.Draw(), Is.EqualTo(expectedNextDraws[1]));
            Assert.That(restored.Draw(), Is.EqualTo(expectedNextDraws[0]));
        });
    }
}
