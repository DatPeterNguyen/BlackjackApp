using System;
using System.Linq;
using BlackjackApp.core.Models;
using BlackjackApp.core.Variants;
using BlackjackApp.Maui.Views;

namespace BlackjackApp.Maui;

/// <summary>
/// MOBILE PORT of the WPF MainWindow.xaml.cs. Same game logic and flow -
/// Deal builds a real shoe and deals real Hand objects, Hit/Stand/Double
/// call into StandardBlackjackVariant, balance/result reflect real payouts.
/// Only the UI toolkit calls changed (MAUI Image/Label/Button instead of
/// WPF's), the game rules did not move at all.
/// </summary>
public partial class MainPage : ContentPage
{
    private readonly StandardBlackjackVariant _variant = new();

    private Deck? _deck;
    private Hand _playerHand = new();
    private Hand _dealerHand = new();

    private decimal _balance = 1000m;
    private int _currentBet;
    private bool _roundInProgress;

    public MainPage()
    {
        InitializeComponent();
        UpdateBalanceText();
    }

    private void ChipButton_OnClicked(object? sender, EventArgs e)
    {
        if (_roundInProgress)
        {
            return;
        }

        if (sender is Button { CommandParameter: string tagValue } && int.TryParse(tagValue, out var chipValue))
        {
            _currentBet += chipValue;
            CurrentBetLabel.Text = $"Current Bet: ${_currentBet:N0}";
            UpdateBetChipDisplay();
        }
    }

    private void ClearBetButton_OnClicked(object? sender, EventArgs e)
    {
        if (_roundInProgress)
        {
            return;
        }

        _currentBet = 0;
        CurrentBetLabel.Text = "Current Bet: $0";
        UpdateBetChipDisplay();
    }

    private async void SettingsButton_OnClicked(object? sender, EventArgs e)
    {
        await Navigation.PushModalAsync(new SettingsPage());
    }

    private void DealButton_OnClicked(object? sender, EventArgs e)
    {
        if (_roundInProgress)
        {
            return;
        }

        if (_currentBet <= 0)
        {
            ResultLabel.Text = "Place a bet before dealing.";
            return;
        }

        if (_currentBet > _balance)
        {
            ResultLabel.Text = "You don't have enough chips for that bet.";
            return;
        }

        // Build a fresh 4-deck shoe the first time, or once it's running
        // low. Reshuffling only between rounds (never mid-hand) keeps this
        // simple for now; a real burn/penetration policy can come later.
        if (_deck is null || _deck.CardsRemaining < 15)
        {
            _deck = new Deck(numberOfDecks: 4);
        }

        _playerHand = new Hand();
        _dealerHand = new Hand();
        _variant.DealInitialCards(_deck, _playerHand, _dealerHand);

        _balance -= _currentBet;
        UpdateBalanceText();

        _roundInProgress = true;
        ResultLabel.Text = "";
        RenderHands(hideHoleCard: true);
        SetRoundInProgress(true);

        if (_playerHand.IsBlackjack)
        {
            EndRound();
        }
    }

    private void HitButton_OnClicked(object? sender, EventArgs e)
    {
        if (!_roundInProgress || _deck is null)
        {
            return;
        }

        _variant.Hit(_deck, _playerHand);
        RenderHands(hideHoleCard: true);

        if (_playerHand.IsBust)
        {
            EndRound();
        }
    }

    private void StandButton_OnClicked(object? sender, EventArgs e)
    {
        if (!_roundInProgress || _deck is null)
        {
            return;
        }

        EndRound();
    }

    private void DoubleButton_OnClicked(object? sender, EventArgs e)
    {
        if (!_roundInProgress || _deck is null)
        {
            return;
        }

        if (!_variant.CanDoubleDown(_playerHand))
        {
            ResultLabel.Text = "Can only double on your first two cards.";
            return;
        }

        if (_currentBet > _balance)
        {
            ResultLabel.Text = "Not enough chips to double down.";
            return;
        }

        _balance -= _currentBet;
        _currentBet *= 2;
        UpdateBalanceText();
        CurrentBetLabel.Text = $"Current Bet: ${_currentBet:N0}";
        UpdateBetChipDisplay();

        _variant.Hit(_deck, _playerHand);
        RenderHands(hideHoleCard: true);
        EndRound();
    }

    private void SplitButton_OnClicked(object? sender, EventArgs e)
    {
        ResultLabel.Text = "Split is coming soon - multi-hand support isn't wired up yet.";
    }

