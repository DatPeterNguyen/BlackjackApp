using System;
using System.Collections.Generic;
using System.Linq;
using BlackjackApp.core.Economy;
using BlackjackApp.core.Models;
using BlackjackApp.core.Variants;
using BlackjackApp.Maui.Views;
using Microsoft.Maui.Controls.Shapes;
using Microsoft.Maui.Layouts;

namespace BlackjackApp.Maui;

/// <summary>
/// MOBILE PORT of the WPF MainWindow.xaml.cs, now both variant-aware AND
/// multi-hand-aware. _variant is an IGameVariant (Standard / Double Down
/// Madness / War), and the player can have 1-5 simultaneous hand slots
/// (_hands), each with its own independent bet, all played and resolved
/// against the SAME shared dealer hand (per the design doc's 1-5 hand
/// setting).
///
/// Turn order: hands play left to right. _activeHandIndex is whichever
/// slot Hit/Stand/Double currently apply to; when that hand finishes
/// (bust, stand, doubled-and-done, or can no longer act), play advances to
/// the next unfinished hand. Once every hand is finished, EndRound plays
/// the dealer once and resolves every hand's outcome/payout against that
/// one dealer hand.
///
/// Before a round starts, tapping a hand slot selects it as the current
/// betting target - chip taps and Clear apply to whichever slot is
/// selected, so each hand's bet is sized fully independently (per the
/// "just duplicate the single-hand pattern" approach agreed on, rather
/// than a separate chip tray per hand).
/// </summary>
public partial class MainPage : ContentPage
{
    private IGameVariant _variant = new StandardBlackjackVariant();
    private int _deckCount = 4;
    private int _handCount = 1;

    /// <summary>Set when Settings is saved mid-round - applied at the start of the next Deal instead of immediately, so an in-progress round never has its rules (or hand count) swapped out from under it.</summary>
    private IGameVariant? _pendingVariant;
    private int? _pendingDeckCount;
    private int? _pendingHandCount;

    private Deck? _deck;
    private Hand _dealerHand = new();

    /// <summary>Owns the balance and enforces the table's $1-$10,000 per-hand betting limits (see ChipWallet).</summary>
    private readonly ChipWallet _wallet = new(startingBalance: 1000m);

    /// <summary>One entry per active hand slot (1-5), rebuilt whenever the hand count changes.</summary>
    private List<PlayerHandSlot> _hands = new();

    /// <summary>The dynamically-built UI views for each hand slot, index-matched to _hands.</summary>
    private readonly List<HandSlotView> _handSlotViews = new();

    /// <summary>Which hand slot chip taps/Clear currently target, before a round starts.</summary>
    private int _selectedBetIndex;

    /// <summary>Which hand slot Hit/Stand/Double currently apply to; -1 when no round is in progress.</summary>
    private int _activeHandIndex = -1;

    private bool _roundInProgress;

    /// <summary>War Blackjack only: whether chip taps currently size the main bet or the optional War side bet.</summary>
    private enum BettingTarget { MainBet, WarBet }

    private BettingTarget _bettingTarget = BettingTarget.MainBet;

    /// <summary>War Blackjack only: hand indices whose War card beat the dealer's and are waiting on a Press-or-Cash-Out choice, processed one at a time.</summary>
    private readonly Queue<int> _warDecisionQueue = new();

    /// <summary>War Blackjack only: the hand a Press/Cash Out tap currently applies to; -1 when no War decision is pending.</summary>
    private int _pendingWarDecisionHandIndex = -1;

    /// <summary>
    /// War Blackjack only: the wallet balance right before this round's
    /// bets were deducted. WarTotalLabel is always just (current balance -
    /// this snapshot) - reading the real wallet directly, rather than
    /// manually re-deriving War-plus-blackjack profit by hand, is what
    /// makes it correct once a War win gets pressed into a hand's bet:
    /// pressed winnings blur the line between "War money" and "blackjack
    /// money", so anything short of the actual balance movement
    /// double-counts or drops part of it.
    /// </summary>
    private decimal _walletBalanceAtRoundStart;

    /// <summary>
    /// Set once a round ends, so the table still shows that round's final
    /// cards/results until the player starts betting again - at that point
    /// the table is wiped clean like a fresh game, rather than lingering
    /// under the new bet being placed.
    /// </summary>
    private bool _needsTableClearOnNextBet;

    /// <summary>Card image height used inside a hand slot - smaller than the dealer's so several fit across one slot's width before wrapping.</summary>
    private const double HandSlotCardHeight = 56;

    /// <summary>Real, explicit width for a hand slot's card row - wide enough to fit 3 HandSlotCardHeight-sized cards per row before wrapping.</summary>
    private const double HandSlotCardsWidth = 140;

    /// <summary>Outer hand slot Border width - HandSlotCardsWidth plus room for its Padding/Border.</summary>
    private const double HandSlotBorderWidth = HandSlotCardsWidth + 16;

    public MainPage()
    {
        InitializeComponent();

        // Matches the wallet's actual starting balance so switching to War
        // before ever dealing a hand shows a real "$0" total instead of the
        // whole starting balance read as profit.
        _walletBalanceAtRoundStart = _wallet.Balance;

        BuildHandSlots();
        UpdateBalanceText();
        UpdateVariantLabel();
        UpdateTableColors();
    }

