using BlackjackApp.core.Models;

namespace BlackjackApp.Tests;

public class PlayerHandSlotTests
{
    [Test]
    public void NewSlot_StartsWithAnEmptyHandZeroBetAndNotFinished()
    {
        var slot = new PlayerHandSlot();

        Assert.Multiple(() =>
        {
            Assert.That(slot.Hand.Cards, Is.Empty);
            Assert.That(slot.Bet, Is.EqualTo(0));
            Assert.That(slot.IsFinished, Is.False);
            Assert.That(slot.ResultText, Is.EqualTo(""));
        });
    }

    [Test]
    public void Bet_CanBeSetIndependentlyOfOtherSlots()
    {
        // Each hand slot's bet is sized independently in multi-hand rounds -
        // setting one slot's bet should never touch another slot's state.
        var slotOne = new PlayerHandSlot { Bet = 25 };
        var slotTwo = new PlayerHandSlot { Bet = 100 };

        Assert.Multiple(() =>
        {
            Assert.That(slotOne.Bet, Is.EqualTo(25));
            Assert.That(slotTwo.Bet, Is.EqualTo(100));
        });
    }

    [Test]
    public void IsFinished_AndResultText_CanBeSetAfterConstruction()
    {
        var slot = new PlayerHandSlot();

        slot.IsFinished = true;
        slot.ResultText = "Blackjack! You win $15.";

        Assert.Multiple(() =>
        {
            Assert.That(slot.IsFinished, Is.True);
            Assert.That(slot.ResultText, Is.EqualTo("Blackjack! You win $15."));
        });
    }
}
