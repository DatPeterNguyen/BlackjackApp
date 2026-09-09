namespace BlackjackApp.core.Models;

// A single hand of cards belonging to a player or the dealer.
// Split hands, bet amount, and settled/finished state will be added
// once betting logic is designed.
public class Hand
{
    public List<Card> Cards { get; } = new();
}
