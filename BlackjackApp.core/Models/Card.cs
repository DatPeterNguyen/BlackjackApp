namespace BlackjackApp.core.Models;

// Immutable representation of a single playing card.
// Blackjack value logic (Ace = 1 or 11, face cards = 10) will live
// wherever hand-value calculation ends up, not on the card itself.
public record Card(Suit Suit, Rank Rank);
