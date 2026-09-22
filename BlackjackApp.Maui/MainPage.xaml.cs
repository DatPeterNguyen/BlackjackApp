using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlackjackApp.core.Economy;
using BlackjackApp.core.Models;
using BlackjackApp.core.Services;
using BlackjackApp.core.Variants;
using BlackjackApp.Maui.Services;
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
/// Turn order: hands play in the same order they were dealt in - rightmost
/// hand slot first (dealt first, same as a real dealer starting at their
/// own left), moving down to the leftmost, dealer last. _activeHandIndex is
/// whichever slot Hit/Stand/Double currently apply to; when that hand
/// finishes (bust, stand, doubled-and-done, or can no longer act), play
/// advances to the next unfinished hand (the next lower index). Once every
/// hand is finished, EndRound plays the dealer once and resolves every
/// hand's outcome/payout against that one dealer hand.
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

    /// <summary>Set when Table Settings is saved mid-round - applied once EndRound finishes this round instead of immediately, so an in-progress round never has its hand count swapped out from under it.</summary>
    private int? _pendingDeckCount;
    private int? _pendingHandCount;

    private Deck? _deck;
    private Hand _dealerHand = new();

    /// <summary>Owns the balance and enforces the table's $1-$10,000 per-hand betting limits (see ChipWallet). Starting balance is whatever was last saved (see GameProgressStorage), or the $1,000 default for a brand new player.</summary>
    private readonly ChipWallet _wallet = new(GameProgressStorage.LoadBalance());

    /// <summary>The player's lifetime win/loss/push record and profit tracking (item 13 on the polish list) - loaded from the same saved progress as the wallet, and persisted again (see GameProgressStorage) every time a round finishes.</summary>
    private readonly GameStats _stats = GameProgressStorage.LoadStats();

    /// <summary>One entry per active hand slot (1-5), rebuilt whenever the hand count changes.</summary>
    private List<PlayerHandSlot> _hands = new();

    /// <summary>The dynamically-built UI views for each hand slot, index-matched to _hands.</summary>
    private readonly List<HandSlotView> _handSlotViews = new();

    /// <summary>Which hand slot chip taps/Clear currently target, before a round starts.</summary>
    private int _selectedBetIndex;

    /// <summary>
    /// The chip denomination currently "held" via tap-to-bet (null when
    /// none is held) - the universal fallback for drag-and-drop betting
    /// (see ChipImage_OnTapped, PlaceChipOnHandAsync, and the tap handler
    /// wired up in CreateHandSlotView). Tapping a chip toggles it held or
    /// not; tapping a hand slot while one is held places it there and
    /// leaves it held, so the same chip can be placed on several hands in a
    /// row without re-tapping the tray each time. Cleared whenever a new
    /// round is dealt.
    /// </summary>
    private decimal? _heldChipDenomination;

    /// <summary>True for the duration of a custom chip drag gesture (see ChipImage_OnPanUpdated) - from the finger going down on a chip to it lifting back up.</summary>
    private bool _isDraggingChip;

    /// <summary>The dragged chip's absolute starting position (in RootLayout's coordinate space) for this drag - the drag ghost's translation is always this origin plus the gesture's running total delta, and it's also where the ghost glides back to on a miss.</summary>
    private Point _dragGhostOrigin;

    /// <summary>Which hand slot the drag ghost is currently hovering over, or -1 for none - set by UpdateDragHoverHighlight, read by ChipImage_OnPanUpdated on release to decide where the chip lands.</summary>
    private int _dragHoverHandIndex = -1;

    /// <summary>War Blackjack only: true when _dragHoverHandIndex's WAR bet border (rather than its main bet border) is what the drag ghost is currently over - see UpdateDragHoverHighlight/CreateHandSlotView.</summary>
    private bool _dragHoverIsWar;

    /// <summary>Each hand slot's absolute Border/WarBorder hit-test bounds, snapshotted once per drag by CacheDragHitTestBounds - this geometry is static for the whole duration of a drag, so UpdateDragHoverHighlight (called on every high-frequency PanGestureRecognizer "Running" tick) reads from here instead of re-walking the visual tree on every call.</summary>
    private readonly List<(Rect MainBounds, Rect? WarBounds)> _dragHitTestBounds = new();

    /// <summary>Which hand slot Hit/Stand/Double currently apply to; -1 when no round is in progress.</summary>
    private int _activeHandIndex = -1;

    private bool _roundInProgress;

    /// <summary>War Blackjack only: hand indices whose War card beat the dealer's and are waiting on a Press-or-Cash-Out choice, processed one at a time.</summary>
    private readonly Queue<int> _warDecisionQueue = new();

    /// <summary>War Blackjack only: the hand a Press/Cash Out tap currently applies to; -1 when no War decision is pending.</summary>
    private int _pendingWarDecisionHandIndex = -1;

    /// <summary>War Blackjack only: true if any hand actually placed a War bet this round (whether it went on to win or lose) - set fresh in StartWarPhase, read by AdvanceWarDecisionQueue to decide whether the table pauses (see WarResultPauseBeforeSecondCards) before dealing the second cards. A round nobody bet War on has no War result worth pausing for.</summary>
    private bool _anyHandBetWarThisRound;

    /// <summary>Standard Blackjack only: true while the table is waiting on the player's Insure/No Insurance decision after an opening deal where the dealer shows an Ace - see PromptForInsurance/ResolveInsuranceDecision.</summary>
    private bool _insuranceDecisionPending;

    /// <summary>War Blackjack only: true if any hand actually won the War this round (i.e. got a Press/Cash-Out decision) - set fresh in StartWarPhase alongside _anyHandBetWarThisRound, used only to pick the right message to hold on screen during that pause (a win's own Press/Cash-Out result vs. a plain "Lost the War" summary when every War bet lost).</summary>
    private bool _anyHandWonWarThisRound;

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

    /// <summary>
    /// True once the just-finished round's cards have actually been moved
    /// into the deck's discard pile - either by the 5-second auto-discard
    /// below, or (if the player deals again before that timer fires) by
    /// DealButton_OnClicked's own fallback. Starts true (nothing to
    /// discard yet); guards against ever discarding the same round's cards
    /// twice.
    /// </summary>
    private bool _roundCardsDiscarded = true;

    /// <summary>Cancels the pending 5-second auto-discard if a new round starts before it fires.</summary>
    private CancellationTokenSource? _autoDiscardCts;

    /// <summary>
    /// Bumped every time a new round actually starts dealing. Lets a
    /// still-in-flight discard-to-pile animation tell, once it finishes,
    /// whether a new round's cards have since been dealt onto the table -
    /// if so, it skips wiping the display so it doesn't erase them.
    /// </summary>
    private int _roundGeneration;

    // ---- Table metrics -------------------------------------------------
    //
    // These were fixed constants, sized against a desktop window. They cannot
    // be: the table needs roughly 500pt of height at those sizes and the
    // largest iPhone offers 409 in landscape, so on any phone the seats were
    // squeezed or clipped off the bottom. They are now fields, recomputed by
    // ApplyAdaptiveMetrics whenever the page is resized.
    //
    // Full sizes are the desktop/tablet values; compact ones are what a phone
    // in landscape can actually afford.

    // Three tiers, not two. Portrait is its own case and deliberately the
    // most generous: a phone held upright has over 800pt of height against
    // under 400 on its side, and that headroom is the whole reason the type
    // can be readable there. Landscape on a phone stays the tightest
    // arrangement, because it genuinely is the tightest space.

    private const double DealerCardHeightFull = 130;
    private const double DealerCardHeightPortrait = 118;
    private const double DealerCardHeightCompact = 72;

    private const double HandSlotCardHeightFull = 56;
    private const double HandSlotCardHeightPortrait = 62;
    private const double HandSlotCardHeightCompact = 40;

    private const double HandSlotCardsWidthFull = 140;
    private const double HandSlotCardsWidthPortrait = 152;
    private const double HandSlotCardsWidthCompact = 116;

    /// <summary>Below this height in device-independent points the table switches to compact metrics. An iPhone 15 is 393pt tall in landscape; an iPad is 834.</summary>
    private const double CompactHeightThreshold = 500;

    /// <summary>Whether the table is currently laid out for a short screen.</summary>
    private bool _compact;

    /// <summary>Whether the screen is taller than it is wide, which on a phone is the arrangement worth optimising for.</summary>
    private bool _portrait;

    /// <summary>Dealer card image height - see the metrics block above.</summary>
    private double DealerCardHeight => _portrait ? DealerCardHeightPortrait : _compact ? DealerCardHeightCompact : DealerCardHeightFull;

    /// <summary>Card image height inside a hand slot - smaller than the dealer's so several fit across one slot's width.</summary>
    private double HandSlotCardHeight => _portrait ? HandSlotCardHeightPortrait : _compact ? HandSlotCardHeightCompact : HandSlotCardHeightFull;

    /// <summary>Explicit width for a hand slot's card row - a Fill request alone wasn't reliably resolving to a real width inside the stack.</summary>
    private double HandSlotCardsWidth => _portrait ? HandSlotCardsWidthPortrait : _compact ? HandSlotCardsWidthCompact : HandSlotCardsWidthFull;

    /// <summary>Outer hand slot Border width - HandSlotCardsWidth plus room for its Padding/Border.</summary>
    private double HandSlotBorderWidth => HandSlotCardsWidth + 16;

    /// <summary>How far above the middle seat the outermost ones are lifted, to follow the curve of the table's near edge - see ApplyHandSlotArc.</summary>
    private const double HandSlotArcLift = 22;

    /// <summary>The house rules painted across the felt - re-pointed at the current variant's wording by UpdateTableColors.</summary>
    private readonly TableRulesDrawable _tableRules = new();

    /// <summary>
    /// Hand slot chrome, matching the CraftPix kit's player nameplates: a
    /// translucent black plate over the felt, ringed in gold while that
    /// seat is the one in play (or the one being bet on before the deal),
    /// and in the kit's button blue while a chip drag is hovering over it.
    /// These live here as named constants rather than inline literals
    /// because three different methods have to agree on them exactly -
    /// CreateHandSlotView paints the resting state,
    /// RefreshHandSlotHighlights restores it after a selection changes, and
    /// UpdateDragHoverHighlight restores it after a drag ends. When those
    /// three disagreed, a slot could be left stuck in a hover colour.
    /// </summary>
    private static readonly Color HandSlotFill = Color.FromArgb("#59000000");
    private static readonly Color HandSlotRestingStroke = Color.FromArgb("#6F7D55");
    private static readonly Color HandSlotSelectedStroke = Color.FromArgb("#FFC400");
    private static readonly Color HandSlotHoverStroke = Color.FromArgb("#10A1EB");
    private static readonly Color WarSlotFill = Color.FromArgb("#4D001B2E");
    private static readonly Color WarSlotRestingStroke = Color.FromArgb("#2E4A63");

    /// <summary>How long each dealt card's fly-in-from-the-shoe animation takes.</summary>
    private const uint CardDealAnimationDurationMs = 220;

    /// <summary>Pause after each card lands before the next one is dealt, so a deal reads as one card at a time instead of everything landing at once.</summary>
    private const int CardDealStaggerMs = 90;

    /// <summary>Cap on how many layout-yield attempts AnimateCardFromShoe will wait through for a just-added card's Bounds to become non-empty before giving up and animating with whatever it has - a single Task.Yield isn't reliably enough for a native measure/arrange pass to finish, especially inside nested layouts, so this polls a few frames instead of guessing wrong on some fraction of dealt cards.</summary>
    private const int MaxLayoutWaitAttempts = 10;

    /// <summary>Half the duration of the dealer's hole-card flip (see FlipDealerHoleCardFaceUp) - the card narrows to a sliver over this long, then widens back out over the same time again with the new face showing.</summary>
    private const uint CardFlipHalfDurationMs = 130;

    /// <summary>How long a finished round's cards stay on the table before they automatically fly off to the discard pile.</summary>
    private static readonly TimeSpan TableHoldBeforeDiscard = TimeSpan.FromSeconds(5);

    /// <summary>How long each card's fly-to-the-discard-pile animation takes.</summary>
    private const uint CardDiscardAnimationDurationMs = 260;

    /// <summary>Stagger between each card starting its fly-to-discard animation, so the whole table doesn't leave at once.</summary>
    private const int CardDiscardStaggerMs = 40;

    /// <summary>How long a hand's chip image takes to fade in when a bet is placed on it.</summary>
    private const uint ChipPlacedAnimationDurationMs = 180;

    /// <summary>War Blackjack only: how long to hold on a hand's War result (won/lost) before dealing the second cards and moving into real blackjack play - see AdvanceWarDecisionQueue/_anyHandWonWarThisRound. Gives a winning Press/Cash-Out tap's result text a beat to actually be read instead of the next cards immediately flying in on top of it.</summary>
    private static readonly TimeSpan WarResultPauseBeforeSecondCards = TimeSpan.FromSeconds(2);

    /// <summary>Parameterless overload kept for anything that still expects a default-constructible page (e.g. design-time tooling) - matches the field initializers' own defaults (Standard Blackjack, 4 decks, 1 hand).</summary>
    public MainPage() : this(new StandardBlackjackVariant(), 4, 1)
    {
    }

    /// <summary>
    /// The game now starts at GameMenuPage (see AppShell.xaml) - its Play
    /// button opens RulesPage as a mode picker, where tapping a variant
    /// selects it and RulesPage's own Play button confirms, landing here
    /// with whatever variant/deck/hand count the player picked.
    /// </summary>
    public MainPage(IGameVariant initialVariant, int initialDeckCount, int initialHandCount)
    {
        InitializeComponent();

        _variant = initialVariant;
        _deckCount = initialDeckCount;
        _handCount = initialHandCount;

        // Matches the wallet's actual starting balance so switching to War
        // before ever dealing a hand shows a real "$0" total instead of the
        // whole starting balance read as profit.
        _walletBalanceAtRoundStart = _wallet.Balance;

        TableLimitsLabel.Text = $"${ChipWallet.TableMinimum:N0} - ${ChipWallet.TableMaximum:N0}";

        BuildHandSlots();
        UpdateBalanceText();
        UpdateVariantLabel();
        UpdateTableColors();
        UpdateShoeDisplay();
    }

    /// <summary>
    /// Resumes a round that was still in progress when the app last closed
    /// - see InProgressRoundState, GameProgressStorage.LoadInProgressRound,
    /// and GameMenuPage's Load Game button (start menu only). Rebuilds the
    /// deck, dealer hand, every player hand slot, whose turn it is, and any
    /// pending War decision exactly as PersistInProgressRound saved them,
    /// then re-renders the table via the same RenderDealerHand/RenderHandSlot
    /// used everywhere else - no dealing animation is replayed.
    /// </summary>
    public MainPage(InProgressRoundState savedRound)
    {
        InitializeComponent();

        _variant = VariantFromKind(savedRound.VariantKind);
        _deckCount = savedRound.DeckCount;
        _handCount = savedRound.HandCount;

        TableLimitsLabel.Text = $"${ChipWallet.TableMinimum:N0} - ${ChipWallet.TableMaximum:N0}";

        _deck = Deck.Restore(savedRound.ShoeDeckCount, savedRound.ShoeRemainingCards, savedRound.ShoeDiscardedCards);

        _dealerHand = new Hand();
        foreach (var card in savedRound.DealerCards)
        {
            _dealerHand.AddCard(card);
        }

        _hands = savedRound.Hands.Select(saved =>
        {
            var slot = new PlayerHandSlot
            {
                Bet = saved.Bet,
                WarBet = saved.WarBet,
                IsFinished = saved.IsFinished,
                HasBeenSplit = saved.HasBeenSplit,
                ResultText = saved.ResultText,
                ResolvedEarly = saved.ResolvedEarly,
                HasPendingBlackjack = saved.HasPendingBlackjack,
                InsuranceBet = saved.InsuranceBet,
                InsuranceResultText = saved.InsuranceResultText,
            };

            foreach (var card in saved.Cards)
            {
                slot.Hand.AddCard(card);
            }

            return slot;
        }).ToList();

        _handSlotViews.Clear();
        PlayerHandsLayout.Children.Clear();
        for (var i = 0; i < _hands.Count; i++)
        {
            var (container, view) = CreateHandSlotView(i);
            _handSlotViews.Add(view);
            PlayerHandsLayout.Children.Add(container);
        }

        ApplyHandSlotArc();

        _activeHandIndex = savedRound.ActiveHandIndex;
        _selectedBetIndex = savedRound.SelectedBetIndex;

        _warDecisionQueue.Clear();
        foreach (var handIndex in savedRound.WarDecisionQueue)
        {
            _warDecisionQueue.Enqueue(handIndex);
        }

        _pendingWarDecisionHandIndex = savedRound.PendingWarDecisionHandIndex;
        _insuranceDecisionPending = savedRound.InsurancePending;
        _walletBalanceAtRoundStart = savedRound.WalletBalanceAtRoundStart;
        _roundInProgress = true;

        AllInButton.IsVisible = _handCount == 1;
        TotalResultLabel.IsVisible = false;
        TotalResultLabel.Text = "";
        _needsTableClearOnNextBet = false;
        _roundCardsDiscarded = true;

        UpdateBalanceText();
        UpdateVariantLabel();
        UpdateTableColors();
        UpdateShoeDisplay();
        UpdateWarUiVisibility();
        UpdateTotalWageredText();

        // The dealer's hole card is only ever revealed inside EndRound,
        // which never ran (or this wouldn't have been saved as "in
        // progress") - so it's always still hidden on resume.
        RenderDealerHand(hideHoleCard: true);
        for (var i = 0; i < _hands.Count; i++)
        {
            RenderHandSlot(i);
            UpdateHandSlotBetDisplay(i);
        }

        SetRoundInProgress(true);
        RefreshHandSlotHighlights();

        if (_pendingWarDecisionHandIndex >= 0)
        {
            var slot = _hands[_pendingWarDecisionHandIndex];
            ResultLabel.Text = _hands.Count > 1
                ? $"Hand {_pendingWarDecisionHandIndex + 1} won the War (+${slot.WarBet:N0})! Press it into your bet, or cash out?"
                : $"You won the War (+${slot.WarBet:N0})! Press it into your bet, or cash out?";
            PressWarButton.IsVisible = true;
            CashOutWarButton.IsVisible = true;
        }

        if (_insuranceDecisionPending)
        {
            ResultLabel.Text = "Dealer shows an Ace. Insurance for half your bet(s)?";
            InsuranceButton.IsVisible = true;
            DeclineInsuranceButton.IsVisible = true;
            RefreshActionButtonVisibility();
        }
    }

    /// <summary>"Standard", "DoubleDownMadness", or "War" - the compact, save-friendly identifier PersistInProgressRound stores for _variant (an IGameVariant instance itself isn't serializable).</summary>
    private static string VariantKind(IGameVariant variant) => variant switch
    {
        DoubleDownMadnessVariant => "DoubleDownMadness",
        WarBlackjackVariant => "War",
        _ => "Standard",
    };

    /// <summary>The inverse of VariantKind - rebuilds a fresh IGameVariant instance from its saved identifier when resuming a round.</summary>
    private static IGameVariant VariantFromKind(string kind) => kind switch
    {
        "DoubleDownMadness" => new DoubleDownMadnessVariant(),
        "War" => new WarBlackjackVariant(),
        _ => new StandardBlackjackVariant(),
    };

    /// <summary>
    /// Snapshots the entire in-progress round - deck, dealer hand, every
    /// hand slot, turn state, any pending War decision - to
    /// GameProgressStorage, so GameMenuPage's Load Game button can restore
    /// it exactly if the app gets closed mid-round. Called at the end of
    /// every player/War action that changes round state; a no-op once the
    /// round isn't in progress (EndRound explicitly clears the save
    /// instead, right where it sets _roundInProgress = false).
    /// </summary>
    private void PersistInProgressRound()
    {
        if (!_roundInProgress || _deck is null)
        {
            return;
        }

        var state = new InProgressRoundState
        {
            VariantKind = VariantKind(_variant),
            DeckCount = _deckCount,
            HandCount = _handCount,
            ShoeDeckCount = _deck.NumberOfDecks,
            ShoeRemainingCards = _deck.RemainingCardsSnapshot().ToList(),
            ShoeDiscardedCards = _deck.DiscardedCardsSnapshot().ToList(),
            DealerCards = _dealerHand.Cards.ToList(),
            Hands = _hands.Select(slot => new SavedHandSlot
            {
                Cards = slot.Hand.Cards.ToList(),
                Bet = slot.Bet,
                WarBet = slot.WarBet,
                IsFinished = slot.IsFinished,
                HasBeenSplit = slot.HasBeenSplit,
                ResultText = slot.ResultText,
                ResolvedEarly = slot.ResolvedEarly,
                HasPendingBlackjack = slot.HasPendingBlackjack,
                InsuranceBet = slot.InsuranceBet,
                InsuranceResultText = slot.InsuranceResultText,
            }).ToList(),
            ActiveHandIndex = _activeHandIndex,
            SelectedBetIndex = _selectedBetIndex,
            WarDecisionQueue = _warDecisionQueue.ToList(),
            PendingWarDecisionHandIndex = _pendingWarDecisionHandIndex,
            InsurancePending = _insuranceDecisionPending,
            WalletBalanceAtRoundStart = _walletBalanceAtRoundStart,
        };

        GameProgressStorage.SaveInProgressRound(state);
    }

    /// <summary>Bundles the dynamically-created views for one hand slot, since XAML can't name a variable (1-5) number of them.</summary>
    private sealed class HandSlotView
    {
        public required Border Border { get; init; }

        /// <summary>War Blackjack only: the small drop target below Border for this hand's own War side bet (see CreateHandSlotView) - hidden entirely for the other variants (see UpdateWarUiVisibility).</summary>
        public required Border WarBorder { get; init; }
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
            var (container, view) = CreateHandSlotView(i);
            _handSlotViews.Add(view);
            PlayerHandsLayout.Children.Add(container);
        }

        ApplyHandSlotArc();
        RefreshHandSlotHighlights();
        UpdateWarUiVisibility();
    }

    /// <summary>
    /// Moves the pieces that cannot simply shrink to fit a narrow screen.
    ///
    /// Landscape puts three things side by side across the top - shoe,
    /// dealer, limits and menu - which needs about 700pt. Portrait has 393.
    /// So in portrait the dealer drops into the strip's second row, beneath
    /// the other two, rather than being squeezed between them. The rail does
    /// the same: chips and action buttons sit side by side when there is room
    /// and stack when there is not.
    ///
    /// Both spare rows are Auto, so they collapse to nothing in landscape and
    /// this costs no height there.
    /// </summary>
    private void ApplyOrientationLayout()
    {
        // Dealer: overlaid across the whole strip in landscape, below the
        // shoe and menu row in portrait.
        Grid.SetRow(DealerStack, _portrait ? 1 : 0);
        Grid.SetRowSpan(DealerStack, _portrait ? 1 : 2);

        // Chips take the full width in portrait and wrap onto as many lines
        // as they need; the buttons move to their own row underneath.
        Grid.SetColumnSpan(ChipButtonsLayout, _portrait ? 2 : 1);

        Grid.SetRow(ActionButtonsLayout, _portrait ? 1 : 0);
        Grid.SetColumn(ActionButtonsLayout, 0);
        Grid.SetColumnSpan(ActionButtonsLayout, _portrait ? 2 : 1);

        if (!_portrait)
        {
            Grid.SetColumn(ActionButtonsLayout, 1);
        }

        ActionButtonsLayout.JustifyContent = _portrait ? FlexJustify.Center : FlexJustify.End;
        ChipButtonsLayout.JustifyContent = FlexJustify.Center;

        // The seats only follow the table's curve when they are on one line.
        // In portrait they wrap onto several, where a per-index lift is
        // meaningless - see ApplyHandSlotArc.
        ApplyHandSlotArc();
    }

    /// <summary>
    /// Re-picks the table's metrics for the space actually available, and
    /// rebuilds anything that was sized with the old ones.
    ///
    /// At full size the table needs roughly 500pt of height. A landscape
    /// iPhone offers between 354 and 409 once the home indicator is out, so
    /// on a phone the seats were being squeezed flat or pushed off the bottom
    /// entirely - the layout was only ever checked against a desktop window.
    /// Compact metrics shrink the cards, tighten the seat text and drop the
    /// decorative chip picture, which brings it inside what a phone has.
    ///
    /// Only does work when the mode actually flips, since it rebuilds every
    /// seat - OnSizeAllocated fires on every resize tick and on rotation.
    /// </summary>
    private void ApplyAdaptiveMetrics(double height)
    {
        if (height <= 0)
        {
            return;
        }

        // Portrait is decided by shape, not size. Compact is only ever about a
        // landscape table being short - a phone held upright has height to
        // spare, so it never wants the cramped arrangement.
        var shouldBePortrait = height > Width && Width > 0;
        var shouldBeCompact = !shouldBePortrait && height < CompactHeightThreshold;

        if (shouldBePortrait == _portrait && shouldBeCompact == _compact)
        {
            return;
        }

        _portrait = shouldBePortrait;
        _compact = shouldBeCompact;

        ApplyOrientationLayout();

        // The type has to come down with the artwork. These four carry the
        // most height of anything in the fixed chrome, and at desktop sizes
        // they alone put the table over what a phone has.
        DealerHeadingLabel.FontSize = _portrait ? 22 : _compact ? 14 : 17;
        DealerValueLabel.FontSize = _portrait ? 24 : _compact ? 15 : 18;
        BalanceLabel.FontSize = _portrait ? 26 : _compact ? 17 : 22;
        CurrentBetLabel.FontSize = _portrait ? 26 : _compact ? 17 : 22;
        VariantLabel.FontSize = _portrait ? 17 : _compact ? 12 : 15;
        ResultLabel.FontSize = _portrait ? 26 : _compact ? 17 : 21;

        // Seats bake their sizes in at construction, so they have to be built
        // again rather than adjusted. Bets and cards are held in _hands, not
        // in the views, so nothing about the round in progress is lost.
        RebuildHandSlotViews();

        // The dealer's cards are already on the table at the old height.
        RenderDealerHand(hideHoleCard: _roundInProgress);

        SizeChipTray();
    }

    /// <summary>
    /// Rebuilds every seat's view from the current metrics, preserving which
    /// hand is selected and which is being played.
    /// </summary>
    private void RebuildHandSlotViews()
    {
        _handSlotViews.Clear();
        PlayerHandsLayout.Children.Clear();

        for (var i = 0; i < _hands.Count; i++)
        {
            var (container, view) = CreateHandSlotView(i);
            _handSlotViews.Add(view);
            PlayerHandsLayout.Children.Add(container);
        }

        ApplyHandSlotArc();

        for (var i = 0; i < _hands.Count; i++)
        {
            RenderHandSlot(i);
            UpdateHandSlotBetDisplay(i);
        }

        RefreshHandSlotHighlights();
        UpdateWarUiVisibility();
    }

    /// <summary>
    /// Sizes the chips so the whole tray fits the width without scrolling.
    ///
    /// It must not scroll: the tray used to sit in a horizontal ScrollView,
    /// and a ScrollView's own pan gesture beats a child's PanGestureRecognizer
    /// on iOS and Android. That is why chips could be dragged with a mouse on
    /// Windows but only tapped on a phone - the drag never reached them.
    /// </summary>
    private void SizeChipTray()
    {
        var chips = ChipButtonsLayout.Children.OfType<Image>().ToList();

        if (chips.Count == 0 || Width <= 0)
        {
            return;
        }

        // What the row has to fit in: the page width, less its padding, less
        // the Clear/All In buttons and the action buttons sharing the rail.
        const double railFurniture = 300;
        var available = Width - 28 - railFurniture;
        var perChip = available / chips.Count;

        // Clamped: never so small it cannot be hit (44pt is the smallest
        // comfortable touch target), never larger than it looked on desktop.
        // 40pt floor: below that a chip stops being a reliable touch
        // target. The ceiling drops on a short screen, where the rail's
        // height is competing with the seats for the same points.
        var size = Math.Clamp(perChip - 8, 40, _compact ? 46 : 54);

        // FlexLayout has no Spacing, so the gap between chips is a margin.
        var gap = new Thickness(3);

        foreach (var chip in chips)
        {
            chip.WidthRequest = size;
            chip.HeightRequest = size;
            chip.Margin = gap;
        }
    }

    protected override void OnSizeAllocated(double width, double height)
    {
        base.OnSizeAllocated(width, height);

        ApplyAdaptiveMetrics(height);
        SizeChipTray();
    }

    /// <summary>
    /// Bends the row of seats into the table's own curve. The felt art is
    /// drawn in perspective with the rail dipping lowest at the centre, so
    /// the middle seat sits lowest and each one further out is lifted
    /// progressively higher to follow it.
    ///
    /// Done with bottom margins against PlayerHandsLayout's AlignItems="End"
    /// rather than with TranslationY, because the chip-drag hit-test
    /// measures each slot's layout Bounds (see GetAbsolutePosition) and a
    /// render transform wouldn't move those - the seats would look moved but
    /// still catch a dropped chip at their old positions.
    ///
    /// Re-walks every seat rather than positioning one, so it can just be
    /// called again after anything changes the row - including a split
    /// inserting a brand new hand into the middle of it.
    /// </summary>
    private void ApplyHandSlotArc()
    {
        var count = PlayerHandsLayout.Children.Count;

        for (var i = 0; i < count; i++)
        {
            if (PlayerHandsLayout.Children[i] is not View seat)
            {
                continue;
            }

            // Flat in portrait. The lift is meant to follow the curve of the
            // table's near edge across a single row of seats; once they wrap
            // onto several rows a per-index lift means nothing and just makes
            // the rows look misaligned.
            if (_portrait)
            {
                seat.Margin = new Thickness(4, 0, 4, 0);
                continue;
            }

            // 0 for the middle seat, 1 for the outermost ones. A single
            // hand is the middle seat by definition, and would divide by
            // zero here otherwise.
            var centre = (count - 1) / 2.0;
            var distanceFromCentre = count == 1 ? 0 : Math.Abs(i - centre) / centre;

            seat.Margin = new Thickness(4, 0, 4, distanceFromCentre * HandSlotArcLift);
        }
    }

    /// <summary>
    /// Builds one hand slot's whole view - shared by BuildHandSlots (the
    /// normal 1-5 seats) and SplitButton_OnClicked (inserting a brand new
    /// hand mid-round when a pair is split). The returned Container is what
    /// actually gets added to PlayerHandsLayout - it's the main Border
    /// (cards/chip/bet/value/result) stacked above WarBorder, a second,
    /// smaller drop target for this hand's own optional War side bet
    /// (War Blackjack only - hidden for the other variants, see
    /// UpdateWarUiVisibility). Keeping War's bet as its own separate,
    /// always-visible target - rather than a mode toggle that repurposes
    /// the same chip taps - means both bets can be sized side by side
    /// without switching modes; War is still dealt and resolved before
    /// blackjack play starts (see DealButton_OnClicked/StartWarPhase), only
    /// how it's bet on changes. handIndex is only used to wire up each
    /// border's tap-to-select-for-betting gesture, which is a no-op
    /// mid-round anyway (see SelectHandForBetting), so it doesn't need to
    /// stay accurate if later hands shift position.
    /// </summary>
    private (VerticalStackLayout Container, HandSlotView View) CreateHandSlotView(int handIndex)
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
        // A single chip image (whichever denomination the bet amount maps
        // to) plus a plain "Bet: $X" label - built/rebuilt by
        // UpdateHandSlotBetDisplay.
        // The chip picture is the first thing to go on a short screen: it is
        // decoration, the bet is already written underneath it in words, and
        // its 32pt is most of what a phone is missing.
        var chipImage = new Image
        {
            HeightRequest = _portrait ? 40 : 32,
            Aspect = Aspect.AspectFit,
            HorizontalOptions = LayoutOptions.Center,
            // Kept in portrait, where there is room for it.
            IsVisible = !_compact,
        };
        var betLabel = new Label { Text = "Bet: $0", TextColor = Color.FromArgb("#E6F3C8"), HorizontalOptions = LayoutOptions.Center, FontSize = _portrait ? 18 : _compact ? 12 : 14, FontFamily = AppFonts.Display };
        var valueLabel = new Label { Text = "", TextColor = Colors.White, HorizontalOptions = LayoutOptions.Center, FontSize = _portrait ? 20 : _compact ? 14 : 16, FontFamily = AppFonts.Display };
        var resultLabel = new Label { Text = "", TextColor = Color.FromArgb("#FFC400"), HorizontalOptions = LayoutOptions.Center, FontSize = _portrait ? 16 : _compact ? 11 : 13, FontFamily = AppFonts.Display };

        var content = new VerticalStackLayout
        {
            HorizontalOptions = LayoutOptions.Fill,
            Spacing = 2,
            Children = { cardsLayout, chipImage, betLabel, valueLabel, resultLabel },
        };

        var border = new Border
        {
            Stroke = HandSlotRestingStroke,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            BackgroundColor = HandSlotFill,
            Padding = 6,
            Margin = 2,
            WidthRequest = HandSlotBorderWidth,
            HorizontalOptions = LayoutOptions.Start,
            Content = content,
        };

        var tap = new TapGestureRecognizer();
        tap.Tapped += (_, _) =>
        {
            // Tap-to-bet fallback: if a chip is currently held (see
            // ChipImage_OnTapped), tapping a hand slot places it there
            // instead of just selecting the hand as the betting target -
            // mirrors what dropping that chip on this slot would do.
            if (_heldChipDenomination is { } heldDenomination)
            {
                _ = PlaceChipOnHandAsync(handIndex, heldDenomination, targetWar: false);
            }
            else
            {
                SelectHandForBetting(handIndex);
            }
        };
        border.GestureRecognizers.Add(tap);

        // No DropGestureRecognizer here anymore - the chip tray no longer
        // uses OS drag-and-drop at all (see ChipImage_OnPanUpdated). Hover
        // highlighting while a chip is being dragged over this slot is done
        // manually, by UpdateDragHoverHighlight hit-testing this Border's
        // own absolute bounds against the drag ghost's current position.

        // War Blackjack only - a second, smaller drop target for this
        // hand's own War side bet, sitting right below its main Border.
        // Its own tap gesture mirrors the main Border's, just always
        // targeting the War bet - see PlaceChipOnHandAsync's targetWar
        // parameter. Visibility is toggled per variant by
        // UpdateWarUiVisibility, not built conditionally here, so it
        // doesn't have to be rebuilt if the variant changes without the
        // hand count also changing.
        var warBetLabel = new Label { Text = "War: $0", TextColor = Color.FromArgb("#7FD4FF"), HorizontalOptions = LayoutOptions.Center, FontSize = 14, FontFamily = AppFonts.Display };
        var warBorder = new Border
        {
            Stroke = WarSlotRestingStroke,
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 12 },
            Padding = 4,
            Margin = 2,
            WidthRequest = HandSlotBorderWidth,
            HorizontalOptions = LayoutOptions.Start,
            BackgroundColor = WarSlotFill,
            IsVisible = _variant is WarBlackjackVariant,
            Content = warBetLabel,
        };

        var warTap = new TapGestureRecognizer();
        warTap.Tapped += (_, _) =>
        {
            if (_heldChipDenomination is { } heldWarDenomination)
            {
                _ = PlaceChipOnHandAsync(handIndex, heldWarDenomination, targetWar: true);
            }
            else
            {
                SelectHandForBetting(handIndex);
            }
        };
        warBorder.GestureRecognizers.Add(warTap);

        var container = new VerticalStackLayout
        {
            Spacing = 4,
            HorizontalOptions = LayoutOptions.Start,
            Children = { border, warBorder },
        };

        var view = new HandSlotView
        {
            Border = border,
            WarBorder = warBorder,
            CardsLayout = cardsLayout,
            ChipImage = chipImage,
            BetLabel = betLabel,
            WarBetLabel = warBetLabel,
            ValueLabel = valueLabel,
            ResultLabel = resultLabel,
        };

        return (container, view);
    }

    /// <summary>
    /// Shows/hides each hand slot's WarBorder (its own War side-bet drop
    /// target - see CreateHandSlotView) based on the current variant, and
    /// zeroes out any leftover War bet whenever War isn't the active
    /// variant, so a stale amount from a previous War game can't silently
    /// carry over into a variant that doesn't use it.
    /// </summary>
    private void UpdateWarUiVisibility()
    {
        var isWar = _variant is WarBlackjackVariant;

        foreach (var view in _handSlotViews)
        {
            view.WarBorder.IsVisible = isWar;
        }

        if (!isWar)
        {
            foreach (var hand in _hands)
            {
                hand.WarBet = 0;
            }

            for (var i = 0; i < _handSlotViews.Count; i++)
            {
                UpdateHandSlotBetDisplay(i);
            }
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
            var view = _handSlotViews[i];
            var isHighlighted = _roundInProgress ? i == _activeHandIndex : i == _selectedBetIndex;
            view.Border.Stroke = isHighlighted ? HandSlotSelectedStroke : HandSlotRestingStroke;
            view.Border.StrokeThickness = isHighlighted ? 3 : 1;

            // WarBorder doesn't take part in the selected/active gold
            // highlight (only Border does) - it only ever lights up while a
            // chip drag is actually hovering over it (see
            // UpdateDragHoverHighlight), so resetting it back to its resting
            // color here is what un-highlights it once a drag ends.
            view.WarBorder.Stroke = WarSlotRestingStroke;
            view.WarBorder.StrokeThickness = 1;
        }

        RefreshActionButtonVisibility();
    }

    /// <summary>
    /// Shows only the Hit/Stand/Double/Split buttons the hand currently up
    /// can actually use right now, instead of showing all four all the time
    /// and letting a tap on an invalid one just print an error message -
    /// hidden entirely (not just grayed out) so the row itself always shows
    /// what's legal. Stand is the one exception: it's always offered for
    /// whichever hand is actively being played, since you can always choose
    /// to stop.
    /// </summary>
    private void RefreshActionButtonVisibility()
    {
        if (!_roundInProgress || _activeHandIndex < 0 || _pendingWarDecisionHandIndex >= 0 || _insuranceDecisionPending)
        {
            // No hand is actively being played right now - either still
            // betting, between rounds, in the middle of a War
            // Press/Cash-Out decision, or waiting on an Insure/No Insurance
            // decision - each of those has its own dedicated buttons.
            HitButton.IsVisible = false;
            StandButton.IsVisible = false;
            DoubleButton.IsVisible = false;
            SplitButton.IsVisible = false;
            return;
        }

        var slot = _hands[_activeHandIndex];

        HitButton.IsVisible = _variant.CanHit(slot.Hand);
        StandButton.IsVisible = true;

        var doubledBet = slot.Bet * 2;
        DoubleButton.IsVisible = _variant.CanDoubleDown(slot.Hand)
            && ChipWallet.IsWithinTableLimits(doubledBet)
            && _wallet.Balance >= slot.Bet;

        SplitButton.IsVisible = !slot.HasBeenSplit
            && _variant.CanSplit(slot.Hand)
            && _wallet.Balance >= slot.Bet;
    }

    /// <summary>
    /// Drives the smooth custom chip drag - a chip in the tray (Row 5) is
    /// pressed and dragged, and a free-floating "ghost" copy of it
    /// (DragGhostImage, living outside every ScrollView at the page's root
    /// - see MainPage.xaml) follows the finger 1:1 for the whole gesture,
    /// rather than relying on the OS's own drag-and-drop chrome (which felt
    /// like dragging a file, and was unreliable on iOS - see PlayerHandSlot
    /// and the tap-to-bet fallback added earlier for that same reason).
    /// The chip itself stays put in the tray the whole time; only the ghost
    /// moves, and it's the ghost's final position that gets hit-tested
    /// against the hand slots to decide where (if anywhere) the chip lands.
    /// </summary>
    private async void ChipImage_OnPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (sender is not Image { ClassId: { } denominationTag } chipImage || !decimal.TryParse(denominationTag, out var denomination))
        {
            return;
        }

        switch (e.StatusType)
        {
            case GestureStatus.Started:
                // One drag at a time. A second finger landing on another
                // chip would otherwise overwrite _dragGhostOrigin and the
                // ghost's image mid-flight, so the first chip's ghost would
                // fly home to the wrong place.
                if (_roundInProgress || _isDraggingChip)
                {
                    return;
                }

                _isDraggingChip = true;
                _dragGhostOrigin = GetAbsolutePosition(chipImage, RootLayout);
                _dragHoverHandIndex = -1;
                _dragHoverIsWar = false;
                CacheDragHitTestBounds();

                DragGhostImage.Source = chipImage.Source;
                DragGhostImage.TranslationX = _dragGhostOrigin.X;
                DragGhostImage.TranslationY = _dragGhostOrigin.Y;
                DragGhostImage.Scale = 1.15;
                DragGhostImage.Opacity = 1;
                DragGhostImage.IsVisible = true;

                // The chip stays visible in the tray at reduced opacity -
                // the ghost is what visually reads as "picked up".
                chipImage.Opacity = 0.35;
                break;

            case GestureStatus.Running:
                if (!_isDraggingChip)
                {
                    return;
                }

                DragGhostImage.TranslationX = _dragGhostOrigin.X + e.TotalX;
                DragGhostImage.TranslationY = _dragGhostOrigin.Y + e.TotalY;
                UpdateDragHoverHighlight();
                break;

            case GestureStatus.Completed:
                // Restore before the guard, never after it. This chip was
                // faded on its own Started, and a drag that has already
                // been ended elsewhere still has to hand its chip back -
                // otherwise it sits at 0.35 for the rest of the round.
                chipImage.Opacity = 1;

                if (!_isDraggingChip)
                {
                    return;
                }

                _isDraggingChip = false;

                var targetHandIndex = _dragHoverHandIndex;
                var targetIsWar = _dragHoverIsWar;
                _dragHoverHandIndex = -1;
                _dragHoverIsWar = false;
                RefreshHandSlotHighlights();

                if (targetHandIndex >= 0)
                {
                    await PlaceChipOnHandAsync(targetHandIndex, denomination, targetIsWar);
                    await HideDragGhostAsync();
                }
                else
                {
                    await ReturnDragGhostHomeAsync();
                }
                break;

            case GestureStatus.Canceled:
                chipImage.Opacity = 1;
                _isDraggingChip = false;
                _dragHoverHandIndex = -1;
                _dragHoverIsWar = false;
                RefreshHandSlotHighlights();
                await ReturnDragGhostHomeAsync();
                break;
        }
    }

    /// <summary>
    /// Snapshots every hand slot's current absolute Border/WarBorder bounds
    /// into _dragHitTestBounds. Called once per drag (GestureStatus.Started)
    /// since this geometry doesn't move while a drag is in progress, so
    /// UpdateDragHoverHighlight can hit-test against the cached rects
    /// instead of re-walking the visual tree on every "Running" tick.
    /// </summary>
    private void CacheDragHitTestBounds()
    {
        _dragHitTestBounds.Clear();

        foreach (var view in _handSlotViews)
        {
            var mainBounds = new Rect(GetAbsolutePosition(view.Border, RootLayout), view.Border.Bounds.Size);

            // War Blackjack only - WarBorder is hidden (and so never
            // measured/positioned) for the other variants, so there's
            // nothing meaningful to hit-test against then.
            Rect? warBounds = view.WarBorder.IsVisible
                ? new Rect(GetAbsolutePosition(view.WarBorder, RootLayout), view.WarBorder.Bounds.Size)
                : null;

            _dragHitTestBounds.Add((mainBounds, warBounds));
        }
    }

    /// <summary>
    /// While a chip drag is in progress, highlights whichever hand slot (if
    /// any) the drag ghost's current center point is over - the custom-drag
    /// equivalent of the old DropGestureRecognizer's DragOver highlight.
    /// Updates _dragHoverHandIndex, which ChipImage_OnPanUpdated reads back
    /// on release to decide where the chip lands.
    /// </summary>
    private void UpdateDragHoverHighlight()
    {
        // DragGhostImage.Width/Height can read as -1 (unmeasured) right
        // after IsVisible flips true, before the next layout pass - so use
        // its known fixed WidthRequest/HeightRequest (see MainPage.xaml)
        // rather than the live Width/Height for this center calculation.
        // Read from the ghost itself rather than repeating its size here -
        // this was hard-coded to 56 while the XAML said 58, so every hover
        // test was centred a point up and left of the real chip.
        var dragGhostSize = DragGhostImage.WidthRequest;
        var ghostCenter = new Point(
            DragGhostImage.TranslationX + dragGhostSize / 2,
            DragGhostImage.TranslationY + dragGhostSize / 2);

        var newHoverIndex = -1;
        var newHoverIsWar = false;
        for (var i = 0; i < _dragHitTestBounds.Count; i++)
        {
            var (mainBounds, warBounds) = _dragHitTestBounds[i];
            if (mainBounds.Contains(ghostCenter))
            {
                newHoverIndex = i;
                newHoverIsWar = false;
                break;
            }

            if (warBounds is not { } warBoundsValue || !warBoundsValue.Contains(ghostCenter))
            {
                continue;
            }

            newHoverIndex = i;
            newHoverIsWar = true;
            break;
        }

        if (newHoverIndex == _dragHoverHandIndex && newHoverIsWar == _dragHoverIsWar)
        {
            return;
        }

        _dragHoverHandIndex = newHoverIndex;
        _dragHoverIsWar = newHoverIsWar;

        for (var i = 0; i < _handSlotViews.Count; i++)
        {
            var view = _handSlotViews[i];
            var isMainHovered = i == _dragHoverHandIndex && !_dragHoverIsWar;
            view.Border.Stroke = isMainHovered ? HandSlotHoverStroke : HandSlotRestingStroke;
            view.Border.StrokeThickness = isMainHovered ? 3 : 1;

            var isWarHovered = i == _dragHoverHandIndex && _dragHoverIsWar;
            view.WarBorder.Stroke = isWarHovered ? HandSlotHoverStroke : WarSlotRestingStroke;
            view.WarBorder.StrokeThickness = isWarHovered ? 3 : 1;
        }
    }

    /// <summary>Fades the drag ghost out and resets it, once a drag has resolved (whether the chip landed on a hand or the drag missed and snapped back).</summary>
    private async Task HideDragGhostAsync()
    {
        await DragGhostImage.FadeToAsync(0, 120);
        DragGhostImage.IsVisible = false;
        DragGhostImage.Opacity = 0;
        DragGhostImage.Scale = 1;
    }

    /// <summary>No hand slot was under the release point - glides the ghost smoothly back to where the chip started, then clears it, so a drag that misses reads as "put back down" rather than just vanishing.</summary>
    private async Task ReturnDragGhostHomeAsync()
    {
        await DragGhostImage.TranslateToAsync(_dragGhostOrigin.X, _dragGhostOrigin.Y, 180, Easing.CubicOut);
        await HideDragGhostAsync();
    }

    /// <summary>
    /// Walks up the visual tree from element to root, summing each
    /// ancestor's layout-relative Bounds.X/Y to get element's position in
    /// root's own coordinate space - MAUI has no built-in "position on
    /// screen" query. Subtracts a ScrollView's ScrollX/ScrollY wherever the
    /// walk passes through one, since a ScrollView's Content keeps its
    /// unscrolled layout Bounds regardless of how far it's actually been
    /// scrolled (scrolling is a render-time pan, not a layout change).
    /// Used to place the drag ghost exactly over the chip it's copying, and
    /// to hit-test the ghost against each hand slot's Border.
    /// </summary>
    private static Point GetAbsolutePosition(VisualElement element, VisualElement root)
    {
        double x = 0, y = 0;
        VisualElement? current = element;

        while (current != null && !ReferenceEquals(current, root))
        {
            x += current.Bounds.X;
            y += current.Bounds.Y;

            if (current.Parent is ScrollView scrollView && ReferenceEquals(scrollView.Content, current))
            {
                x -= scrollView.ScrollX;
                y -= scrollView.ScrollY;
            }

            current = current.Parent as VisualElement;
        }

        return new Point(x, y);
    }

    /// <summary>
    /// Adds chipValue to handIndex's bet (or its own separate War bet, when
    /// targetWar is set - War Blackjack only, ignored for the other
    /// variants since there's no War bet to target then), clamped to the
    /// table maximum. Shared by both chip-betting interactions - the custom
    /// chip drag (ChipImage_OnPanUpdated, which resolves targetWar from
    /// which drop target - Border or WarBorder - the ghost was released
    /// over) and tap-to-bet (the separate main/War tap handlers wired up in
    /// CreateHandSlotView) - so a chip placed either way behaves
    /// identically.
    /// </summary>
    private async Task PlaceChipOnHandAsync(int handIndex, decimal chipValue, bool targetWar)
    {
        if (_roundInProgress)
        {
            return;
        }

        if (_needsTableClearOnNextBet)
        {
            ClearTableForNewRound();
        }

        SelectHandForBetting(handIndex);

        var slot = _hands[handIndex];
        var targetingWarBet = targetWar && _variant is WarBlackjackVariant;
        var currentAmount = targetingWarBet ? slot.WarBet : slot.Bet;

        if (currentAmount >= ChipWallet.TableMaximum)
        {
            ResultLabel.Text = $"Table maximum is ${ChipWallet.TableMaximum:N0} per hand.";
            return;
        }

        // Clamp rather than reject outright, so placing a big chip near the
        // cap still places as much of it as the table allows.
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

        UpdateHandSlotBetDisplay(handIndex);
        UpdateTotalWageredText();
        ResultLabel.Text = (targetingWarBet ? slot.WarBet : slot.Bet) >= ChipWallet.TableMaximum
            ? $"Table maximum is ${ChipWallet.TableMaximum:N0} per hand."
            : "";

        // The chip image only ever reflects the main bet (there's no
        // separate War-bet chip graphic), so only fade it in when this
        // actually changed it.
        if (!targetingWarBet)
        {
            await AnimateChipPlaced(handIndex);
        }
    }

    /// <summary>
    /// Tap-to-bet fallback for the chip tray (Row 5) - a universal
    /// alternative to drag-and-drop betting, added because drag gestures
    /// can be unreliable in some environments (e.g. the iOS Simulator).
    /// Tapping a chip "holds" it (tapping the same chip again releases it);
    /// while a chip is held, tapping any hand slot places it there via
    /// PlaceChipOnHandAsync - see the tap handler wired up in
    /// CreateHandSlotView.
    /// </summary>
    private void ChipImage_OnTapped(object? sender, TappedEventArgs e)
    {
        if (_roundInProgress)
        {
            return;
        }

        if (sender is not Image { ClassId: { } denominationTag } || !decimal.TryParse(denominationTag, out var denomination))
        {
            return;
        }

        _heldChipDenomination = _heldChipDenomination == denomination ? null : denomination;
        RefreshHeldChipHighlight();
    }

    /// <summary>Visually marks whichever chip in the tray is currently held (see ChipImage_OnTapped), so it's obvious a tap-to-bet is armed and waiting for a hand slot.</summary>
    private void RefreshHeldChipHighlight()
    {
        foreach (var chipImage in ChipButtonsLayout.Children.OfType<Image>())
        {
            var isHeld = chipImage.ClassId is { } tag
                && decimal.TryParse(tag, out var denomination)
                && denomination == _heldChipDenomination;

            chipImage.Scale = isHeld ? 1.15 : 1.0;
            chipImage.Opacity = isHeld ? 1.0 : 0.85;
        }
    }

    /// <summary>Fades a hand's chip image in whenever a bet is placed or increased, so dropping a chip gives some visual feedback instead of the display just silently updating.</summary>
    private async Task AnimateChipPlaced(int handIndex)
    {
        var chipImage = _handSlotViews[handIndex].ChipImage;

        if (chipImage.Source is null)
        {
            return;
        }

        chipImage.Opacity = 0;
        await chipImage.FadeToAsync(1, ChipPlacedAnimationDurationMs);
    }

    /// <summary>Wipes both of the selected hand's bets (main and, for War Blackjack, its own War bet too) back to $0 - there's no separate "mode" to clear one at a time anymore, so Clear just clears everything on that hand.</summary>
    private void ClearBetButton_OnClicked(object? sender, EventArgs e)
    {
        if (_roundInProgress)
        {
            return;
        }

        var slot = _hands[_selectedBetIndex];
        slot.Bet = 0;
        slot.WarBet = 0;

        UpdateHandSlotBetDisplay(_selectedBetIndex);
        UpdateTotalWageredText();
    }

    /// <summary>
    /// Shoves the entire balance (clamped to the table max) onto the single
    /// hand's bet - only wired up/visible when playing one hand at a time,
    /// since splitting "everything" across several independently-sized
    /// hands wouldn't have one obvious meaning.
    /// </summary>
    private async void AllInButton_OnClicked(object? sender, EventArgs e)
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

        await AnimateChipPlaced(_selectedBetIndex);
    }

    private async void MenuButton_OnClicked(object? sender, EventArgs e)
    {
        var menuPage = new GameMenuPage(_variant, _deckCount, _handCount);
        menuPage.SettingsSaved += OnSettingsSaved;
        await Navigation.PushModalAsync(menuPage);
    }

    /// <summary>Table Settings only ever changes deck count and hand count now - the variant itself can only be chosen by starting a fresh game via Play/RulesPage (see GameMenuPage), so there's nothing here to queue or apply beyond those two numbers.</summary>
    private void OnSettingsSaved(int deckCount, int handCount)
    {
        if (_roundInProgress)
        {
            // Never swap the number of hand slots out from under a round
            // that's already dealt - queue it instead and apply it once
            // EndRound finishes this round.
            _pendingDeckCount = deckCount;
            _pendingHandCount = handCount;
            ResultLabel.Text = "Settings saved - will apply once this round finishes.";
            return;
        }

        ApplyTableSettings(deckCount, handCount);
    }

    private void ApplyTableSettings(int deckCount, int handCount)
    {
        _deckCount = deckCount;
        _deck = null; // force a fresh shoe built at the new deck count on the next Deal
        UpdateShoeDisplay();

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

    private async void DealButton_OnClicked(object? sender, EventArgs e)
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

        // A new round is starting right now - stop the 5-second
        // auto-discard countdown (or a still-running fly-to-discard
        // animation) from doing anything further to the table once this
        // round's own cards are on it.
        _autoDiscardCts?.Cancel();
        _roundGeneration++;

        // A held tap-to-bet chip (see ChipImage_OnTapped) only makes sense
        // while still betting - release it now that the round is starting.
        _heldChipDenomination = null;
        RefreshHeldChipHighlight();

        // Move the previous round's cards into the discard pile instead of
        // silently vanishing them, then only reshuffle the shoe once it's
        // actually been played down past the cut-card threshold (scaled by
        // how many hands are being dealt this round). A brand-new shoe is
        // only ever built the first time, or once the deck count changed in
        // Settings - from then on it's the same shoe, discarded into and
        // reshuffled from, never silently swapped for a fresh one.
        if (_deck is null)
        {
            _deck = new Deck(numberOfDecks: _deckCount);
        }
        else
        {
            if (!_roundCardsDiscarded)
            {
                // The previous round's auto-discard hasn't happened yet
                // (the 5-second hold hasn't elapsed) - do it now, instantly,
                // since a new round is starting immediately regardless.
                _deck.Discard(_dealerHand.Cards);

                foreach (var previousHand in _hands)
                {
                    _deck.Discard(previousHand.Hand.Cards);
                }

                _roundCardsDiscarded = true;
            }

            if (_deck.NeedsReshuffle(CutCardThreshold(_handCount)))
            {
                _deck.ReshuffleDiscardIntoShoe();
            }
        }

        UpdateShoeDisplay();

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
            // any winning War bets decided before real blackjack play
            // starts. They're already hidden (RefreshActionButtonVisibility
            // only shows them once there's an active hand to use them on),
            // so there's nothing further to hide here.
            await StartWarPhase(warVariant);
        }
        else
        {
            await DealOpeningHandsAndStartPlay();
        }

        PersistInProgressRound();
    }

    /// <summary>How many cards left in the shoe triggers a reshuffle-before-next-round, scaled by how many hands are being dealt.</summary>
    private static int CutCardThreshold(int handCount) => 15 * handCount;

    /// <summary>Non-War path: deals every hand's normal two-card opening hand at the model level, then plays the dealt-left-of-dealer, dealer-last reveal animation and starts play (or resolves immediately on an all-blackjack round).</summary>
    private async Task DealOpeningHandsAndStartPlay()
    {
        if (_deck is null)
        {
            return;
        }

        // The model-level dealing itself is instant - it's just data. Only
        // the on-screen reveal below is sequenced/animated.
        _variant.DealDealerOpeningHand(_deck, _dealerHand);

        foreach (var slot in _hands)
        {
            _variant.DealPlayerOpeningHand(_deck, slot.Hand);

            // A natural blackjack finishes this hand immediately, but other
            // hands may still need to play - it just gets skipped in turn
            // order. Its payout, though, cashes out right now rather than
            // waiting on the rest of the round - see TryPayEarlyBlackjack.
            if (slot.Hand.IsBlackjack)
            {
                slot.IsFinished = true;
                TryPayEarlyBlackjack(slot);
            }
        }

        UpdateShoeDisplay();
        await RevealOpeningDeal(hideDealerHoleCard: true);

        if (ShouldOfferInsurance())
        {
            PromptForInsurance();
            return;
        }

        await StartPlayAfterOpeningDeal();
    }

    /// <summary>
    /// Turn order plays rightmost-hand-first, same as the deal, so play
    /// starts on the highest-index unfinished hand, not the lowest. Shared
    /// by DealOpeningHandsAndStartPlay's no-insurance-offered path and
    /// ResolveInsuranceDecision's no-dealer-blackjack path, since both land
    /// in exactly the same place once the opening deal is fully settled.
    /// </summary>
    private async Task StartPlayAfterOpeningDeal()
    {
        _activeHandIndex = _hands.FindLastIndex(h => !h.IsFinished);

        if (_activeHandIndex == -1)
        {
            await EndRound();
        }
        else
        {
            RefreshHandSlotHighlights();
        }
    }

    /// <summary>True if this round should pause for an Insure/No Insurance decision - Standard Blackjack only (see IGameVariant.OffersInsurance), and only when the dealer's face-up card is an Ace.</summary>
    private bool ShouldOfferInsurance() =>
        _variant.OffersInsurance && _dealerHand.Cards.Count > 0 && _dealerHand.Cards[0].Rank == Rank.Ace;

    /// <summary>Shows the Insure/No Insurance prompt and its dedicated buttons in place of the normal Hit/Stand row - see RefreshActionButtonVisibility's _insuranceDecisionPending gate.</summary>
    private void PromptForInsurance()
    {
        _insuranceDecisionPending = true;
        ResultLabel.Text = "Dealer shows an Ace. Insurance for half your bet(s)?";
        InsuranceButton.IsVisible = true;
        DeclineInsuranceButton.IsVisible = true;
        RefreshActionButtonVisibility();
        PersistInProgressRound();
    }

    private async void InsuranceButton_OnClicked(object? sender, EventArgs e) => await ResolveInsuranceDecision(tookInsurance: true);

    private async void DeclineInsuranceButton_OnClicked(object? sender, EventArgs e) => await ResolveInsuranceDecision(tookInsurance: false);

    /// <summary>
    /// Settles the player's Insure/No Insurance choice. If they took it,
    /// every hand puts up to half its own bet on insurance (skipping any
    /// hand that can't afford even that), the dealer's already-dealt hole
    /// card is checked right here - insurance is the one place this table
    /// ever looks at it before the round would otherwise reveal it - and
    /// insurance pays 2:1 per hand if it's a ten-value card, or is simply
    /// lost if not. Declining (or every hand being unable to afford it)
    /// skips straight to that same check with no wager placed. Either way,
    /// a dealer blackjack ends the round immediately via EndRound (which
    /// won't draw the dealer any further cards, since 21 already satisfies
    /// PlayDealerHand's stopping condition) rather than letting the player
    /// act on hands that can no longer do anything but lose or push.
    /// </summary>
    private async Task ResolveInsuranceDecision(bool tookInsurance)
    {
        if (!_insuranceDecisionPending)
        {
            return;
        }

        _insuranceDecisionPending = false;
        InsuranceButton.IsVisible = false;
        DeclineInsuranceButton.IsVisible = false;

        var dealerHasBlackjack = _dealerHand.IsBlackjack;

        if (tookInsurance)
        {
            for (var i = 0; i < _hands.Count; i++)
            {
                var slot = _hands[i];
                var insuranceBet = slot.Bet / 2;

                if (insuranceBet <= 0 || !_wallet.TryDeduct(insuranceBet))
                {
                    continue;
                }

                slot.InsuranceBet = insuranceBet;

                if (dealerHasBlackjack)
                {
                    // Insurance pays 2:1 - return the staked insurance bet
                    // itself plus double its value in profit, the same
                    // "return stake, then add the profit" shape as every
                    // other payout in this file.
                    _wallet.Add(insuranceBet * 3);
                    slot.InsuranceResultText = $"Insurance paid ${insuranceBet * 2:N0}. ";
                }
                else
                {
                    slot.InsuranceResultText = $"Insurance lost ${insuranceBet:N0}. ";
                }

                _handSlotViews[i].ResultLabel.Text = slot.InsuranceResultText;
            }

            UpdateBalanceText();
        }

        ResultLabel.Text = "";

        if (dealerHasBlackjack)
        {
            await EndRound();
            return;
        }

        await StartPlayAfterOpeningDeal();
        PersistInProgressRound();
    }

    /// <summary>
    /// Pays out a natural blackjack the moment it's dealt, instead of
    /// making it wait through every other hand's turn and the dealer's own
    /// play - UNLESS the dealer's face-up card could itself be part of a
    /// dealer blackjack (an Ace, or any ten-value card), in which case a
    /// dealer blackjack underneath would push rather than lose, and this
    /// hand has to wait for that hidden card to be revealed at EndRound
    /// before it's safe to settle. When it's safe, the outcome is already
    /// fully determined by the two hands as they stand right now - the
    /// dealer hasn't drawn any further cards, but a player natural beats
    /// any non-blackjack dealer total no matter what it later becomes, so
    /// there's nothing left to wait for.
    /// </summary>
    private void TryPayEarlyBlackjack(PlayerHandSlot slot)
    {
        if (!slot.Hand.IsBlackjack || slot.ResolvedEarly)
        {
            return;
        }

        if (_dealerHand.Cards.Count == 0)
        {
            return;
        }

        if (DealerUpCardCouldBeBlackjack(_dealerHand.Cards[0]))
        {
            // Can't safely pay yet - the dealer's hole card might complete a
            // blackjack of their own, which would push instead of lose.
            // Without this, the hand just sits with a blank result until
            // EndRound resolves it several seconds later, which reads
            // exactly like the instant payout silently failing rather than
            // correctly waiting. EndRound below overwrites this placeholder
            // (rather than appending after it) once it actually resolves.
            slot.ResultText = "Blackjack! Waiting on dealer's hole card...";
            slot.HasPendingBlackjack = true;
            return;
        }

        var outcome = _variant.DetermineOutcome(slot.Hand, _dealerHand);
        var payout = _variant.ResolvePayout(slot.Hand, _dealerHand, slot.Bet);

        _wallet.Add(slot.Bet + payout);
        slot.ResultText += DescribeOutcome(outcome, payout);
        slot.ResolvedEarly = true;
        _stats.RecordHand(ClassifyHandResult(outcome));
        UpdateBalanceText();
    }

    /// <summary>Maps a resolved hand's RoundOutcome onto the simpler win/loss/push bucket GameStats tracks.</summary>
    private static HandResult ClassifyHandResult(RoundOutcome outcome) => outcome switch
    {
        RoundOutcome.PlayerBlackjack or RoundOutcome.PlayerWin or RoundOutcome.DealerBust => HandResult.Win,
        RoundOutcome.DealerWin or RoundOutcome.PlayerBust => HandResult.Loss,
        _ => HandResult.Push,
    };

    /// <summary>True if the dealer's face-up card could possibly be part of a dealer blackjack - an Ace, or any ten-value card (10/J/Q/K).</summary>
    private static bool DealerUpCardCouldBeBlackjack(Card dealerUpCard) =>
        dealerUpCard.Rank == Rank.Ace || Hand.PointValue(dealerUpCard.Rank) == 10;

    /// <summary>
    /// Plays the "cards fly in from the shoe" reveal for a full round of
    /// opening cards that have already been dealt at the model level, the
    /// way a real dealer actually deals them: one card at a time, round by
    /// round, NOT a whole hand at once - everyone's first card, then
    /// everyone's second card (and so on, for variants dealing more), with
    /// the dealer's own card dealt last in each round. Within a round, the
    /// dealer deals starting with the player at their own left and moves
    /// clockwise; from our on-screen view - facing the dealer, hands laid
    /// out left to right - that's the dealer's left being our right, so the
    /// rightmost hand slot goes first, on down to the leftmost, then
    /// finally the dealer. The model already holds every hand's final
    /// cards; this only controls how they visually appear.
    /// </summary>
    private async Task RevealOpeningDeal(bool hideDealerHoleCard)
    {
        foreach (var view in _handSlotViews)
        {
            view.CardsLayout.Children.Clear();
        }

        DealerCardsLayout.Children.Clear();

        var maxRounds = Math.Max(
            _hands.Count > 0 ? _hands.Max(h => h.Hand.Cards.Count) : 0,
            _dealerHand.Cards.Count);

        for (var round = 0; round < maxRounds; round++)
        {
            for (var i = _hands.Count - 1; i >= 0; i--)
            {
                var slot = _hands[i];

                if (round >= slot.Hand.Cards.Count)
                {
                    continue;
                }

                var view = _handSlotViews[i];
                await DealAnimatedCard(view.CardsLayout, CardImageFile(slot.Hand.Cards[round]), HandSlotCardHeight);

                // As soon as THIS hand's own cards are all down, show its
                // value - and, if it was already cashed out as an immediate
                // blackjack (see TryPayEarlyBlackjack), its result text -
                // right away. Don't make a hand's own payout confirmation
                // wait on every other hand and the dealer finishing their
                // deal too; that's what made an instant blackjack look like
                // nothing had happened yet.
                if (round == slot.Hand.Cards.Count - 1)
                {
                    view.ValueLabel.Text = $"Value: {slot.Hand.GetBestValue().Value}";
                    view.ResultLabel.Text = slot.ResultText;
                }

                await Task.Delay(CardDealStaggerMs);
            }

            if (round < _dealerHand.Cards.Count)
            {
                var showFaceDown = hideDealerHoleCard && round == 1;
                var imageFile = showFaceDown ? CardBackFile : CardImageFile(_dealerHand.Cards[round]);
                await DealAnimatedCard(DealerCardsLayout, imageFile, DealerCardHeight);
                await Task.Delay(CardDealStaggerMs);
            }
        }

        UpdateDealerValueLabel(hideDealerHoleCard);
    }

    /// <summary>
    /// Adds a card image to its final destination layout, then animates it
    /// flying in from the shoe's on-screen position - MAUI has no built-in
    /// "position relative to some other element" API, so both positions are
    /// measured by walking each element's Parent chain (see
    /// GetPositionOnPage) and the difference becomes the fly-in offset.
    /// </summary>
    private async Task DealAnimatedCard(Layout targetLayout, string imageFile, double height)
    {
        var image = CreateCardImage(imageFile, height);
        targetLayout.Children.Add(image);
        await AnimateCardFromShoe(image);
    }

    /// <summary>
    /// Best-effort fly-in-from-the-shoe animation for one already-placed
    /// card image. Falls back to a plain fade-in in place if layout hasn't
    /// happened yet and positions can't be measured (both would read as
    /// (0,0), giving no offset) - it never guesses a wrong direction.
    /// </summary>
    private async Task AnimateCardFromShoe(View cardImage)
    {
        cardImage.Opacity = 0;

        // Let MAUI finish a layout pass so the card actually has real
        // Bounds to read before measuring positions off of it. A single
        // yield isn't reliably enough for a native measure/arrange pass
        // to complete, so poll a few frames (bounded, so a card that
        // genuinely never gets laid out still animates instead of
        // hanging) until Bounds actually has real content.
        for (var attempt = 0; attempt < MaxLayoutWaitAttempts && cardImage.Bounds.IsEmpty; attempt++)
        {
            await Task.Yield();
        }

        var shoePosition = GetPositionOnPage(ShoeImage);
        var cardPosition = GetPositionOnPage(cardImage);

        cardImage.TranslationX = shoePosition.X - cardPosition.X;
        cardImage.TranslationY = shoePosition.Y - cardPosition.Y;

        await Task.WhenAll(
            cardImage.TranslateToAsync(0, 0, CardDealAnimationDurationMs, Easing.CubicOut),
            cardImage.FadeToAsync(1, CardDealAnimationDurationMs));
    }

    /// <summary>
    /// Walks up the visual tree from `element`, summing each ancestor's
    /// Bounds offset, to get its position relative to the outermost
    /// page-level layout. Used to measure both the shoe's and a newly-added
    /// card's position so the difference can drive the deal animation.
    /// </summary>
    private static (double X, double Y) GetPositionOnPage(VisualElement element)
    {
        double x = 0;
        double y = 0;
        Element? current = element;

        while (current is VisualElement visual)
        {
            x += visual.Bounds.X;
            y += visual.Bounds.Y;
            current = current.Parent;
        }

        return (x, y);
    }

    /// <summary>Refreshes the shoe/discard-pile counts shown next to the dealer area.</summary>
    private void UpdateShoeDisplay()
    {
        ShoeCountLabel.Text = _deck is null ? "—" : $"{_deck.CardsRemaining}";
        DiscardCountLabel.Text = _deck is null ? "0" : $"{_deck.DiscardCount}";
    }

    /// <summary>
    /// War path, phase 1: deals the one shared dealer War card and each
    /// hand's own War card, immediately settles any hand that lost or tied
    /// its War bet (dealer wins ties), and queues up a Press-or-Cash-Out
    /// decision for every hand that won one.
    /// </summary>
    private async Task StartWarPhase(WarBlackjackVariant warVariant)
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

        UpdateShoeDisplay();
        // No hole card exists yet - both War cards are shown face-up.
        await RevealOpeningDeal(hideDealerHoleCard: false);

        _warDecisionQueue.Clear();
        _anyHandBetWarThisRound = false;
        _anyHandWonWarThisRound = false;

        // Same rightmost-hand-first order as the deal and blackjack turn
        // order, so Press/Cash-Out decisions are asked in the same order
        // hands were dealt.
        for (var i = _hands.Count - 1; i >= 0; i--)
        {
            var slot = _hands[i];

            if (slot.WarBet <= 0)
            {
                continue;
            }

            _anyHandBetWarThisRound = true;

            if (warVariant.PlayerWinsWar(slot.Hand, _dealerHand))
            {
                _warDecisionQueue.Enqueue(i);
                _anyHandWonWarThisRound = true;
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

        // If every War bet this round lost outright, nothing queues a
        // Press/Cash-Out prompt to put on the shared ResultLabel - without
        // this, the table would sit there with that label still blank
        // through the whole pause below, looking like nothing happened.
        // Each hand's own small result label already shows its exact loss
        // amount (see the loop above), so this just needs to be the one
        // headline "something happened here" message.
        if (_anyHandBetWarThisRound && !_anyHandWonWarThisRound)
        {
            ResultLabel.Text = "Lost the War.";
        }

        // The Total display intentionally doesn't refresh here (or anywhere
        // else mid-round) - every hand's main bet is still deducted and
        // "in the air" until blackjack plays out, so any total taken now
        // would just show the whole round's stake as a loss before the
        // round has actually finished. It stays at the "$0" it was reset to
        // when betting started, and only shows the real result once
        // EndRound resolves the whole round.
        await AdvanceWarDecisionQueue();
    }

    /// <summary>Moves to the next hand with a pending War decision, or - once the queue is empty - deals every hand's second card and starts real blackjack play.</summary>
    private async Task AdvanceWarDecisionQueue()
    {
        if (_warDecisionQueue.Count == 0)
        {
            _pendingWarDecisionHandIndex = -1;
            PressWarButton.IsVisible = false;
            CashOutWarButton.IsVisible = false;

            // Give the War result - a winning hand's Press/Cash-Out choice,
            // or the "Lost the War." summary set above when every War bet
            // lost - a moment to actually be read before the second cards
            // come flying in right on top of it. Nothing worth pausing for
            // if nobody bet War at all this round; there's no result to see.
            if (_anyHandBetWarThisRound)
            {
                await Task.Delay(WarResultPauseBeforeSecondCards);
            }

            ResultLabel.Text = "";
            await DealOpeningSecondCardsAndStartPlay();
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
    private async void PressWarButton_OnClicked(object? sender, EventArgs e)
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

        // Replaces the now-stale "Press it into your bet, or cash out?"
        // prompt with what was actually chosen, so that's what's still on
        // screen during AdvanceWarDecisionQueue's pause before the second
        // cards come out (see WarResultPauseBeforeSecondCards).
        ResultLabel.Text = slot.ResultText;

        await AdvanceWarDecisionQueue();
        PersistInProgressRound();
    }

    /// <summary>Banks this hand's War stake plus its 1:1 winnings straight into the balance, leaving the blackjack bet untouched.</summary>
    private async void CashOutWarButton_OnClicked(object? sender, EventArgs e)
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

        // See the matching note in PressWarButton_OnClicked - replaces the
        // stale prompt with the actual outcome for AdvanceWarDecisionQueue's
        // pause to show.
        ResultLabel.Text = slot.ResultText;

        await AdvanceWarDecisionQueue();
        PersistInProgressRound();
    }

    /// <summary>War path, phase 2: once every War decision is settled, deals the dealer's and each hand's second card, plays the fly-in-from-the-shoe reveal for just those new cards, and starts real blackjack play (or resolves immediately on an all-blackjack round).</summary>
    private async Task DealOpeningSecondCardsAndStartPlay()
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
                TryPayEarlyBlackjack(slot);
            }
        }

        UpdateShoeDisplay();
        await RevealSecondCards();

        // Turn order plays rightmost-hand-first, same as the deal, so play
        // starts on the highest-index unfinished hand, not the lowest.
        _activeHandIndex = _hands.FindLastIndex(h => !h.IsFinished);

        if (_activeHandIndex == -1)
        {
            await EndRound();
        }
        else
        {
            SetRoundInProgress(true); // re-enables Hit/Stand/Double/Split now that real play begins
            RefreshHandSlotHighlights();
        }
    }

    /// <summary>
    /// Plays the fly-in-from-the-shoe reveal for just the War path's second
    /// round of cards (the War cards are already on the table) - every
    /// hand's new card, dealt in the same rightmost-first, dealer-last
    /// order as the opening deal, then the dealer's face-down hole card.
    /// </summary>
    private async Task RevealSecondCards()
    {
        for (var i = _hands.Count - 1; i >= 0; i--)
        {
            var slot = _hands[i];
            var view = _handSlotViews[i];
            var newCard = slot.Hand.Cards[^1];

            await DealAnimatedCard(view.CardsLayout, CardImageFile(newCard), HandSlotCardHeight);
            view.ValueLabel.Text = $"Value: {slot.Hand.GetBestValue().Value}";
            view.ResultLabel.Text = slot.ResultText;
            await Task.Delay(CardDealStaggerMs);
        }

        await DealAnimatedCard(DealerCardsLayout, CardBackFile, DealerCardHeight); // the dealer's second card stays hidden as the hole card
        UpdateDealerValueLabel(hideHoleCard: true);
    }

    private async void HitButton_OnClicked(object? sender, EventArgs e)
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
        UpdateShoeDisplay();
        await RevealNewCardInHand(_activeHandIndex);

        // The hand's own card count just changed (e.g. Double is no longer
        // legal after a third card) even if the turn itself doesn't
        // advance, so button visibility needs a refresh regardless.
        RefreshActionButtonVisibility();
        await ContinueOrAdvance(cameFromDouble: false);
        PersistInProgressRound();
    }

    private async void StandButton_OnClicked(object? sender, EventArgs e)
    {
        if (!_roundInProgress || _deck is null || _activeHandIndex < 0)
        {
            return;
        }

        _hands[_activeHandIndex].IsFinished = true;
        await AdvanceToNextHandOrEndRound();
        PersistInProgressRound();
    }

    private async void DoubleButton_OnClicked(object? sender, EventArgs e)
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
        UpdateShoeDisplay();
        await RevealNewCardInHand(_activeHandIndex);
        RefreshActionButtonVisibility();
        await ContinueOrAdvance(cameFromDouble: true);
        PersistInProgressRound();
    }

    /// <summary>
    /// Animates just the newest card in a hand (the one a Hit or Double
    /// just drew) flying in from the shoe, rather than clearing and
    /// redrawing every card in the hand - the earlier cards are already on
    /// the table and shouldn't re-animate every time a new one is dealt.
    /// </summary>
    private async Task RevealNewCardInHand(int handIndex)
    {
        var slot = _hands[handIndex];
        var view = _handSlotViews[handIndex];
        var newCard = slot.Hand.Cards[^1];

        await DealAnimatedCard(view.CardsLayout, CardImageFile(newCard), HandSlotCardHeight);
        view.ValueLabel.Text = $"Value: {slot.Hand.GetBestValue().Value}";
        view.ResultLabel.Text = slot.ResultText;
    }

    /// <summary>
    /// Splits the active hand's pair into two separate hands, each getting
    /// one new card and its own equal-sized bet (an additional deduction
    /// from the wallet). Turn order plays hands in descending index order
    /// (rightmost hand slot first, matching the deal), so the new hand is
    /// inserted right BEFORE the original's current position - which pushes
    /// the still-active original hand's index up by one - so it's the very
    /// next (lower-index) hand visited once the first half is done, with no
    /// other turn-advancing code needing to know splitting happened at all.
    /// Only one split per hand is offered (no re-splitting a hand that
    /// already came from a split), and splitting Aces follows the standard
    /// rule: each Ace hand gets exactly one more card and is locked
    /// immediately, with no further hitting or doubling.
    /// </summary>
    private async void SplitButton_OnClicked(object? sender, EventArgs e)
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
        UpdateShoeDisplay();

        if (wasSplittingAces)
        {
            slot.IsFinished = true;
            newSlot.IsFinished = true;
        }

        // Insert at the original hand's current position, which pushes the
        // original hand itself up to the next index - _activeHandIndex is
        // bumped to match, so it still refers to the same (first-half) hand.
        var insertIndex = _activeHandIndex;
        _hands.Insert(insertIndex, newSlot);
        _activeHandIndex++;

        var (container, view) = CreateHandSlotView(insertIndex);
        _handSlotViews.Insert(insertIndex, view);
        PlayerHandsLayout.Children.Insert(insertIndex, container);
        ApplyHandSlotArc();

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
            await AdvanceToNextHandOrEndRound();
        }
        else
        {
            ResultLabel.Text = "";
            RefreshHandSlotHighlights();
        }

        PersistInProgressRound();
    }

    /// <summary>
    /// Shared post-Hit/post-Double bookkeeping for the active hand: ends
    /// its turn on a bust, ends it if this variant's rule says doubling
    /// finishes the turn, and otherwise ends it once the hand can neither
    /// hit nor double any further (e.g. Double Down Madness's Ace-opener
    /// lock finally closing). Either way, play then advances to the next
    /// unfinished hand, or to EndRound if this was the last one.
    /// </summary>
    private async Task ContinueOrAdvance(bool cameFromDouble)
    {
        var slot = _hands[_activeHandIndex];

        if (slot.Hand.IsBust)
        {
            slot.IsFinished = true;
            await AdvanceToNextHandOrEndRound();
            return;
        }

        if (cameFromDouble && _variant.EndsTurnAfterDouble)
        {
            slot.IsFinished = true;
            await AdvanceToNextHandOrEndRound();
            return;
        }

        if (!_variant.CanHit(slot.Hand) && !_variant.CanDoubleDown(slot.Hand))
        {
            slot.IsFinished = true;
            await AdvanceToNextHandOrEndRound();
        }
    }

    private async Task AdvanceToNextHandOrEndRound()
    {
        var nextIndex = -1;

        // Turn order plays rightmost-hand-first, same as the deal - so the
        // next hand to act is the next LOWER index, not the next higher one.
        for (var i = _activeHandIndex - 1; i >= 0; i--)
        {
            if (!_hands[i].IsFinished)
            {
                nextIndex = i;
                break;
            }
        }

        if (nextIndex == -1)
        {
            await EndRound();
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
    /// <summary>Starts filling the rewarded video the bust-out rescue needs, well before it could be asked for.</summary>
    protected override void OnAppearing()
    {
        base.OnAppearing();

        // Start filling a rewarded video now, not when the player busts out -
        // one that only starts loading at the moment it is needed will not be
        // ready in time, and ChipRescue treats "not ready" as "do not offer",
        // so the rescue would simply never appear. Cheap and idempotent: the
        // implementation no-ops when one is already loaded or in flight.
        AppServices.Ads.PreloadRewardedAd();
    }

    private async Task EndRound()
    {
        if (_deck is null)
        {
            return;
        }

        // How many cards the dealer already had (both War cards, or the
        // opening two) before their actual play - anything beyond this is a
        // card their play just drew, and gets revealed one at a time below.
        var dealerCardsBeforePlay = _dealerHand.Cards.Count;

        if (_hands.Any(h => !h.Hand.IsBust))
        {
            _variant.PlayDealerHand(_deck, _dealerHand);
            UpdateShoeDisplay();
            await RevealDealerPlay(dealerCardsBeforePlay);
        }
        else
        {
            // Every hand already busted - the dealer doesn't draw any
            // further cards, but the hole card still needs to be flipped
            // face-up so the final hand is visible.
            RenderDealerHand(hideHoleCard: true);
            await FlipDealerHoleCardFaceUp();
            UpdateDealerValueLabel(hideHoleCard: false);
        }

        foreach (var slot in _hands)
        {
            // Already cashed out the moment it was dealt (see
            // TryPayEarlyBlackjack) - resolving it again here would pay it
            // a second time.
            if (slot.ResolvedEarly)
            {
                continue;
            }

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

            // A deferred natural blackjack's ResultText is still the
            // "Waiting on dealer's hole card..." placeholder from
            // TryPayEarlyBlackjack, not a real outcome - replace it outright
            // instead of appending after it. Every other hand keeps
            // appending, since some already carry real text from earlier in
            // the round (a War press/cash-out note, for instance). Either
            // way, InsuranceResultText (set separately in
            // ResolveInsuranceDecision, if this hand took insurance) is
            // prepended in front, since it's the one piece of this round's
            // text that survives the pending-blackjack overwrite below.
            slot.ResultText = slot.HasPendingBlackjack
                ? slot.InsuranceResultText + DescribeOutcome(outcome, payout)
                : slot.InsuranceResultText + slot.ResultText + DescribeOutcome(outcome, payout);
            slot.IsFinished = true;
            _stats.RecordHand(ClassifyHandResult(outcome));
        }

        // The round's true net win/loss - computed from the actual wallet
        // movement since the snapshot taken at Deal, rather than re-derived
        // by summing payouts by hand, since a pressed War win blurs "War
        // money" into "blackjack money" and a hand-by-hand sum would either
        // double-count or drop part of it. Recorded as this round's overall
        // profit/loss (separately from each hand's own win/loss/push above),
        // and everything gets saved right away so balance and stats survive
        // an app close between rounds.
        var totalNet = _wallet.Balance - _walletBalanceAtRoundStart;
        _stats.RecordRoundNet(totalNet, DateOnly.FromDateTime(DateTime.Now));
        GameProgressStorage.Save(_wallet, _stats);

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

        // The dealer's hand was already revealed card-by-card above - only
        // the player hand slots (result text, values) still need redrawing.
        for (var i = 0; i < _hands.Count; i++)
        {
            RenderHandSlot(i);
        }

        _roundInProgress = false;
        _activeHandIndex = -1;

        // The round just finished normally (not abandoned) - it's no
        // longer "unfinished", so drop any mid-round save (see
        // PersistInProgressRound) that GameMenuPage's Load Game button
        // would otherwise still offer.
        GameProgressStorage.ClearInProgressRound();

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
        // the player actually starts betting on the next one - or, absent
        // that, for the fixed hold below, before they automatically fly off
        // to the discard pile on their own.
        _needsTableClearOnNextBet = true;
        _roundCardsDiscarded = false;
        _ = ScheduleAutoDiscard();

        if (_pendingDeckCount is not null && _pendingHandCount is not null)
        {
            ApplyTableSettings(_pendingDeckCount.Value, _pendingHandCount.Value);
            _pendingDeckCount = null;
            _pendingHandCount = null;
        }

        await OfferChipRescueIfBustedOut();
    }

    /// <summary>
    /// When the round just played leaves the player unable to place even the
    /// table minimum, offer to trade a rewarded video for a fresh stake -
    /// see BlackjackApp.core.Services.ChipRescue for the rules, which live
    /// there so they are testable without an ad network.
    ///
    /// Only ever called at the end of a round, and only ever as an offer. The
    /// player is never shown an ad they did not ask for, and declining leaves
    /// them exactly where they were.
    /// </summary>
    private async Task OfferChipRescueIfBustedOut()
    {
        var today = DateOnly.FromDateTime(DateTime.Now);

        var status = ChipRescue.GetStatus(
            _wallet.Balance,
            GameProgressStorage.LoadLastChipRescue(),
            GameProgressStorage.LoadChipRescuesOnThatDay(),
            today,
            AppServices.Ads.IsRewardedAdReady);

        if (!status.CanWatch)
        {
            // Out of chips with nothing to offer - out of watches for today,
            // or no ad loaded. Say so rather than leaving them staring at a
            // table they cannot bet on with no explanation.
            if (ChipRescue.IsBustedOut(_wallet.Balance))
            {
                ResultLabel.Text = "Out of chips - your daily bonus is waiting in the menu.";
            }

            return;
        }

        var watch = await DisplayAlertAsync(
            "Out of Chips",
            $"Watch a short video for ${status.Reward:N0} in chips?"
            + $"\n\n{status.WatchesRemainingToday} of {ChipRescue.MaxWatchesPerDay} left today.",
            "Watch",
            "No Thanks");

        if (!watch)
        {
            ResultLabel.Text = "Out of chips - your daily bonus is waiting in the menu.";
            return;
        }

        var earned = await AppServices.Ads.ShowRewardedAdAsync();

        if (!earned)
        {
            // Either they closed it early or it failed to show. Paying out
            // anyway would pay for skipped ads, so it does not - but the
            // watch is not counted against their daily cap either, since
            // they did not actually get anything for it.
            ResultLabel.Text = "No chips added - the video wasn't finished.";
            return;
        }

        _wallet.Add(ChipRescue.RewardPerAd);
        GameProgressStorage.SaveBalance(_wallet.Balance);

        var (watchedOn, countThatDay) = ChipRescue.RecordWatch(
            GameProgressStorage.LoadLastChipRescue(),
            GameProgressStorage.LoadChipRescuesOnThatDay(),
            today);
        GameProgressStorage.SaveChipRescue(watchedOn, countThatDay);

        UpdateBalanceText();
        ResultLabel.Text = $"+${ChipRescue.RewardPerAd:N0} in chips. Good luck.";
    }

    /// <summary>
    /// Waits out the fixed table-hold period after a round ends, then moves
    /// that round's cards to the discard pile with a fly-away animation -
    /// unless a new round starts (or Settings changes hand count etc.)
    /// before the wait finishes, in which case DealButton_OnClicked's own
    /// cancellation here just lets this quietly do nothing.
    /// </summary>
    private async Task ScheduleAutoDiscard()
    {
        _autoDiscardCts?.Cancel();
        var cts = new CancellationTokenSource();
        _autoDiscardCts = cts;

        try
        {
            await Task.Delay(TableHoldBeforeDiscard, cts.Token);
        }
        catch (TaskCanceledException)
        {
            return;
        }

        await DiscardTableToDiscardPile();
    }

    /// <summary>
    /// Animates every card currently on the table flying off to the
    /// discard pile, then actually moves them there in the model and clears
    /// the table's display - the real-world equivalent of a dealer
    /// sweeping a finished hand into the discard tray. Cards to discard are
    /// snapshotted up front so a new round starting mid-animation (a rare
    /// race right at the 5-second mark) can never end up having THIS round
    /// discard the new round's cards instead of its own; the same new-round
    /// check also guards the final display clear, so it never erases cards
    /// the new round has already dealt.
    /// </summary>
    private async Task DiscardTableToDiscardPile()
    {
        if (_deck is null || _roundCardsDiscarded)
        {
            return;
        }

        _roundCardsDiscarded = true;
        var generation = _roundGeneration;

        var dealerCardsToDiscard = _dealerHand.Cards.ToList();
        var handCardsToDiscard = _hands.Select(h => h.Hand.Cards.ToList()).ToList();
        var cardViews = CollectTableCardViews();

        await AnimateCardsToDiscard(cardViews);

        _deck.Discard(dealerCardsToDiscard);

        foreach (var cards in handCardsToDiscard)
        {
            _deck.Discard(cards);
        }

        UpdateShoeDisplay();

        if (generation == _roundGeneration)
        {
            ClearTableForNewRound();
        }
    }

    /// <summary>Every card image currently showing on the table - the dealer's and every hand's - snapshotted right before the discard-fly-away animation starts.</summary>
    private List<View> CollectTableCardViews()
    {
        var views = new List<View>();

        foreach (var child in DealerCardsLayout.Children)
        {
            if (child is View view)
            {
                views.Add(view);
            }
        }

        foreach (var handView in _handSlotViews)
        {
            foreach (var child in handView.CardsLayout.Children)
            {
                if (child is View view)
                {
                    views.Add(view);
                }
            }
        }

        return views;
    }

    /// <summary>Flies every given card image to the discard pile's on-screen position, fading each out as it lands, with a slight stagger so the whole table doesn't leave in one instant jump.</summary>
    private async Task AnimateCardsToDiscard(List<View> cardViews)
    {
        if (cardViews.Count == 0)
        {
            return;
        }

        var discardPosition = GetPositionOnPage(DiscardImage);
        var animations = new List<Task>(cardViews.Count);

        for (var i = 0; i < cardViews.Count; i++)
        {
            animations.Add(AnimateOneCardToDiscard(cardViews[i], discardPosition, startDelayMs: i * CardDiscardStaggerMs));
        }

        await Task.WhenAll(animations);
    }

    private static async Task AnimateOneCardToDiscard(View cardView, (double X, double Y) discardPosition, int startDelayMs)
    {
        if (startDelayMs > 0)
        {
            await Task.Delay(startDelayMs);
        }

        var cardPosition = GetPositionOnPage(cardView);
        var targetX = discardPosition.X - cardPosition.X;
        var targetY = discardPosition.Y - cardPosition.Y;

        await Task.WhenAll(
            cardView.TranslateToAsync(targetX, targetY, CardDiscardAnimationDurationMs, Easing.CubicIn),
            cardView.FadeToAsync(0, CardDiscardAnimationDurationMs));
    }

    /// <summary>
    /// Reveals the dealer's final hand the way a real dealer plays it out,
    /// instead of the whole thing just appearing at once: first flips the
    /// hole card face-up (see FlipDealerHoleCardFaceUp), then - for any
    /// further cards the dealer's play actually drew past that - animates
    /// each one flying in from the shoe, one at a time, updating the value
    /// readout as each lands.
    /// </summary>
    private async Task RevealDealerPlay(int dealerCardsBeforePlay)
    {
        DealerCardsLayout.Children.Clear();
        for (var i = 0; i < dealerCardsBeforePlay; i++)
        {
            var showFaceDown = i == 1;
            var imageFile = showFaceDown ? CardBackFile : CardImageFile(_dealerHand.Cards[i]);
            DealerCardsLayout.Children.Add(CreateCardImage(imageFile, DealerCardHeight));
        }

        UpdateDealerValueLabel(hideHoleCard: true);

        if (dealerCardsBeforePlay > 1)
        {
            await FlipDealerHoleCardFaceUp();
        }

        UpdateDealerValueLabel(hideHoleCard: false);
        await Task.Delay(CardDealStaggerMs);

        for (var i = dealerCardsBeforePlay; i < _dealerHand.Cards.Count; i++)
        {
            await DealAnimatedCard(DealerCardsLayout, CardImageFile(_dealerHand.Cards[i]), DealerCardHeight);
            UpdateDealerValueLabel(hideHoleCard: false);
            await Task.Delay(CardDealStaggerMs);
        }
    }

    /// <summary>
    /// Flips the dealer's already-on-table hole card (DealerCardsLayout's
    /// second card) from face-down to face-up. MAUI has no built-in card
    /// flip, so this fakes one with a horizontal scale: narrow the card to
    /// a sliver on its X axis, swap its image source once it's edge-on
    /// (where the swap is invisible), then widen it back out - reading as
    /// the card turning over rather than an instant image swap.
    /// </summary>
    private async Task FlipDealerHoleCardFaceUp()
    {
        if (DealerCardsLayout.Children.Count < 2 || DealerCardsLayout.Children[1] is not Image holeCardImage)
        {
            return;
        }

        await ScaleXTo(holeCardImage, 0, CardFlipHalfDurationMs, Easing.CubicIn);
        holeCardImage.Source = ImageSource.FromFile(CardImageFile(_dealerHand.Cards[1]));
        await ScaleXTo(holeCardImage, 1, CardFlipHalfDurationMs, Easing.CubicOut);
    }

    /// <summary>
    /// Animates a view's ScaleX to the given value. MAUI's built-in
    /// animation extensions (FadeToAsync/TranslateToAsync/RotateTo etc. -
    /// see elsewhere in this file) don't include a ScaleX-only variant, so
    /// this drives the ScaleX property directly through the same
    /// VisualElement.Animate primitive those extensions are built on.
    /// </summary>
    private static Task ScaleXTo(VisualElement view, double toValue, uint length, Easing easing)
    {
        var tcs = new TaskCompletionSource<bool>();
        view.Animate(
            "FlipScaleX",
            v => view.ScaleX = v,
            view.ScaleX,
            toValue,
            length: length,
            easing: easing,
            finished: (_, cancelled) => tcs.TrySetResult(!cancelled));
        return tcs.Task;
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
            var imageFile = showFaceDown ? CardBackFile : CardImageFile(_dealerHand.Cards[i]);
            DealerCardsLayout.Children.Add(CreateCardImage(imageFile, DealerCardHeight));
        }

        UpdateDealerValueLabel(hideHoleCard);
    }

    /// <summary>
    /// Sets the dealer's value readout from current game state. While
    /// hideHoleCard is true, only the visible upcard's value is shown.
    /// Factored out so the animated deal-reveal paths (which add the
    /// dealer's cards one at a time rather than through RenderDealerHand)
    /// can still update the same label once their reveal finishes.
    /// </summary>
    private void UpdateDealerValueLabel(bool hideHoleCard)
    {
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

    private void UpdateTotalWageredText() => CurrentBetLabel.Text = $"${_hands.Sum(h => h.Bet + h.WarBet):N0}";

    /// <summary>
    /// Deal and the whole chip/Clear/All-In row only make sense before a
    /// round starts - rather than graying them out mid-round (same as
    /// Hit/Stand/Double/Split used to be), they're hidden entirely, so the
    /// screen only ever shows buttons that currently do something. Their
    /// actual usability once visible again is handled elsewhere (Deal's own
    /// bet-validation messages; RefreshActionButtonVisibility for the four
    /// action buttons).
    /// </summary>
    private void SetRoundInProgress(bool roundInProgress)
    {
        DealButton.IsVisible = !roundInProgress;
        ChipButtonsLayout.IsVisible = !roundInProgress;
    }

    private void UpdateBalanceText() => BalanceLabel.Text = $"${_wallet.Balance:N0}";

    private void UpdateVariantLabel() => VariantLabel.Text = _variant.Name;

    /// <summary>The face-down card art for the current variant - War Blackjack gets the kit's blue back to match its blue-accented table, everyone else keeps the red back.</summary>
    private string CardBackFile => _variant is WarBlackjackVariant ? "back_blue.png" : "back_red.png";

    /// <summary>
    /// Swaps the table artwork to match the design doc's per-variant look.
    /// The kit ships exactly three felts and three backdrops, so each
    /// variant gets a real one rather than a tinted copy of one table:
    /// green for Standard, red for Double Down Madness, gold for War.
    /// </summary>
    private void UpdateTableColors()
    {
        var (felt, backdrop, pageBackground) = _variant switch
        {
            DoubleDownMadnessVariant => ("felt_red.png", "backdrop_olive.png", "#4A0E00"),
            WarBlackjackVariant => ("felt_gold.png", "backdrop_blue.png", "#003A54"),
            _ => ("felt_green.png", "backdrop_purple.png", "#2A4D00"),
        };

        FeltImage.Source = ImageSource.FromFile(felt);
        BackdropImage.Source = ImageSource.FromFile(backdrop);

        // Only ever seen in the sliver the felt art can't reach on an
        // extreme window aspect ratio, so it's matched to each table's own
        // darkest edge rather than being a colour in its own right.
        BackgroundColor = Color.FromArgb(pageBackground);

        // The shoe/discard piles show the same face-down back the dealer's
        // hole card uses, so a variant switch doesn't leave them showing
        // the wrong colour back next to a re-themed table.
        ShoeImage.Source = ImageSource.FromFile(CardBackFile);
        DiscardImage.Source = ImageSource.FromFile(CardBackFile);

        // Each variant's table is printed with its own house rules, so the
        // printing gets repainted along with the felt underneath it.
        // Attached here rather than in the constructor because every path
        // that sets up a table comes through here, so the printing can't end
        // up unattached on one of them.
        TableRulesView.Drawable = _tableRules;
        _tableRules.Lines = _variant.TableRules;
        TableRulesView.Invalidate();
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

    /// <summary>
    /// Paints the current variant's house rules across the open felt, the way
    /// a real table has them screen-printed on it - see IGameVariant.TableRules
    /// for why the wording lives on the variant rather than here.
    ///
    /// Each line is set on its own shallow arc, struck from a circle centred
    /// far above the table so the line dips lowest in the middle and lifts at
    /// both ends. That's the same curve the felt art itself is drawn on (its
    /// rail, and the pools of light marking the seats, both sit lowest at the
    /// centre), which is what makes the printing look like part of the table
    /// rather than a caption laid over it - and it's the same curve
    /// ApplyHandSlotArc bends the row of seats along.
    ///
    /// Letters are stepped at a fixed angular advance rather than by their
    /// real widths, so the text comes out evenly spaced the way screen-printed
    /// table lettering is. It also avoids having to measure glyphs, which is
    /// exactly the kind of text metric this codebase has already been bitten
    /// by (see HandSlotCardsWidth).
    /// </summary>
    private sealed class TableRulesDrawable : IDrawable
    {
        /// <summary>How far the middle of a line dips below its two ends.</summary>
        private const float ArcSagitta = 18f;

        private const float HeadlineFontSize = 30f;
        private const float RuleFontSize = 18f;

        /// <summary>Horizontal step between letters, as a fraction of the font size - wide enough to read as printed-on lettering rather than as a label.</summary>
        private const float LetterAdvanceRatio = 0.62f;

        private const float LineGap = 16f;

        /// <summary>Kept clear of the seats below, which start where this band ends.</summary>
        private const float BottomInset = 18f;

        private static readonly Color HeadlineInk = Color.FromRgba(255, 233, 168, 100);
        private static readonly Color RuleInk = Color.FromRgba(255, 233, 168, 78);

        public IReadOnlyList<string> Lines { get; set; } = [];

        public void Draw(ICanvas canvas, RectF dirtyRect)
        {
            if (Lines.Count == 0 || dirtyRect.Width <= 0 || dirtyRect.Height <= 0)
            {
                return;
            }

            // Bebas Neue, the same display face the rest of the table uses -
            // but named the way the platform knows it, NOT by the MAUI alias.
            // Drawing into a GraphicsView bypasses MAUI's font registry, so an
            // alias here would quietly fall back to the system face; see
            // AppFonts.DisplayFamily. A name the platform still can't resolve
            // falls back rather than failing, so this needs no guard.
            canvas.Font = new Microsoft.Maui.Graphics.Font(AppFonts.DisplayFamily);

            // Laid out upwards from the bottom of the band: the printing
            // belongs just above the seats, leaving the space higher up clear
            // for round messages. Work out where the LAST line sits first,
            // then back up to the first one.
            var spanToLastBaseline = Lines.Count <= 1
                ? 0f
                : HeadlineFontSize + LineGap + (Lines.Count - 2) * (RuleFontSize + LineGap);
            var baseline = dirtyRect.Bottom - BottomInset - spanToLastBaseline;

            for (var i = 0; i < Lines.Count; i++)
            {
                var isHeadline = i == 0;
                var fontSize = isHeadline ? HeadlineFontSize : RuleFontSize;

                canvas.FontColor = isHeadline ? HeadlineInk : RuleInk;
                canvas.FontSize = fontSize;

                DrawArcedLine(canvas, Lines[i], dirtyRect, baseline, fontSize);

                baseline += fontSize + LineGap;
            }
        }

        private static void DrawArcedLine(ICanvas canvas, string line, RectF dirtyRect, float baseline, float fontSize)
        {
            if (string.IsNullOrEmpty(line))
            {
                return;
            }

            var advance = fontSize * LetterAdvanceRatio;
            var centreX = dirtyRect.Center.X;

            // Half the line's width is the arc's chord; the radius that gives
            // it ArcSagitta of dip follows from r = (w2 + s2) / 2s. A
            // single-character line has no chord to bend, so it just sits flat.
            var halfWidth = (line.Length - 1) * advance / 2f;
            var radius = halfWidth <= 0f
                ? 0f
                : (halfWidth * halfWidth + ArcSagitta * ArcSagitta) / (2f * ArcSagitta);

            // Anything wider than the table gets set flat rather than bent
            // into a curve too tight to read.
            if (radius <= 0f || halfWidth * 2f > dirtyRect.Width)
            {
                // Same optical baseline as the arced path below: that one
                // centres each glyph box ON the baseline, so this has to
                // too, or a single line falling back on a narrow window
                // would print noticeably higher than its neighbours.
                canvas.DrawString(
                    line,
                    dirtyRect.X,
                    baseline - fontSize,
                    dirtyRect.Width,
                    fontSize * 1.4f,
                    Microsoft.Maui.Graphics.HorizontalAlignment.Center,
                    Microsoft.Maui.Graphics.VerticalAlignment.Center);
                return;
            }

            // Circle centred directly above the line's midpoint, so the arc's
            // lowest point is the middle of the line.
            var centreY = baseline - radius;

            for (var i = 0; i < line.Length; i++)
            {
                var offsetX = (i - (line.Length - 1) / 2f) * advance;
                var letterY = centreY + MathF.Sqrt(MathF.Max(0f, radius * radius - offsetX * offsetX));

                // Tangent at this point, so each letter stands square to the
                // curve instead of upright on a sloping line.
                var tiltDegrees = MathF.Asin(Math.Clamp(offsetX / radius, -1f, 1f)) * 180f / MathF.PI;

                canvas.SaveState();
                canvas.Translate(centreX + offsetX, letterY);
                canvas.Rotate(-tiltDegrees);
                canvas.DrawString(
                    line[i].ToString(),
                    -advance,
                    -fontSize,
                    advance * 2f,
                    fontSize * 2f,
                    Microsoft.Maui.Graphics.HorizontalAlignment.Center,
                    Microsoft.Maui.Graphics.VerticalAlignment.Center);
                canvas.RestoreState();
            }
        }
    }
}
