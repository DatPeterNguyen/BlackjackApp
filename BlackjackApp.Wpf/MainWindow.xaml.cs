using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using BlackjackApp.core.Models;
using BlackjackApp.core.Variants;
using BlackjackApp.Wpf.Views;

namespace BlackjackApp.Wpf;

/// <summary>
/// Interaction logic for MainWindow.xaml.
///
/// DAY 2: real Standard Blackjack is now playable end-to-end for Hand 1 -
/// Deal builds an actual shoe and deals real Hand objects, Hit/Stand/Double
/// call into StandardBlackjackVariant, and the balance/result text reflect
/// real payouts. Hands 2-5 and Split are still placeholders; they land with
/// multi-hand support.
/// </summary>
public partial class MainWindow : Window
{
    private readonly StandardBlackjackVariant _variant = new();

    private Deck? _deck;
    private Hand _playerHand = new();
    private Hand _dealerHand = new();

    private decimal _balance = 1000m;
    private int _currentBet;
    private bool _roundInProgress;

    public MainWindow()
    {
        InitializeComponent();
        UpdateBalanceText();
    }

    private void ChipButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_roundInProgress)
        {
            return;
        }

        if (sender is Button { Tag: string tagValue } && int.TryParse(tagValue, out var chipValue))
        {
            _currentBet += chipValue;
            CurrentBetText.Text = $"Current Bet: ${_currentBet:N0}";
            UpdateBetChipDisplay();
        }
    }

    private void ClearBetButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_roundInProgress)
        {
            return;
        }

        _currentBet = 0;
        CurrentBetText.Text = "Current Bet: $0";
        UpdateBetChipDisplay();
    }

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow { Owner = this };
        settingsWindow.ShowDialog();
    }

    private void DealButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (_roundInProgress)
        {
            return;
        }

        if (_currentBet <= 0)
        {
            ResultText.Text = "Place a bet before dealing.";
            return;
        }

        if (_currentBet > _balance)
        {
            ResultText.Text = "You don't have enough chips for that bet.";
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
        ResultText.Text = "";
        RenderHands(hideHoleCard: true);
        SetRoundInProgress(true);

        if (_playerHand.IsBlackjack)
        {
            EndRound();
        }
    }

    private void HitButton_OnClick(object sender, RoutedEventArgs e)
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

    private void StandButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_roundInProgress || _deck is null)
        {
            return;
        }

        EndRound();
    }

    private void DoubleButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (!_roundInProgress || _deck is null)
        {
            return;
        }

        if (!_variant.CanDoubleDown(_playerHand))
        {
            ResultText.Text = "Can only double on your first two cards.";
            return;
        }

        if (_currentBet > _balance)
        {
            ResultText.Text = "Not enough chips to double down.";
            return;
        }

        _balance -= _currentBet;
        _currentBet *= 2;
        UpdateBalanceText();
        CurrentBetText.Text = $"Current Bet: ${_currentBet:N0}";
        UpdateBetChipDisplay();

        _variant.Hit(_deck, _playerHand);
        RenderHands(hideHoleCard: true);
        EndRound();
    }

    private void SplitButton_OnClick(object sender, RoutedEventArgs e)
    {
        ResultText.Text = "Split is coming soon - multi-hand support isn't wired up yet.";
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
        ResultText.Text = DescribeOutcome(outcome, payout);

        _roundInProgress = false;
        _currentBet = 0;
        CurrentBetText.Text = "Current Bet: $0";
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
        PlayerHandCardsPanel.Children.Clear();
        foreach (var card in _playerHand.Cards)
        {
            PlayerHandCardsPanel.Children.Add(CreateCardImage(CardImagePath(card), 110));
        }

        PlayerHandValueText.Text = _playerHand.Cards.Count > 0
            ? $"Value: {_playerHand.GetBestValue().Value}"
            : "";

        DealerCardsPanel.Children.Clear();
        for (var i = 0; i < _dealerHand.Cards.Count; i++)
        {
            var showFaceDown = hideHoleCard && i == 1;
            var imagePath = showFaceDown ? "Assets/Cards/back_red.png" : CardImagePath(_dealerHand.Cards[i]);
            DealerCardsPanel.Children.Add(CreateCardImage(imagePath, 140));
        }

        if (_dealerHand.Cards.Count == 0)
        {
            DealerValueText.Text = "";
        }
        else if (hideHoleCard)
        {
            DealerValueText.Text = $"Showing: {Hand.PointValue(_dealerHand.Cards[0].Rank)}";
        }
        else
        {
            DealerValueText.Text = $"Value: {_dealerHand.GetBestValue().Value}";
        }
    }

    /// <summary>Picks the largest chip denomination at or under the current bet, purely for display.</summary>
    private void UpdateBetChipDisplay()
    {
        PlayerHandBetText.Text = $"Bet: ${_currentBet:N0}";

        if (_currentBet <= 0)
        {
            PlayerHandChipImage.Source = null;
            return;
        }

        int[] denominations = [10000, 1000, 100, 25, 10, 5, 1];
        var chipValue = denominations.FirstOrDefault(d => d <= _currentBet, 1);
        PlayerHandChipImage.Source = new BitmapImage(new Uri($"Assets/Chips/chip_{chipValue}.png", UriKind.Relative));
    }

    private void SetRoundInProgress(bool roundInProgress)
    {
        DealButton.IsEnabled = !roundInProgress;
        HitButton.IsEnabled = roundInProgress;
        StandButton.IsEnabled = roundInProgress;
        DoubleButton.IsEnabled = roundInProgress;
        SplitButton.IsEnabled = roundInProgress;
        ChipButtonsPanel.IsEnabled = !roundInProgress;
    }

    private void UpdateBalanceText() => BalanceText.Text = $"Balance: ${_balance:N0}";

    private static Image CreateCardImage(string relativePath, double height)
    {
        var image = new Image
        {
            Source = new BitmapImage(new Uri(relativePath, UriKind.Relative)),
            Height = height,
            Margin = new Thickness(2),
        };
        RenderOptions.SetBitmapScalingMode(image, BitmapScalingMode.NearestNeighbor);
        return image;
    }

    private static string CardImagePath(Card card) => $"Assets/Cards/{RankCode(card.Rank)}{SuitCode(card.Suit)}.png";

    private static string RankCode(Rank rank) => rank switch
    {
        Rank.Ace => "A",
        Rank.Jack => "J",
        Rank.Queen => "Q",
        Rank.King => "K",
        _ => ((int)rank).ToString(),
    };

    private static string SuitCode(Suit suit) => suit switch
    {
        Suit.Clubs => "C",
        Suit.Diamonds => "D",
        Suit.Hearts => "H",
        Suit.Spades => "S",
        _ => throw new ArgumentOutOfRangeException(nameof(suit)),
    };
}