    /// <summary>Bundles the dynamically-created views for one hand slot, since XAML can't name a variable (1-5) number of them.</summary>
    private sealed class HandSlotView
    {
        public required Border Border { get; init; }
        public required FlexLayout CardsLayout { get; init; }
        public required Image ChipImage { get; init; }
        public required Label BetLabel { get; init; }
        public required Label WarBetLabel { get; init; }
        public required Label ValueLabel { get; init; }
        public required Label ResultLabel { get; init; }
    }

    /// <summary>
    /// (Re)builds _hands and their matching UI views for the current
    /// _handCount. Called on startup and whenever the hand count changes in
    /// Settings. Resets betting/turn selection back to hand 0.
    /// </summary>
    private void BuildHandSlots()
    {
        _hands = Enumerable.Range(0, _handCount).Select(_ => new PlayerHandSlot()).ToList();
        _handSlotViews.Clear();
        PlayerHandsLayout.Children.Clear();
        _selectedBetIndex = 0;
        _activeHandIndex = -1;

        // All In only makes sense when there's a single hand to shove
        // everything onto - with multiple hands the player sizes each one
        // independently, so an "all in" button would be ambiguous.
        AllInButton.IsVisible = _handCount == 1;
        TotalResultLabel.IsVisible = false;
        TotalResultLabel.Text = "";
        _needsTableClearOnNextBet = false;

        for (var i = 0; i < _handCount; i++)
        {
            var (border, view) = CreateHandSlotView(i);
            _handSlotViews.Add(view);
            PlayerHandsLayout.Children.Add(border);
        }

        RefreshHandSlotHighlights();
        UpdateWarUiVisibility();
    }

    /// <summary>
    /// Builds one hand slot's Border and bundled view - shared by
    /// BuildHandSlots (the normal 1-5 seats) and SplitButton_OnClicked
    /// (inserting a brand new hand mid-round when a pair is split).
    /// handIndex is only used to wire up its tap-to-select-for-betting
    /// gesture, which is a no-op mid-round anyway (see SelectHandForBetting),
    /// so it doesn't need to stay accurate if later hands shift position.
    /// </summary>
    private (Border Border, HandSlotView View) CreateHandSlotView(int handIndex)
    {
        var cardsLayout = new FlexLayout
        {
            Direction = FlexDirection.Row,
            Wrap = FlexWrap.Wrap,
            JustifyContent = FlexJustify.Center,
            AlignItems = FlexAlignItems.Center,
            HorizontalOptions = LayoutOptions.Fill,
            WidthRequest = HandSlotCardsWidth, // pinned explicitly - a Fill request alone wasn't reliably resolving to a real width inside the stack
        };
        var chipImage = new Image { HeightRequest = 28 };
        var betLabel = new Label { Text = "Bet: $0", TextColor = Color.FromArgb("#8FBFA9"), HorizontalOptions = LayoutOptions.Center, FontSize = 11 };
        var warBetLabel = new Label { Text = "War: $0", TextColor = Color.FromArgb("#8FBFA9"), HorizontalOptions = LayoutOptions.Center, FontSize = 11, IsVisible = _variant is WarBlackjackVariant };
        var valueLabel = new Label { Text = "", TextColor = Color.FromArgb("#8FBFA9"), HorizontalOptions = LayoutOptions.Center, FontSize = 11 };
        var resultLabel = new Label { Text = "", TextColor = Color.FromArgb("#FFD700"), HorizontalOptions = LayoutOptions.Center, FontSize = 10, FontAttributes = FontAttributes.Bold };

        var content = new VerticalStackLayout
        {
            HorizontalOptions = LayoutOptions.Fill,
            Spacing = 2,
            Children = { cardsLayout, chipImage, betLabel, warBetLabel, valueLabel, resultLabel },
        };

        var border = new Border
        {
            Stroke = Color.FromArgb("#8FBFA9"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 6 },
            Padding = 4,
            Margin = 2,
            WidthRequest = HandSlotBorderWidth,
            HorizontalOptions = LayoutOptions.Start,
            Content = content,
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) => SelectHandForBetting(handIndex);
        border.GestureRecognizers.Add(tap);

        var view = new HandSlotView
        {
            Border = border,
            CardsLayout = cardsLayout,
            ChipImage = chipImage,
            BetLabel = betLabel,
            WarBetLabel = warBetLabel,
            ValueLabel = valueLabel,
            ResultLabel = resultLabel,
        };

        return (border, view);
    }

    /// <summary>
    /// Shows/hides the War-only betting-target toggle and each hand slot's
    /// War bet label based on the current variant, and resets betting back
    /// to the main bet whenever War isn't the active variant.
    /// </summary>
    private void UpdateWarUiVisibility()
    {
        var isWar = _variant is WarBlackjackVariant;
        BetTargetButton.IsVisible = isWar;

        if (!isWar)
        {
            _bettingTarget = BettingTarget.MainBet;
            BetTargetButton.Text = "Betting: Main Bet";
        }

        foreach (var view in _handSlotViews)
        {
            view.WarBetLabel.IsVisible = isWar;
        }

        UpdateWarTotalLabel();
    }

