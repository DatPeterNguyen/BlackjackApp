namespace BlackjackApp.core.Models;

/// <summary>
/// The shoe: 1-6 standard 52-card decks shuffled together. Cards are drawn
/// from the end of the internal list (treated as the "top" of the shoe) so
/// draws are O(1) instead of shifting the whole list on every card dealt.
/// </summary>

public class Deck
{
    private readonly List<Card> _cards = new(); // Empty list to store cards
    private readonly List<Card> _discard = new(); // Cards that have been played this shoe, waiting to be reshuffled back in
    private readonly Random _random; // Variable to store num for shuffling

    public int NumberOfDecks { get; } // Getter for number of decks.

    public int CardsRemaining => _cards.Count; // Looks at cards list and return how many is in it.

    /// <summary>How many played cards are currently sitting in the discard pile, waiting for the next reshuffle.</summary>
    public int DiscardCount => _discard.Count;

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
        _discard.Clear(); // A brand new shoe starts with nothing played yet.

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

    /// <summary>
    /// Moves a finished hand's (or the dealer's) cards into the discard
    /// pile once a round is over. These cards stay out of play - visibly
    /// "removed", not silently vanished - until the shoe actually needs
    /// reshuffling.
    /// </summary>
    public void Discard(IEnumerable<Card> cards) => _discard.AddRange(cards);

    /// <summary>
    /// True once the shoe has been played down to (or below) a cut-card
    /// threshold and needs reshuffling before the next round. Callers
    /// typically check this between rounds, never mid-round.
    /// </summary>
    public bool NeedsReshuffle(int cutCardThreshold) => _cards.Count <= cutCardThreshold;

    /// <summary>
    /// Shuffles every discarded card back into the shoe. This is the only
    /// way previously-played cards come back into play - unlike rebuilding
    /// a whole new Deck, it reuses the shoe's actual discard pile rather
    /// than conjuring a fresh, fully-stocked one from nowhere.
    /// </summary>
    public void ReshuffleDiscardIntoShoe()
    {
        _cards.AddRange(_discard);
        _discard.Clear();
        Shuffle();
    }
}
