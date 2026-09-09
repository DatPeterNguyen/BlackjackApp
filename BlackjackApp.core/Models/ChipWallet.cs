namespace BlackjackApp.core.Models;

// Tracks a player's chip balance. Table minimum ($1) / maximum ($10K)
// enforcement and transaction logic will be added later.
public class ChipWallet
{
    public decimal Balance { get; set; }
}