    /// <summary>
    /// Refreshes the always-visible round total readout (War Blackjack
    /// only) - shown/hidden whenever the variant changes, and refreshed
    /// with the real result once EndRound resolves the whole round (War
    /// AND blackjack combined). Deliberately NOT called mid-round (e.g.
    /// right after War cards are dealt, or on a Press/Cash Out) - every
    /// hand's bet is still deducted and in play at that point, so showing a
    /// total then would read as a loss before the round has even finished.
    /// Computed straight from the real wallet movement since
    /// _walletBalanceAtRoundStart, rather than re-derived by hand, so a
    /// pressed War win (which blurs "War money" into "blackjack money")
    /// can't throw the total off.
    /// </summary>
    private void UpdateWarTotalLabel()
    {
        if (_variant is not WarBlackjackVariant)
        {
            WarTotalLabel.IsVisible = false;
            return;
        }

        var totalNet = _wallet.Balance - _walletBalanceAtRoundStart;

        WarTotalLabel.IsVisible = true;
        WarTotalLabel.Text = totalNet switch
        {
            > 0 => $"Total: won ${totalNet:N0}",
            < 0 => $"Total: lost ${-totalNet:N0}",
            _ => "Total: $0",
        };
    }

    /// <summary>Before a round: tapping a hand slot makes it the current betting target. Ignored mid-round, since bets are locked in once dealt.</summary>
    private void SelectHandForBetting(int index)
    {
        if (_roundInProgress)
        {
            return;
        }

        _selectedBetIndex = index;
        RefreshHandSlotHighlights();
    }

    /// <summary>Highlights whichever hand is currently relevant - the betting target pre-round, or the hand in play mid-round.</summary>
    private void RefreshHandSlotHighlights()
    {
        for (var i = 0; i < _handSlotViews.Count; i++)
        {
            var isHighlighted = _roundInProgress ? i == _activeHandIndex : i == _selectedBetIndex;
            _handSlotViews[i].Border.Stroke = isHighlighted ? Color.FromArgb("#FFD700") : Color.FromArgb("#8FBFA9");
            _handSlotViews[i].Border.StrokeThickness = isHighlighted ? 3 : 1;
        }
    }

    private void ChipButton_OnClicked(object? sender, EventArgs e)
    {
        if (_roundInProgress)
        {
            return;
        }

        if (sender is Button { CommandParameter: string tagValue } && int.TryParse(tagValue, out var chipValue))
        {
            if (_needsTableClearOnNextBet)
            {
                ClearTableForNewRound();
            }

            var slot = _hands[_selectedBetIndex];
            var targetingWarBet = _bettingTarget == BettingTarget.WarBet && _variant is WarBlackjackVariant;
            var currentAmount = targetingWarBet ? slot.WarBet : slot.Bet;

            if (currentAmount >= ChipWallet.TableMaximum)
            {
                ResultLabel.Text = $"Table maximum is ${ChipWallet.TableMaximum:N0} per hand.";
                return;
            }

            // Clamp rather than reject outright, so tapping a big chip near
            // the cap still places as much of it as the table allows.
            var room = ChipWallet.RemainingRoomUnderMax(currentAmount);
            var amountToAdd = (int)Math.Min(chipValue, room);

            if (targetingWarBet)
            {
                slot.WarBet += amountToAdd;
            }
            else
            {
                slot.Bet += amountToAdd;
            }

            UpdateHandSlotBetDisplay(_selectedBetIndex);
            UpdateTotalWageredText();
            ResultLabel.Text = (targetingWarBet ? slot.WarBet : slot.Bet) >= ChipWallet.TableMaximum
                ? $"Table maximum is ${ChipWallet.TableMaximum:N0} per hand."
                : "";
        }
    }

    private void ClearBetButton_OnClicked(object? sender, EventArgs e)
    {
        if (_roundInProgress)
        {
            return;
        }

        var slot = _hands[_selectedBetIndex];

        if (_bettingTarget == BettingTarget.WarBet && _variant is WarBlackjackVariant)
        {
            slot.WarBet = 0;
        }
        else
        {
            slot.Bet = 0;
        }

        UpdateHandSlotBetDisplay(_selectedBetIndex);
        UpdateTotalWageredText();
    }

    /// <summary>War Blackjack only: toggles whether chip taps/Clear size the main bet or the War side bet.</summary>
    private void BetTargetButton_OnClicked(object? sender, EventArgs e)
    {
        if (_roundInProgress)
        {
            return;
        }

        _bettingTarget = _bettingTarget == BettingTarget.MainBet ? BettingTarget.WarBet : BettingTarget.MainBet;
        BetTargetButton.Text = _bettingTarget == BettingTarget.MainBet ? "Betting: Main Bet" : "Betting: War Bet";
    }

