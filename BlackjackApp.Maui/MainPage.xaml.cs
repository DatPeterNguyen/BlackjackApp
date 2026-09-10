using System;
using System.Linq;
using BlackjackApp.core.Models;
using BlackjackApp.core.Variants;
using BlackjackApp.Maui.Views;

namespace BlackjackApp.Maui;

/// <summary>
/// MOBILE PORT of the WPF MainWindow.xaml.cs, now variant-aware: _variant
/// is an IGameVariant rather than hardcoded StandardBlackjackVariant, so
/// whichever variant Settings selects (Standard / Double Down Madness /
/// War) actually gets dealt and played here, not just tested in isolation.
///
/// The three variants have genuinely different round shapes:
///  - Standard: 2 cards each, double ends the turn immediately.
///  - Double Down Madness: player starts on 1 card, doubling can repeat
///    and doesn't end the turn (EndsTurnAfterDouble is false), and an
///    Ace-opener hand locks itself after its one follow-up card.
///  - War: DealInitialCards already resolves the War card stage AND deals
///    the second blackjack cards in one call (see WarBlackjackVariant) -
///    this page separately applies the War side-bet payout right after
///    dealing, then plays out completely normal blackjack from there.
/// None of that needed a MainPage rewrite per variant - IGameVariant.
/// CanHit / EndsTurnAfterDouble are enough for one shared round loop to
/// handle all three correctly.
/// </summary>
public partial class MainPage : ContentPage
{
    private IGameVariant _variant = new StandardBlackjackVariant();
    private int _deckCount = 4;

    /// <summary>Set when Settings is saved mid-round - applied at the start of the next Deal instead of immediately, so an in-progress hand never has its rules swapped out from under it.</summary>
    private IGameVariant? _pendingVariant;
    private int? _pendingDeckCount;

    private Deck? _deck;
    private Hand _playerHand = new();
    private Hand _dealerHand = new();

    private decimal _balance = 1000m;
    private int _currentBet;
    private bool _roundInProgress;

    /// <summary>Set right after dealing when the active variant is War Blackjack, so EndRound can prepend the War side-bet result to whatever the blackjack hand's own outcome message is.</summary>
    private string _resultPrefix = "";

    public MainPage()
    {
        InitializeComponent();
        UpdateBalanceText();
        UpdateVariantLabel();
        UpdateTableColors();
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
        var settingsPage = new SettingsPage(_variant, _deckCount);
        settingsPage.SettingsSaved += OnSettingsSaved;
        await Navigation.PushModalAsync(settingsPage);
    }

    private void OnSettingsSaved(IGameVariant variant, int deckCount)
    {
        if (_roundInProgress)
        {
            // Never swap the rules out from under a hand that's already
            // dealt under the old variant's shape - queue it instead and
            // apply it once EndRound finishes this hand.
            _pendingVariant = variant;
            _pendingDeckCount = deckCount;
            ResultLabel.Text = _resultPrefix + "Settings saved - will apply once this round finishes.";
            return;
        }

        ApplyVariantChange(variant, deckCount);
    }

    private void ApplyVariantChange(IGameVariant variant, int deckCount)
    {
        _variant = variant;
        _deckCount = deckCount;
        _deck = null; // force a fresh shoe built at the new deck count on the next Deal
        UpdateVariantLabel();
        UpdateTableColors();
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

        // Build a fresh shoe the first time, once it's running low, or
        // once the deck count changed in Settings. Reshuffling only
        // between rounds (never mid-hand) keeps this simple for now.
        if (_deck is null || _deck.CardsRemaining < 15)
        {
            _deck = new Deck(numberOfDecks: _deckCount);
        }

        _playerHand = new Hand();
        _dealerHand = new Hand();
        _resultPrefix = "";
        _variant.DealInitialCards(_deck, _playerHand, _dealerHand);

        _balance -= _currentBet;

        // War Blackjack's DealInitialCards already dealt and "locked in"
        // the War card stage (see WarBlackjackVariant) - resolve that side
        // bet's payout now, before the blackjack hand continues.
        if (_variant is WarBlackjackVariant warVariant)
        {
            var warPayout = warVariant.ResolveWarPayout(_playerHand, _dealerHand, _currentBet);
            _balance += warPayout;
            _resultPrefix = warVariant.PlayerWinsWar(_playerHand, _dealerHand)
                ? $"War: you win ${warPayout:N0}! "
                : $"War: dealer wins the ${-warPayout:N0} side bet. ";
        }

        UpdateBalanceText();

        _roundInProgress = true;
        ResultLabel.Text = _resultPrefix;
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

        if (!_variant.CanHit(_playerHand))
        {
            ResultLabel.Text = _resultPrefix + "This hand can't be hit again.";
            return;
        }

        _variant.Hit(_deck, _playerHand);
        RenderHands(hideHoleCard: true);
        ContinueOrEndTurnAfterAction(cameFromDouble: false);
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
            ResultLabel.Text = _resultPrefix + "Can't double down on this hand right now.";
            return;
        }

