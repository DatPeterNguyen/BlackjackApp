using BlackjackApp.core.Models;

namespace BlackjackApp.core.Variants;

// Contract every table variant (Standard, Double Down Madness, War) will
// implement. Method bodies are stubbed for now — this is just the shape.
public interface IGameVariant
{
    string Name { get; }

    bool CanDoubleDown(Hand hand);

    bool CanSplit(Hand hand);

    decimal ResolvePayout(Hand playerHand, Hand dealerHand, decimal bet);
}