    /// <summary>
    /// Shoves the entire balance (clamped to the table max) onto the single
    /// hand's bet - only wired up/visible when playing one hand at a time,
    /// since splitting "everything" across several independently-sized
    /// hands wouldn't have one obvious meaning.
    /// </summary>
    private void AllInButton_OnClicked(object? sender, EventArgs e)
    {
        if (_roundInProgress || _handCount != 1)
        {
            return;
        }

        if (_needsTableClearOnNextBet)
        {
            ClearTableForNewRound();
        }

        var slot = _hands[_selectedBetIndex];
        var allInAmount = Math.Min(_wallet.Balance, ChipWallet.TableMaximum);

        if (allInAmount < ChipWallet.TableMinimum)
        {
            ResultLabel.Text = "Not enough chips to go all in.";
            return;
        }

        slot.Bet = (int)allInAmount;
        UpdateHandSlotBetDisplay(_selectedBetIndex);
        UpdateTotalWageredText();
        ResultLabel.Text = allInAmount >= ChipWallet.TableMaximum
            ? $"Table maximum is ${ChipWallet.TableMaximum:N0} per hand."
            : "";
    }

    private async void SettingsButton_OnClicked(object? sender, EventArgs e)
    {
        var settingsPage = new SettingsPage(_variant, _deckCount, _handCount);
        settingsPage.SettingsSaved += OnSettingsSaved;
        await Navigation.PushModalAsync(settingsPage);
    }

    private void OnSettingsSaved(IGameVariant variant, int deckCount, int handCount)
    {
        if (_roundInProgress)
        {
            // Never swap the rules (or the number of hand slots) out from
            // under a round that's already dealt - queue it instead and
            // apply it once EndRound finishes this round.
            _pendingVariant = variant;
            _pendingDeckCount = deckCount;
            _pendingHandCount = handCount;
            ResultLabel.Text = "Settings saved - will apply once this round finishes.";
            return;
        }

        ApplyVariantChange(variant, deckCount, handCount);
    }

    private void ApplyVariantChange(IGameVariant variant, int deckCount, int handCount)
    {
        _variant = variant;
        _deckCount = deckCount;
        _deck = null; // force a fresh shoe built at the new deck count on the next Deal

        if (handCount != _handCount)
        {
            _handCount = handCount;
            BuildHandSlots(); // also refreshes War UI visibility for the new variant
        }
        else
        {
            UpdateWarUiVisibility();
        }

        UpdateVariantLabel();
        UpdateTableColors();
    }

    private void DealButton_OnClicked(object? sender, EventArgs e)
    {
        if (_roundInProgress)
        {
            return;
        }

        if (_hands.Any(h => h.Bet <= 0))
        {
            ResultLabel.Text = "Place a bet on every hand before dealing.";
            return;
        }

        if (_hands.Any(h => !ChipWallet.IsWithinTableLimits(h.Bet)))
        {
            ResultLabel.Text = $"Bets must be between ${ChipWallet.TableMinimum:N0} and ${ChipWallet.TableMaximum:N0} per hand.";
            return;
        }

        // War bets are optional (0 skips the side bet entirely for that
        // hand), but any non-zero War bet still has to sit within the same
        // table limits as a normal bet.
        if (_variant is WarBlackjackVariant && _hands.Any(h => h.WarBet > 0 && !ChipWallet.IsWithinTableLimits(h.WarBet)))
        {
            ResultLabel.Text = $"War bets must be between ${ChipWallet.TableMinimum:N0} and ${ChipWallet.TableMaximum:N0} per hand.";
            return;
        }

        var totalBet = _hands.Sum(h => h.Bet + h.WarBet);

        // Snapshot before the deduction, so the round's total (War Blackjack
        // only) can just be "balance now minus this" at any point, rather
        // than manually re-deriving it as War/blackjack money moves around.
        _walletBalanceAtRoundStart = _wallet.Balance;

        if (!_wallet.TryDeduct(totalBet))
        {
            ResultLabel.Text = "You don't have enough chips for that bet.";
            return;
        }

        // Build a fresh shoe the first time, once it's running low (scaled
        // by how many hands are being dealt this round), or once the deck
        // count changed in Settings. Reshuffling only between rounds
        // (never mid-round) keeps this simple for now.
        if (_deck is null || _deck.CardsRemaining < 15 * _handCount)
        {
            _deck = new Deck(numberOfDecks: _deckCount);
        }

        // Fresh hands for the new round, keeping each slot's bet(s).
        var bets = _hands.Select(h => (h.Bet, h.WarBet)).ToList();
        _hands = bets.Select(b => new PlayerHandSlot { Bet = b.Bet, WarBet = b.WarBet }).ToList();
        _dealerHand = new Hand();

        _roundInProgress = true;
        ResultLabel.Text = "";
        UpdateBalanceText();
        UpdateTotalWageredText();
        SetRoundInProgress(true);

        if (_variant is WarBlackjackVariant warVariant)
        {
            // Nothing to act on yet - the War card(s) have to be dealt and
            // any winning War bets decided before real blackjack play starts.
            HitButton.IsEnabled = false;
            StandButton.IsEnabled = false;
            DoubleButton.IsEnabled = false;
            SplitButton.IsEnabled = false;
            StartWarPhase(warVariant);
        }
        else
        {
            DealOpeningHandsAndStartPlay();
        }
    }

