namespace BlackjackApp.core.Models;

public class Player
{
    public string Name { get; set; } = string.Empty;
    public ChipWallet Wallet { get; } = new();
    public List<Hand> Hands { get; } = new();
}
