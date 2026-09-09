using BlackjackApp.core.Models;

namespace BlackjackApp.core.Variants;

public class WarBlackjackVariant : IGameVariant
{
    public string Name => "War Blackjack";

    public bool CanDoubleDown(Hand hand) => throw new NotImplementedException();

    public bool CanSplit(Hand hand) => throw new NotImplementedException();

    public decimal ResolvePayout(Hand playerHand, Hand dealerHand, decimal bet) =>
        throw new NotImplementedException();
}