    /// <summary>Non-War path: deals every hand's normal two-card opening hand in one shot and starts play (or resolves immediately on an all-blackjack round).</summary>
    private void DealOpeningHandsAndStartPlay()
    {
        if (_deck is null)
        {
            return;
        }

        _variant.DealDealerOpeningHand(_deck, _dealerHand);

        foreach (var slot in _hands)
        {
            _variant.DealPlayerOpeningHand(_deck, slot.Hand);

            // A natural blackjack finishes this hand immediately, but other
            // hands may still need to play - it just gets skipped in turn
            // order and resolved together with everyone else in EndRound.
            if (slot.Hand.IsBlackjack)
            {
                slot.IsFinished = true;
            }
        }

        RenderAllHandSlots(hideHoleCard: true);
        _activeHandIndex = _hands.FindIndex(h => !h.IsFinished);

        if (_activeHandIndex == -1)
        {
            EndRound();
        }
        else
        {
            RefreshHandSlotHighlights();
        }
    }

    /// <summary>
    /// War path, phase 1: deals the one shared dealer War card and each
    /// hand's own War card, immediately settles any hand that lost or tied
    /// its War bet (dealer wins ties), and queues up a Press-or-Cash-Out
    /// decision for every hand that won one.
    /// </summary>
    private void StartWarPhase(WarBlackjackVariant warVariant)
    {
        if (_deck is null)
        {
            return;
        }

        warVariant.DealDealerWarCard(_deck, _dealerHand);

        foreach (var slot in _hands)
        {
            warVariant.DealPlayerWarCard(_deck, slot.Hand);
        }

        RenderAllHandSlots(hideHoleCard: false); // no hole card exists yet - both War cards are shown face-up

        _warDecisionQueue.Clear();

        for (var i = 0; i < _hands.Count; i++)
        {
            var slot = _hands[i];

            if (slot.WarBet <= 0)
            {
                continue;
            }

            if (warVariant.PlayerWinsWar(slot.Hand, _dealerHand))
            {
                _warDecisionQueue.Enqueue(i);
            }
            else
            {
                // Lost or tied - the War stake is simply forfeited; it was
                // already deducted from the balance up front, so there's no
                // further wallet change here.
                slot.ResultText = $"War: lost ${slot.WarBet:N0}. ";
                RenderHandSlot(i);
            }
        }

        // The Total display intentionally doesn't refresh here (or anywhere
        // else mid-round) - every hand's main bet is still deducted and
        // "in the air" until blackjack plays out, so any total taken now
        // would just show the whole round's stake as a loss before the
        // round has actually finished. It stays at the "$0" it was reset to
        // when betting started, and only shows the real result once
        // EndRound resolves the whole round.
        AdvanceWarDecisionQueue();
    }

    /// <summary>Moves to the next hand with a pending War decision, or - once the queue is empty - deals every hand's second card and starts real blackjack play.</summary>
    private void AdvanceWarDecisionQueue()
    {
        if (_warDecisionQueue.Count == 0)
        {
            _pendingWarDecisionHandIndex = -1;
            PressWarButton.IsVisible = false;
            CashOutWarButton.IsVisible = false;
            ResultLabel.Text = "";
            DealOpeningSecondCardsAndStartPlay();
            return;
        }

        _pendingWarDecisionHandIndex = _warDecisionQueue.Dequeue();
        var slot = _hands[_pendingWarDecisionHandIndex];

        ResultLabel.Text = _hands.Count > 1
            ? $"Hand {_pendingWarDecisionHandIndex + 1} won the War (+${slot.WarBet:N0})! Press it into your bet, or cash out?"
            : $"You won the War (+${slot.WarBet:N0})! Press it into your bet, or cash out?";

        _activeHandIndex = _pendingWarDecisionHandIndex;
        RefreshHandSlotHighlights();
        PressWarButton.IsVisible = true;
        CashOutWarButton.IsVisible = true;
    }

    /// <summary>Folds this hand's War stake plus its 1:1 winnings straight into its blackjack bet, putting it at risk for the rest of the round instead of banking it.</summary>
    private void PressWarButton_OnClicked(object? sender, EventArgs e)
    {
        if (_pendingWarDecisionHandIndex < 0)
        {
            return;
        }

        var slot = _hands[_pendingWarDecisionHandIndex];
        var warWinnings = slot.WarBet; // War pays 1:1
        slot.Bet += slot.WarBet + warWinnings;
        slot.ResultText = $"War: won ${warWinnings:N0} - pressed into bet. ";

        // Pressed winnings aren't realized profit yet - that money is now
        // just part of the (larger) blackjack bet, still at risk. The round
        // total only reflects it once EndRound resolves that bigger bet.
        UpdateHandSlotBetDisplay(_pendingWarDecisionHandIndex);
        UpdateTotalWageredText();
        RenderHandSlot(_pendingWarDecisionHandIndex);

        AdvanceWarDecisionQueue();
    }

    /// <summary>Banks this hand's War stake plus its 1:1 winnings straight into the balance, leaving the blackjack bet untouched.</summary>
    private void CashOutWarButton_OnClicked(object? sender, EventArgs e)
    {
        if (_pendingWarDecisionHandIndex < 0)
        {
            return;
        }

        var slot = _hands[_pendingWarDecisionHandIndex];
        var warWinnings = slot.WarBet; // War pays 1:1
        _wallet.Add(slot.WarBet + warWinnings);
        slot.ResultText = $"War: won ${warWinnings:N0} - cashed out. ";

        // The Total display intentionally doesn't refresh here either - see
        // the matching note in StartWarPhase. It only shows the real
        // combined result once EndRound resolves the whole round.
        UpdateBalanceText();
        RenderHandSlot(_pendingWarDecisionHandIndex);

        AdvanceWarDecisionQueue();
    }

