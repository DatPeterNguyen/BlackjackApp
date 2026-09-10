namespace BlackjackApp.core.Models;

/// <summary>
/// The shoe: 1-6 standard 52-card decks shuffled together. Cards are drawn
/// from the end of the internal list (treated as the "top" of the shoe) so
/// draws are O(1) instead of shifting the whole list on every card dealt.
/// </summary>

public class Deck
{
    private readonly List<Card> _cards = new(); // Empty list to store cards 
    private readonly Random _random; // Variable to store num for shuffling

    public int NumberOfDecks { get; } // Getter for number of decks.

    public int CardsRemaining => _cards.Count; // Looks at cards list and return how many is in it.

    public Deck(int numberOfDecks = 4, Random? random = null)
    {
        /*
         * Constructor when new deck is made, and new random generator that is null by default.
         */
        if (numberOfDecks is < 1 or > 6) // Safety check
        {
            throw new ArgumentOutOfRangeException(nameof(numberOfDecks), numberOfDecks,
                "Number of decks must be between 1 and 6, per the table settings.");
        }

        NumberOfDecks = numberOfDecks;
        _random = random ?? new Random(); // If null, make new one, otherwise use random generator.
        BuildAndShuffle(); // Generate cards
    }
    
    public void BuildAndShuffle()
    {
        /*
         * Clears the table, get new cards, and combine them into a large stack before shuffling them.
         */
        _cards.Clear(); // Deletes left over cards from previous round.

        // Adds new cards to deck
        for (var deckIndex = 0; deckIndex < NumberOfDecks; deckIndex++)
        {
            foreach (Suit suit in Enum.GetValues<Suit>())
            {
                foreach (Rank rank in Enum.GetValues<Rank>())
                {
                    _cards.Add(new Card(suit, rank));
                }
            }
        }

        Shuffle(); 
    }
    
    private void Shuffle()
    {
        /*
         * Shuffle uses the Fisher-Yates shuffle
         */
        for (var i = _cards.Count - 1; i > 0; i--) // Start from last card.
        {
            var j = _random.Next(i + 1);
            (_cards[i], _cards[j]) = (_cards[j], _cards[i]);
        }
    }
    
    public Card Draw()
    {
        /*
         * Draws card out of shoe and hands to player or dealer
         */
        if (_cards.Count == 0) // If shoe is empty
        {
            throw new InvalidOperationException("The shoe is empty - reshuffle before drawing again.");
        }

        // Takes card from top of deck, copies, then removes that card so it cannot be drawn again before returning the card.
        var topIndex = _cards.Count - 1;
        var card = _cards[topIndex];
        _cards.RemoveAt(topIndex);
        return card;
    }
}
