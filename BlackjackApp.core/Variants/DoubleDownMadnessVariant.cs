using BlackjackApp.core.Models;

namespace BlackjackApp.core.Variants;

public class DoubleDownMadnessVariant : IGameVariant
{
    public string Name => "Black Double Down Madness";

    public void DealInitialCards(Deck deck, Hand playerHand, Hand dealerHand) => throw new NotImplementedException();

    public void Hit(Deck deck, Hand hand) => throw new NotImplementedException();

    public bool CanDoubleDown(Hand hand) => throw new NotImplementedException();

    public bool CanSplit(Hand hand) => throw new NotImplementedException();

    public void PlayDealerHand(Deck deck, Hand dealerHand) => throw new NotImplementedException();

    public RoundOutcome DetermineOutcome(Hand playerHand, Hand dealerHand) => throw new NotImplementedException();

    public decimal ResolvePayout(Hand playerHand, Hand dealerHand, decimal bet) =>
        throw new NotImplementedException();
}
