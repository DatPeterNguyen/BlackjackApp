namespace BlackjackApp.core.Economy;

/// <summary>
/// Owns the player's chip balance and enforces the table's betting limits,
/// per the design doc: chip denominations of $1, $5, $10, $25, $100, $1,000
/// and $10,000, a table minimum of $1, and a table maximum of $10,000. Any
/// single wager - the initial bet, a War side bet, or a bet after doubling
/// (including Double Down Madness's repeated doubles, which can otherwise
/// escalate past the cap fast: $10 -> $20 -> $40 -> $80 -> ...) must fall
/// within [TableMinimum, TableMaximum].
/// </summary>
public class ChipWallet
{
    public const decimal TableMinimum = 1m;
    public const decimal TableMaximum = 10_000m;

    /// <summary>The chip denominations available at the table, smallest to largest.</summary>
    public static readonly decimal[] Denominations = [1m, 5m, 10m, 25m, 100m, 1_000m, 10_000m];

    public decimal Balance { get; private set; }

    public ChipWallet(decimal startingBalance)
    {
        Balance = startingBalance;
    }

    /// <summary>Adds winnings, a payout, or a bonus (check-in reward, etc.) to the balance.</summary>
    public void Add(decimal amount) => Balance += amount;

    /// <summary>
    /// Attempts to remove a wager from the balance. Fails without changing
    /// the balance if the player can't cover it.
    /// </summary>
    public bool TryDeduct(decimal amount)
    {
        if (amount > Balance)
        {
            return false;
        }

        Balance -= amount;
        return true;
    }

    /// <summary>True if a wager of this size is between the table minimum and maximum, inclusive.</summary>
    public static bool IsWithinTableLimits(decimal wager) => wager >= TableMinimum && wager <= TableMaximum;

    /// <summary>How much more can still be added to an existing wager before hitting the table maximum.</summary>
    public static decimal RemainingRoomUnderMax(decimal currentWager) => Math.Max(0m, TableMaximum - currentWager);
}