    /// <summary>War path, phase 2: once every War decision is settled, deals the dealer's and each hand's second card and starts real blackjack play (or resolves immediately on an all-blackjack round).</summary>
    private void DealOpeningSecondCardsAndStartPlay()
    {
        if (_deck is null || _variant is not WarBlackjackVariant warVariant)
        {
            return;
        }

        warVariant.DealDealerSecondCard(_deck, _dealerHand);

        foreach (var slot in _hands)
        {
            warVariant.DealPlayerSecondCard(_deck, slot.Hand);

            if (slot.Hand.IsBlackjack)
            {
                slot.IsFinished = true;
            }
        }

        RenderAllHandSlots(hideHoleCard: true);
        _activeHandIndex = _hands.FindIndex(h => !h.IsFinished);

        if (_activeHandIndex == -1)
        {
            EndRound();
        }
        else
        {
            SetRoundInProgress(true); // re-enables Hit/Stand/Double/Split now that real play begins
            RefreshHandSlotHighlights();
        }
    }

    private void HitButton_OnClicked(object? sender, EventArgs e)
    {
        if (!_roundInProgress || _deck is null || _activeHandIndex < 0)
        {
            return;
        }

        var slot = _hands[_activeHandIndex];

        if (!_variant.CanHit(slot.Hand))
        {
            ResultLabel.Text = "This hand can't be hit again.";
            return;
        }

        _variant.Hit(_deck, slot.Hand);
        RenderHandSlot(_activeHandIndex);
        ContinueOrAdvance(cameFromDouble: false);
    }

    private void StandButton_OnClicked(object? sender, EventArgs e)
    {
        if (!_roundInProgress || _deck is null || _activeHandIndex < 0)
        {
            return;
        }

        _hands[_activeHandIndex].IsFinished = true;
        AdvanceToNextHandOrEndRound();
    }

    private void DoubleButton_OnClicked(object? sender, EventArgs e)
    {
        if (!_roundInProgress || _deck is null || _activeHandIndex < 0)
        {
            return;
        }

        var slot = _hands[_activeHandIndex];

        if (!_variant.CanDoubleDown(slot.Hand))
        {
            ResultLabel.Text = "Can't double down on this hand right now.";
            return;
        }

        // Double Down Madness allows doubling repeatedly, each based on the
        // current wager, so an unchecked run can blow past the table max
        // fast ($10 -> $20 -> $40 -> $80 -> ...). Block the double outright
        // once doubling again would exceed it.
        var doubledBet = slot.Bet * 2;

        if (!ChipWallet.IsWithinTableLimits(doubledBet))
        {
            ResultLabel.Text = $"Doubling would exceed the table max of ${ChipWallet.TableMaximum:N0}.";
            return;
        }

        if (!_wallet.TryDeduct(slot.Bet))
        {
            ResultLabel.Text = "Not enough chips to double down.";
            return;
        }

        slot.Bet = doubledBet;
        UpdateBalanceText();
        UpdateHandSlotBetDisplay(_activeHandIndex);
        UpdateTotalWageredText();

        _variant.Hit(_deck, slot.Hand);
        RenderHandSlot(_activeHandIndex);
        ContinueOrAdvance(cameFromDouble: true);
    }

    /// <summary>
    /// Splits the active hand's pair into two separate hands, each getting
    /// one new card and its own equal-sized bet (an additional deduction
    /// from the wallet). The new hand is inserted right after the original
    /// in turn order, so play naturally continues into it once the first
    /// half is done - no other turn-advancing code needs to know splitting
    /// happened at all. Only one split per hand is offered (no re-splitting
    /// a hand that already came from a split), and splitting Aces follows
    /// the standard rule: each Ace hand gets exactly one more card and is
    /// locked immediately, with no further hitting or doubling.
    /// </summary>
    private void SplitButton_OnClicked(object? sender, EventArgs e)
    {
        if (!_roundInProgress || _deck is null || _activeHandIndex < 0)
        {
            return;
        }

        var slot = _hands[_activeHandIndex];

        if (slot.HasBeenSplit)
        {
            ResultLabel.Text = "This hand has already been split.";
            return;
        }

        if (!_variant.CanSplit(slot.Hand))
        {
            ResultLabel.Text = "This hand can't be split.";
            return;
        }

        if (!_wallet.TryDeduct(slot.Bet))
        {
            ResultLabel.Text = "Not enough chips to split this hand.";
            return;
        }

        var wasSplittingAces = slot.Hand.Cards[0].Rank == Rank.Ace;
        var secondCard = slot.Hand.TakeSecondCardForSplit();

        var newSlot = new PlayerHandSlot { Bet = slot.Bet, HasBeenSplit = true };
        newSlot.Hand.AddCard(secondCard);
        slot.HasBeenSplit = true;

        // One more card each, completing both hands back to two cards.
        _variant.Hit(_deck, slot.Hand);
        _variant.Hit(_deck, newSlot.Hand);

        if (wasSplittingAces)
        {
            slot.IsFinished = true;
            newSlot.IsFinished = true;
        }

        var insertIndex = _activeHandIndex + 1;
        _hands.Insert(insertIndex, newSlot);

        var (border, view) = CreateHandSlotView(insertIndex);
        _handSlotViews.Insert(insertIndex, view);
        PlayerHandsLayout.Children.Insert(insertIndex, border);

        UpdateBalanceText();
        UpdateHandSlotBetDisplay(_activeHandIndex);
        UpdateHandSlotBetDisplay(insertIndex);
        UpdateTotalWageredText();
        RenderHandSlot(_activeHandIndex);
        RenderHandSlot(insertIndex);

        if (wasSplittingAces)
        {
            // Both halves are already locked - hand the turn straight to
            // whatever comes next instead of leaving play on a hand that
            // can't act (ContinueOrAdvance would re-derive "finished" from
            // CanHit/CanDoubleDown, which don't know about the Ace-split
            // lock, so it has to be the direct turn-advance call instead).
            ResultLabel.Text = "";
            AdvanceToNextHandOrEndRound();
        }
        else
        {
            ResultLabel.Text = "";
            RefreshHandSlotHighlights();
        }
    }