        if (_currentBet > _balance)
        {
            ResultLabel.Text = _resultPrefix + "Not enough chips to double down.";
            return;
        }

        // Double Down Madness allows doubling repeatedly, each based on
        // the current wager - the same "double the current bet" math
        // covers Standard's single double just as well.
        _balance -= _currentBet;
        _currentBet *= 2;
        UpdateBalanceText();
        CurrentBetLabel.Text = $"Current Bet: ${_currentBet:N0}";
        UpdateBetChipDisplay();

        _variant.Hit(_deck, _playerHand);
        RenderHands(hideHoleCard: true);
        ContinueOrEndTurnAfterAction(cameFromDouble: true);
    }

    private void SplitButton_OnClicked(object? sender, EventArgs e)
    {
        ResultLabel.Text = _resultPrefix + "Split isn't wired up yet - multi-hand support is still coming.";
    }

    /// <summary>
    /// Shared post-Hit/post-Double bookkeeping: ends the round on a bust,
    /// ends it if this variant's rule says doubling finishes the turn, and
    /// otherwise ends it once the hand can neither hit nor double any
    /// further (e.g. Double Down Madness's Ace-opener lock finally closing).
    /// Anything else leaves the round in progress for more player actions.
    /// </summary>
    private void ContinueOrEndTurnAfterAction(bool cameFromDouble)
    {
        if (_playerHand.IsBust)
        {
            EndRound();
            return;
        }

        if (cameFromDouble && _variant.EndsTurnAfterDouble)
        {
            EndRound();
            return;
        }

        if (!_variant.CanHit(_playerHand) && !_variant.CanDoubleDown(_playerHand))
        {
            EndRound();
        }
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
        ResultLabel.Text = _resultPrefix + DescribeOutcome(outcome, payout);

        _roundInProgress = false;
        _currentBet = 0;
        CurrentBetLabel.Text = "Current Bet: $0";
        UpdateBetChipDisplay();
        SetRoundInProgress(false);

        if (_pendingVariant is not null && _pendingDeckCount is not null)
        {
            ApplyVariantChange(_pendingVariant, _pendingDeckCount.Value);
            _pendingVariant = null;
            _pendingDeckCount = null;
        }
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
            PlayerHandCardsLayout.Children.Add(CreateCardImage(CardImageFile(card), 80));
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
        else if (hideHoleCard && _dealerHand.Cards.Count > 1)
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

    private void UpdateVariantLabel() => VariantLabel.Text = _variant.Name;

    /// <summary>
    /// Recolors the table felt to match the design doc's per-variant look:
    /// green for Standard, red for Double Down Madness, blue for War.
    /// </summary>
    private void UpdateTableColors()
    {
        var (pageBackground, feltBackground, feltStroke) = _variant switch
        {
            DoubleDownMadnessVariant => (Color.FromArgb("#3D0B0B"), Color.FromArgb("#441010"), Color.FromArgb("#6B3A3A")),
            WarBlackjackVariant => (Color.FromArgb("#0B1A3D"), Color.FromArgb("#101B44"), Color.FromArgb("#3A4A6B")),
            _ => (Color.FromArgb("#0B3D2E"), Color.FromArgb("#0E4433"), Color.FromArgb("#3A6B57")),
        };

        BackgroundColor = pageBackground;
        DealerAreaBorder.BackgroundColor = feltBackground;
        DealerAreaBorder.Stroke = feltStroke;
        PlayerAreaBorder.BackgroundColor = feltBackground;
        PlayerAreaBorder.Stroke = feltStroke;
    }

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