    /// <summary>
    /// Plays out the dealer's hand (skipped if the player already busted),
    /// resolves the outcome, applies the payout to the balance, shows the
    /// result, and resets the table so a new bet can be placed.
    /// </summary>
    private void EndRound()
    {
        if (_deck is null)
        {
            return;
        }

        if (!_playerHand.IsBust)
        {
            _variant.PlayDealerHand(_deck, _dealerHand);
        }

        var outcome = _variant.DetermineOutcome(_playerHand, _dealerHand);
        var payout = _variant.ResolvePayout(_playerHand, _dealerHand, _currentBet);

        // The original bet was already deducted up front, so returning to
        // the balance means giving back the bet itself plus/minus payout.
        _balance += _currentBet + payout;
        UpdateBalanceText();

        RenderHands(hideHoleCard: false);
        ResultLabel.Text = DescribeOutcome(outcome, payout);

        _roundInProgress = false;
        _currentBet = 0;
        CurrentBetLabel.Text = "Current Bet: $0";
        UpdateBetChipDisplay();
        SetRoundInProgress(false);
    }

    private static string DescribeOutcome(RoundOutcome outcome, decimal payout) => outcome switch
    {
        RoundOutcome.PlayerBlackjack => $"Blackjack! You win ${payout:N0}.",
        RoundOutcome.PlayerWin => $"You win ${payout:N0}.",
        RoundOutcome.DealerBust => $"Dealer busts - you win ${payout:N0}.",
        RoundOutcome.Push => "Push - bet returned.",
        RoundOutcome.DealerWin => $"Dealer wins. You lose ${-payout:N0}.",
        RoundOutcome.PlayerBust => $"Bust! You lose ${-payout:N0}.",
        _ => "",
    };

    /// <summary>
    /// Redraws the dealer's cards and Hand 1's cards from current game
    /// state. While hideHoleCard is true, the dealer's second card is
    /// shown face-down and only the upcard's value is displayed.
    /// </summary>
    private void RenderHands(bool hideHoleCard)
    {
        PlayerHandCardsLayout.Children.Clear();
        foreach (var card in _playerHand.Cards)
        {
            PlayerHandCardsLayout.Children.Add(CreateCardImage(CardImageFile(card), 100));
        }

        PlayerHandValueLabel.Text = _playerHand.Cards.Count > 0
            ? $"Value: {_playerHand.GetBestValue().Value}"
            : "";

        DealerCardsLayout.Children.Clear();
        for (var i = 0; i < _dealerHand.Cards.Count; i++)
        {
            var showFaceDown = hideHoleCard && i == 1;
            var imageFile = showFaceDown ? "back_red.png" : CardImageFile(_dealerHand.Cards[i]);
            DealerCardsLayout.Children.Add(CreateCardImage(imageFile, 130));
        }

        if (_dealerHand.Cards.Count == 0)
        {
            DealerValueLabel.Text = "";
        }
        else if (hideHoleCard)
        {
            DealerValueLabel.Text = $"Showing: {Hand.PointValue(_dealerHand.Cards[0].Rank)}";
        }
        else
        {
            DealerValueLabel.Text = $"Value: {_dealerHand.GetBestValue().Value}";
        }
    }

    /// <summary>Picks the largest chip denomination at or under the current bet, purely for display.</summary>
    private void UpdateBetChipDisplay()
    {
        PlayerHandBetLabel.Text = $"Bet: ${_currentBet:N0}";

        if (_currentBet <= 0)
        {
            PlayerHandChipImage.Source = null;
            return;
        }

        int[] denominations = [10000, 1000, 100, 25, 10, 5, 1];
        var chipValue = denominations.FirstOrDefault(d => d <= _currentBet, 1);
        PlayerHandChipImage.Source = ImageSource.FromFile($"chip_{chipValue}.png");
    }

    private void SetRoundInProgress(bool roundInProgress)
    {
        DealButton.IsEnabled = !roundInProgress;
        HitButton.IsEnabled = roundInProgress;
        StandButton.IsEnabled = roundInProgress;
        DoubleButton.IsEnabled = roundInProgress;
        SplitButton.IsEnabled = roundInProgress;
        ChipButtonsLayout.IsEnabled = !roundInProgress;
    }

    private void UpdateBalanceText() => BalanceLabel.Text = $"Balance: ${_balance:N0}";

    private static Image CreateCardImage(string imageFile, double height) => new()
    {
        Source = ImageSource.FromFile(imageFile),
        HeightRequest = height,
        Aspect = Aspect.AspectFit,
        Margin = new Thickness(2),
    };

    private static string CardImageFile(Card card) => $"card_{RankCode(card.Rank)}{SuitCode(card.Suit)}.png";

    private static string RankCode(Rank rank) => rank switch
    {
        Rank.Ace => "a",
        Rank.Jack => "j",
        Rank.Queen => "q",
        Rank.King => "k",
        _ => ((int)rank).ToString(),
    };

    private static string SuitCode(Suit suit) => suit switch
    {
        Suit.Clubs => "c",
        Suit.Diamonds => "d",
        Suit.Hearts => "h",
        Suit.Spades => "s",
        _ => throw new ArgumentOutOfRangeException(nameof(suit)),
    };
}