    /// <summary>
    /// Shared post-Hit/post-Double bookkeeping for the active hand: ends
    /// its turn on a bust, ends it if this variant's rule says doubling
    /// finishes the turn, and otherwise ends it once the hand can neither
    /// hit nor double any further (e.g. Double Down Madness's Ace-opener
    /// lock finally closing). Either way, play then advances to the next
    /// unfinished hand, or to EndRound if this was the last one.
    /// </summary>
    private void ContinueOrAdvance(bool cameFromDouble)
    {
        var slot = _hands[_activeHandIndex];

        if (slot.Hand.IsBust)
        {
            slot.IsFinished = true;
            AdvanceToNextHandOrEndRound();
            return;
        }

        if (cameFromDouble && _variant.EndsTurnAfterDouble)
        {
            slot.IsFinished = true;
            AdvanceToNextHandOrEndRound();
            return;
        }

        if (!_variant.CanHit(slot.Hand) && !_variant.CanDoubleDown(slot.Hand))
        {
            slot.IsFinished = true;
            AdvanceToNextHandOrEndRound();
        }
    }

    private void AdvanceToNextHandOrEndRound()
    {
        var nextIndex = -1;

        for (var i = _activeHandIndex + 1; i < _hands.Count; i++)
        {
            if (!_hands[i].IsFinished)
            {
                nextIndex = i;
                break;
            }
        }

        if (nextIndex == -1)
        {
            EndRound();
        }
        else
        {
            _activeHandIndex = nextIndex;
            RefreshHandSlotHighlights();
        }
    }

