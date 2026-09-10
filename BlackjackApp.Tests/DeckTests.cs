using BlackjackApp.core.Models;

namespace BlackjackApp.Tests;

public class DeckTests
{
    [TestCase(1, 52)]
    [TestCase(4, 208)]
    [TestCase(6, 312)]
    public void BuildAndShuffle_CreatesCorrectCardCount(int numberOfDecks, int expectedCardCount)
    {
        var deck = new Deck(numberOfDecks, new Random(1));

        Assert.That(deck.CardsRemaining, Is.EqualTo(expectedCardCount));
    }

    [Test]
    public void SingleDeck_ContainsNoDuplicateCards()
    {
        var deck = new Deck(numberOfDecks: 1, new Random(42));
        var drawn = new List<Card>();

        while (deck.CardsRemaining > 0)
        {
            drawn.Add(deck.Draw());
        }

        Assert.That(drawn.Distinct().Count(), Is.EqualTo(52));
    }

    [Test]
    public void Draw_ReducesCardsRemainingByOne()
    {
        var deck = new Deck(numberOfDecks: 1, new Random(7));
        var before = deck.CardsRemaining;

        deck.Draw();

        Assert.That(deck.CardsRemaining, Is.EqualTo(before - 1));
    }

    [Test]
    public void Draw_OnEmptyShoe_Throws()
    {
        var deck = new Deck(numberOfDecks: 1, new Random(3));
        for (var i = 0; i < 52; i++)
        {
            deck.Draw();
        }

        Assert.Throws<InvalidOperationException>(() => deck.Draw());
    }

    [TestCase(0)]
    [TestCase(7)]
    public void Constructor_RejectsOutOfRangeDeckCount(int invalidDeckCount)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new Deck(invalidDeckCount));
    }
}
