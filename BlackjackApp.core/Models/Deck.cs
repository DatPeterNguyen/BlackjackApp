namespace BlackjackApp.core.Models;

// Represents the shoe: 1-6 standard 52-card decks shuffled together.
// Shuffle/draw logic intentionally left out for now — foundation only.
public class Deck
{
    public int NumberOfDecks { get; }

    public Deck(int numberOfDecks = 4)
    {
        NumberOfDecks = numberOfDecks;
    }
}