    /// <summary>
    /// Plays out the dealer's hand once (skipped only if every player hand
    /// already busted), then resolves each hand's own outcome and payout
    /// against that one shared dealer hand, applies every hand's result to
    /// the balance, and resets the table so a new round of bets can be placed.
    /// </summary>
    private void EndRound()
    {
        if (_deck is null)
        {
            return;
        }

        if (_hands.Any(h => !h.Hand.IsBust))
        {
            _variant.PlayDealerHand(_deck, _dealerHand);
        }

        foreach (var slot in _hands)
        {
            var outcome = _variant.DetermineOutcome(slot.Hand, _dealerHand);
            var payout = _variant.ResolvePayout(slot.Hand, _dealerHand, slot.Bet);

            // A 21 made from a split hand doesn't get the 3:2 blackjack
            // bonus, per standard casino rules - only the original two-card
            // deal counts as a "natural". Pay it the same as a normal win.
            if (slot.HasBeenSplit && outcome == RoundOutcome.PlayerBlackjack)
            {
                outcome = RoundOutcome.PlayerWin;
                payout = slot.Bet;
            }

            // The original bet was already deducted up front, so returning
            // to the balance means giving back the bet itself plus/minus payout.
            _wallet.Add(slot.Bet + payout);
            slot.ResultText += DescribeOutcome(outcome, payout);
            slot.IsFinished = true;
        }

        // The round's true net win/loss - computed from the actual wallet
        // movement since the snapshot taken at Deal, rather than re-derived
        // by summing payouts by hand, since a pressed War win blurs "War
        // money" into "blackjack money" and a hand-by-hand sum would either
        // double-count or drop part of it.
        var totalNet = _wallet.Balance - _walletBalanceAtRoundStart;

        if (_variant is WarBlackjackVariant)
        {
            // WarTotalLabel is the one true round total for War - War side
            // bets plus every hand's blackjack payout, single hand or
            // multiple - so there's no separate multi-hand total to show.
            UpdateWarTotalLabel();
            TotalResultLabel.IsVisible = false;
            TotalResultLabel.Text = "";
        }
        else if (_hands.Count > 1)
        {
            // Only worth showing for the other variants when there's more
            // than one hand to sum across - a single hand's own result
            // label already says the same thing.
            TotalResultLabel.IsVisible = true;
            TotalResultLabel.Text = totalNet switch
            {
                > 0 => $"Total: won ${totalNet:N0}",
                < 0 => $"Total: lost ${-totalNet:N0}",
                _ => "Total: broke even",
            };
        }
        else
        {
            TotalResultLabel.IsVisible = false;
            TotalResultLabel.Text = "";
        }

        UpdateBalanceText();
        RenderAllHandSlots(hideHoleCard: false);

        _roundInProgress = false;
        _activeHandIndex = -1;

        foreach (var slot in _hands)
        {
            slot.Bet = 0;
            slot.WarBet = 0;
        }

        UpdateTotalWageredText();
        for (var i = 0; i < _hands.Count; i++)
        {
            UpdateHandSlotBetDisplay(i);
        }

        SetRoundInProgress(false);
        _selectedBetIndex = 0;
        RefreshHandSlotHighlights();

        // The finished round's cards/results stay visible for review until
        // the player actually starts betting on the next one.
        _needsTableClearOnNextBet = true;

        if (_pendingVariant is not null && _pendingDeckCount is not null && _pendingHandCount is not null)
        {
            ApplyVariantChange(_pendingVariant, _pendingDeckCount.Value, _pendingHandCount.Value);
            _pendingVariant = null;
            _pendingDeckCount = null;
            _pendingHandCount = null;
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
    /// Wipes the previous round's cards, values, and result text off the
    /// table so it looks like a fresh game as soon as the player starts
    /// betting again. Only touches the display - the underlying hands are
    /// already rebuilt fresh in DealButton_OnClicked regardless.
    /// </summary>
    private void ClearTableForNewRound()
    {
        DealerCardsLayout.Children.Clear();
        DealerValueLabel.Text = "";
        ResultLabel.Text = "";
        TotalResultLabel.IsVisible = false;
        TotalResultLabel.Text = "";

        // A hard reset to "$0" rather than a fresh UpdateWarTotalLabel() call -
        // that call would just recompute from the still-stale round-start
        // snapshot and redisplay the PREVIOUS round's final total.
        // _walletBalanceAtRoundStart itself gets a real reset in
        // DealButton_OnClicked once the next round actually starts.
        if (_variant is WarBlackjackVariant)
        {
            WarTotalLabel.Text = "Total: $0";
        }

        if (_hands.Count != _handCount)
        {
            // A hand got split last round, leaving an extra hand slot beyond
            // the normal 1-5 seats - BuildHandSlots collapses the table
            // back down to exactly _handCount fresh ones (it also resets
            // _needsTableClearOnNextBet itself).
            BuildHandSlots();
            return;
        }

        foreach (var view in _handSlotViews)
        {
            view.CardsLayout.Children.Clear();
            view.ValueLabel.Text = "";
            view.ResultLabel.Text = "";
        }

        _needsTableClearOnNextBet = false;
    }

    private void RenderAllHandSlots(bool hideHoleCard)
    {
        RenderDealerHand(hideHoleCard);

        for (var i = 0; i < _hands.Count; i++)
        {
            RenderHandSlot(i);
        }
    }

    /// <summary>
    /// Redraws the dealer's cards from current game state. While
    /// hideHoleCard is true, the dealer's second card is shown face-down
    /// and only the upcard's value is displayed.
    /// </summary>
    private void RenderDealerHand(bool hideHoleCard)
    {
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

    /// <summary>Redraws one hand slot's cards and value from current game state. The player's own cards are always shown face-up.</summary>
    private void RenderHandSlot(int index)
    {
        var slot = _hands[index];
        var view = _handSlotViews[index];

        view.CardsLayout.Children.Clear();
        foreach (var card in slot.Hand.Cards)
        {
            view.CardsLayout.Children.Add(CreateCardImage(CardImageFile(card), HandSlotCardHeight));
        }

        view.ValueLabel.Text = slot.Hand.Cards.Count > 0 ? $"Value: {slot.Hand.GetBestValue().Value}" : "";
        view.ResultLabel.Text = slot.ResultText;
    }

    /// <summary>Updates one hand slot's bet label and chip-denomination image, purely for display.</summary>
    private void UpdateHandSlotBetDisplay(int index)
    {
        var slot = _hands[index];
        var view = _handSlotViews[index];
        view.BetLabel.Text = $"Bet: ${slot.Bet:N0}";
        view.WarBetLabel.Text = $"War: ${slot.WarBet:N0}";

        if (slot.Bet <= 0)
        {
            view.ChipImage.Source = null;
            return;
        }

        var chipValue = ChipWallet.Denominations.Reverse().FirstOrDefault(d => d <= slot.Bet, 1m);
        view.ChipImage.Source = ImageSource.FromFile($"chip_{(int)chipValue}.png");
    }

    private void UpdateTotalWageredText() => CurrentBetLabel.Text = $"Total Wagered: ${_hands.Sum(h => h.Bet + h.WarBet):N0}";

    private void SetRoundInProgress(bool roundInProgress)
    {
        DealButton.IsEnabled = !roundInProgress;
        HitButton.IsEnabled = roundInProgress;
        StandButton.IsEnabled = roundInProgress;
        DoubleButton.IsEnabled = roundInProgress;
        SplitButton.IsEnabled = roundInProgress;
        ChipButtonsLayout.IsEnabled = !roundInProgress;
    }

    private void UpdateBalanceText() => BalanceLabel.Text = $"Balance: ${_wallet.Balance:N0}";

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
