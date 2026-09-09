using BlackjackApp.core.Models;

namespace BlackjackApp.core.Variants;

public class StandardBlackjackVariant : IGameVariant
{
    public string Name => "Standard Blackjack";

    public bool CanDoubleDown(Hand hand) => throw new NotImplementedException();

    public bool CanSplit(Hand hand) => throw new NotImplementedException();

    public decimal ResolvePayout(Hand playerHand, Hand dealerHand, decimal bet) =>
        throw new NotImplementedException();
}
