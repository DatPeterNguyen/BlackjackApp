namespace BlackjackApp.core.Models;

/// <summary>
/// A single hand of cards belonging to a player or the dealer, with real
/// blackjack value calculation. Split-hand tracking, bet amount, and
/// settled/finished state still live outside this class - it only knows
/// about the cards themselves and what they add up to.
/// </summary>
public class Hand
{
    public List<Card> Cards { get; } = new();

    public void AddCard(Card card) => Cards.Add(card);

    /// <summary>
    /// The best blackjack total for this hand, and whether that total is
    /// "soft" (an Ace is still being counted as 11 rather than 1).
    /// Standard algorithm: count every Ace as 11 first, then downgrade
    /// Aces to 1 one at a time (subtracting 10) while the total is over 21
    /// and an Ace is still available to downgrade.
    /// </summary>
    public (int Value, bool IsSoft) GetBestValue()
    {
        var total = 0;
        var acesCountedAsEleven = 0;

        foreach (var card in Cards)
        {
            total += PointValue(card.Rank);
            if (card.Rank == Rank.Ace)
            {
                acesCountedAsEleven++;
            }
        }

        while (total > 21 && acesCountedAsEleven > 0)
        {
            total -= 10;
            acesCountedAsEleven--;
        }

        return (total, acesCountedAsEleven > 0);
    }

    public bool IsBust => GetBestValue().Value > 21;

    /// <summary>
    /// A "natural" blackjack: exactly two cards totaling 21. A 21 reached
    /// with 3+ cards (e.g. 7+7+7) is not a blackjack and doesn't get the
    /// 3:2 payout - that distinction gets enforced wherever payouts are
    /// resolved, not here, but the two-card check itself lives on the hand.
    /// </summary>
    public bool IsBlackjack => Cards.Count == 2 && GetBestValue().Value == 21;

    /// <summary>
    /// Blackjack point value for a rank: Ace starts at 11 (downgraded to 1
    /// as needed in GetBestValue), face cards are worth 10, and everything
    /// else matches its numeric rank. The Rank enum's underlying int values
    /// (Jack=11, Queen=12, King=13) are ordinal positions, not point
    /// values, so face cards need to be mapped down to 10 explicitly here.
    /// Public so variant/split logic elsewhere (e.g. "can these two cards
    /// be split?") can compare point values without duplicating this map.
    /// </summary>
    public static int PointValue(Rank rank) => rank switch
    {
        Rank.Ace => 11,
        Rank.Jack or Rank.Queen or Rank.King => 10,
        _ => (int)rank,
    };
}
