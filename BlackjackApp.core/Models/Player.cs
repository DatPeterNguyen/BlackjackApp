using BlackjackApp.core.Economy;

namespace BlackjackApp.core.Models;

public class Player
{
    public string Name { get; set; } = string.Empty;
    public ChipWallet Wallet { get; } = new(startingBalance: 1000m);
    public List<Hand> Hands { get; } = new();
}
