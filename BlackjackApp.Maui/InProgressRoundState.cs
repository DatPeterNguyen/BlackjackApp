using System.Collections.Generic;
using BlackjackApp.core.Models;

namespace BlackjackApp.Maui;

/// <summary>
/// A serializable snapshot of a round that was still in progress (mid-hand,
/// or mid a War press/cash-out decision) when the app last closed, so
/// GameMenuPage's Load Game button can put the player right back where
/// they left off instead of the round simply being lost. Written by
/// MainPage.PersistInProgressRound after every action that changes round
/// state, read back by MainPage's resume constructor, and stored/cleared
/// via GameProgressStorage.SaveInProgressRound/LoadInProgressRound/
/// ClearInProgressRound (a single JSON blob in Preferences, same storage
/// as the rest of GameProgressStorage). Deliberately plain data only - no
/// behavior - so it serializes with System.Text.Json with no custom
/// converters needed (Card is already a trivially-serializable record).
/// </summary>
public sealed class InProgressRoundState
{
    /// <summary>"Standard", "DoubleDownMadness", or "War" - see MainPage.VariantKind/VariantFromKind.</summary>
    public required string VariantKind { get; init; }

    public required int DeckCount { get; init; }
    public required int HandCount { get; init; }

    /// <summary>The shoe's own deck count (Deck.NumberOfDecks) - normally the same as DeckCount, kept separately since they're conceptually different fields on Deck vs. MainPage.</summary>
    public required int ShoeDeckCount { get; init; }
    public required List<Card> ShoeRemainingCards { get; init; }
    public required List<Card> ShoeDiscardedCards { get; init; }

    public required List<Card> DealerCards { get; init; }
    public required List<SavedHandSlot> Hands { get; init; }

    public required int ActiveHandIndex { get; init; }
    public required int SelectedBetIndex { get; init; }

    /// <summary>War Blackjack only - which hand indices still have a pending Press/Cash-Out decision, in the order they'll be asked.</summary>
    public required List<int> WarDecisionQueue { get; init; }

    /// <summary>War Blackjack only - the hand index currently awaiting a Press/Cash-Out decision, or -1 for none.</summary>
    public required int PendingWarDecisionHandIndex { get; init; }

    /// <summary>Standard Blackjack only - true if the dealer's Ace up card is currently awaiting the player's Insure/No Insurance decision (see MainPage.PromptForInsurance).</summary>
    public required bool InsurancePending { get; init; }

    public required decimal WalletBalanceAtRoundStart { get; init; }
}

/// <summary>One player hand slot's saved state - see InProgressRoundState.Hands and PlayerHandSlot (the live equivalent this snapshots).</summary>
public sealed class SavedHandSlot
{
    public required List<Card> Cards { get; init; }
    public required int Bet { get; init; }
    public required int WarBet { get; init; }
    public required bool IsFinished { get; init; }
    public required bool HasBeenSplit { get; init; }
    public required string ResultText { get; init; }
    public required bool ResolvedEarly { get; init; }
    public required bool HasPendingBlackjack { get; init; }
    public required int InsuranceBet { get; init; }
    public required string InsuranceResultText { get; init; }
}
