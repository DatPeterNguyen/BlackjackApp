using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BlackjackApp.core.Services;
using BlackjackApp.core.Variants;
using BlackjackApp.Maui;

namespace BlackjackApp.Maui.Views;

/// <summary>
/// The game menu (item 4 on the polish list) - doubles as two different
/// screens depending on _isStartMenu:
///  - Start menu (the app's actual first screen now, wired up as
///    AppShell's ShellContent instead of MainPage - see the parameterless
///    constructor and AppShell.xaml): shows Play/Load Game/Daily Reward/
///    Table Settings. Play opens RulesPage as a mode picker - tap a
///    variant to select it, then RulesPage's own Play button confirms and
///    actually starts a new game with it (see StartGameWith). Load Game
///    resumes a round that was still in progress when the app last closed
///    (see GameProgressStorage.HasInProgressRound/LoadInProgressRound and
///    MainPage's resume constructor) - hidden whenever there's nothing
///    saved to resume.
///  - Mid-game menu (opened from MainPage's own top-bar Menu button):
///    Table Settings, Exit to Menu (abandons the current game and resets
///    all the way back to the real start menu - see
///    ExitToMenuButton_OnClicked), and Close to get back to the game in
///    progress.
/// Table Settings itself is a small inline overlay on this same page
/// (SettingsOverlayBackdrop/SettingsOverlayCard in the XAML) rather than a
/// separate pushed page - it's just deck count and hand count now, since
/// the variant can only be chosen by starting a fresh game via Play.
/// </summary>
public partial class GameMenuPage : ContentPage
{
    private readonly bool _isStartMenu;
    private IGameVariant _currentVariant;
    private int _deckCount;
    private int _handCount;

    /// <summary>Start menu only - makes the automatic daily-reward popup fire at most once per visit to the menu, rather than every time a modal closes over it.</summary>
    private bool _checkInPromptShown;

    /// <summary>Forwarded straight through from the Table Settings overlay's Save button when it's used from the mid-game menu, so MainPage only ever needs to listen to this one event regardless of which page instance the save actually came from. Carries just deck count and hand count now - not used in start-menu mode, since there's no live game yet to apply anything to.</summary>
    public event Action<int, int>? SettingsSaved;

    /// <summary>Parameterless constructor for AppShell's ShellContent DataTemplate - this is what actually makes GameMenuPage the app's start screen.</summary>
    public GameMenuPage() : this(new StandardBlackjackVariant(), 4, 1, isStartMenu: true)
    {
    }

    public GameMenuPage(IGameVariant currentVariant, int deckCount, int handCount, bool isStartMenu = false)
    {
        InitializeComponent();
        _currentVariant = currentVariant;
        _deckCount = deckCount;
        _handCount = handCount;
        _isStartMenu = isStartMenu;

        Title = isStartMenu ? "Blackjack" : "Menu";
        MenuTitleLabel.Text = isStartMenu ? "Blackjack" : "Menu";
        PlayButton.IsVisible = isStartMenu;
        LoadGameButton.IsVisible = false; // refreshed below once we know whether there's actually a save
        StatsBorder.IsVisible = isStartMenu;
        ResetProgressButton.IsVisible = isStartMenu;
        ExitToMenuButton.IsVisible = !isStartMenu;
        DailyRewardButton.IsVisible = isStartMenu;
        LeaderboardButton.IsVisible = isStartMenu;
        ShopButton.IsVisible = isStartMenu;
        CloseButton.IsVisible = !isStartMenu;
        RefreshVariantSummary();

        if (isStartMenu)
        {
            RefreshStatsDisplay();
            RefreshDailyRewardButton();
            RefreshLoadGameButton();
        }
    }

    /// <summary>
    /// Start menu only (item 14) - opens the daily check-in reward. It also
    /// opens on its own from OnAppearing the first time the menu appears with
    /// something to claim, so a returning player never has to go looking for
    /// it; this button is how they check the streak the rest of the time.
    /// </summary>
    private async void DailyRewardButton_OnClicked(object? sender, EventArgs e)
    {
        await ShowCheckInAsync();
    }

    /// <summary>Start menu only - opens the local leaderboard (see Views/LeaderboardPage).</summary>
    private async void LeaderboardButton_OnClicked(object? sender, EventArgs e)
    {
        await Navigation.PushModalAsync(new LeaderboardPage());
    }

    /// <summary>Start menu only - opens the chip shop. It is a shelf with nothing behind the counter: every bundle is shown and none can be bought, because IChipPurchaseService has no implementation.</summary>
    private async void ShopButton_OnClicked(object? sender, EventArgs e)
    {
        await Navigation.PushModalAsync(new ShopPage());
    }

    private async Task ShowCheckInAsync()
    {
        var checkInPage = new CheckInPage();
        checkInPage.Claimed += () =>
        {
            RefreshStatsDisplay();
            RefreshDailyRewardButton();
        };

        await Navigation.PushModalAsync(checkInPage);
    }

    /// <summary>Badges the button whenever a reward is actually waiting, so the menu itself advertises it rather than hiding it behind a tap.</summary>
    private void RefreshDailyRewardButton()
    {
        if (!_isStartMenu)
        {
            return;
        }

        var status = CurrentCheckInStatus();

        DailyRewardButton.Text = status.CanClaim
            ? $"Daily Reward - Day {status.StreakDay} Ready!"
            : "Daily Reward";
        DailyRewardButton.FontAttributes = status.CanClaim ? FontAttributes.Bold : FontAttributes.None;
    }

    private static CheckInStatus CurrentCheckInStatus() => DailyCheckIn.GetStatus(
        GameProgressStorage.LoadLastCheckInUtc(),
        GameProgressStorage.LoadCheckInStreakDay(),
        DateTime.UtcNow);

    /// <summary>Start menu only - shows Load Game only when there's actually an unfinished round saved (see GameProgressStorage.HasInProgressRound), so it doesn't sit there as a dead button on a normal fresh launch.</summary>
    private void RefreshLoadGameButton()
    {
        if (!_isStartMenu)
        {
            return;
        }

        LoadGameButton.IsVisible = GameProgressStorage.HasInProgressRound();
    }

    /// <summary>Start menu only - opens the saved in-progress round exactly as MainPage.PersistInProgressRound left it. If the save turns out to be missing or unreadable (GameProgressStorage.LoadInProgressRound already treats a corrupt save as none), just refreshes the button away instead of launching anything.</summary>
    private async void LoadGameButton_OnClicked(object? sender, EventArgs e)
    {
        var savedRound = GameProgressStorage.LoadInProgressRound();

        if (savedRound is null)
        {
            RefreshLoadGameButton();
            return;
        }

        await Navigation.PushModalAsync(new MainPage(savedRound));
    }

    /// <summary>
    /// Re-reads the saved stats and streak every time the start menu comes
    /// back into view (returning from a game, from the settings overlay, or
    /// from the reward itself), and pops the daily reward open unprompted the
    /// first time it appears with something to claim. The short delay lets
    /// the menu finish appearing before a modal gets stacked on top of it.
    /// </summary>
    protected override async void OnAppearing()
    {
        base.OnAppearing();

        if (!_isStartMenu)
        {
            return;
        }

        RefreshStatsDisplay();
        RefreshDailyRewardButton();
        RefreshLoadGameButton();

        if (_checkInPromptShown || !CurrentCheckInStatus().CanClaim)
        {
            return;
        }

        _checkInPromptShown = true;

        await Task.Delay(250);
        await ShowCheckInAsync();
    }

    private void RefreshVariantSummary() => VariantSummaryLabel.Text = $"Currently playing: {_currentVariant.Name}";

    /// <summary>Start menu only (item 13) - reloads the saved balance/stats and refreshes the record/lifetime-profit/period-profit/biggest-win-loss labels from them.</summary>
    private void RefreshStatsDisplay()
    {
        var stats = GameProgressStorage.LoadStats();

        MenuBalanceLabel.Text = $"Balance: ${GameProgressStorage.LoadBalance():N0}";

        RecordLabel.Text = $"Record: {stats.Wins}W - {stats.Losses}L - {stats.Pushes}P";

        NetProfitLabel.Text = FormatSignedNet("Lifetime", stats.NetProfit);
        NetProfitLabel.TextColor = ColorForNet(stats.NetProfit);

        WeeklyNetProfitLabel.Text = FormatSignedNet("Week", stats.WeeklyNetProfit);
        WeeklyNetProfitLabel.TextColor = ColorForNet(stats.WeeklyNetProfit);

        MonthlyNetProfitLabel.Text = FormatSignedNet("Month", stats.MonthlyNetProfit);
        MonthlyNetProfitLabel.TextColor = ColorForNet(stats.MonthlyNetProfit);

        YearToDateNetProfitLabel.Text = FormatSignedNet("YTD", stats.YearToDateNetProfit);
        YearToDateNetProfitLabel.TextColor = ColorForNet(stats.YearToDateNetProfit);

        BiggestWinLossLabel.Text = $"Biggest win: ${stats.BiggestWin:N0}  |  Biggest loss: ${stats.BiggestLoss:N0}";
    }

    /// <summary>Formats a labeled net profit/loss figure as "Label: +$N" / "Label: -$N" / "Label: $0" - shared by the lifetime and every period figure in RefreshStatsDisplay so they read identically.</summary>
    private static string FormatSignedNet(string label, decimal net) => net switch
    {
        > 0 => $"{label}: +${net:N0}",
        < 0 => $"{label}: -${-net:N0}",
        _ => $"{label}: $0",
    };

    /// <summary>Green for a positive net, red for negative, muted green-grey for exactly zero - shared by the lifetime and every period figure in RefreshStatsDisplay.</summary>
    private static Color ColorForNet(decimal net) => net switch
    {
        > 0 => Color.FromArgb("#4CAF50"),
        < 0 => Color.FromArgb("#FF6B6B"),
        _ => Color.FromArgb("#E6F3C8"),
    };

    /// <summary>
    /// Start menu only: opens RulesPage in mode-picker mode - shows every
    /// variant's rules, lets the player tap one to select it, and its own
    /// Play button confirms and actually starts the game (see
    /// StartGameWith). This is the sole way to start a new game now, so
    /// reading (or at least glancing at) the rules always happens first.
    /// </summary>
    private async void PlayButton_OnClicked(object? sender, EventArgs e)
    {
        // Starting a fresh game abandons any saved in-progress round outright
        // (StartGameWith below clears it) - warn first, since that round's
        // wallet balance reflects money already committed to bets that would
        // otherwise just vanish silently. Nothing to warn about if there's no
        // save to lose.
        if (GameProgressStorage.HasInProgressRound())
        {
            var confirmed = await DisplayAlertAsync(
                "Start a New Game?",
                "You have a saved game in progress. Starting a new one abandons it - any money on the table there will be lost. Use Load Game instead if you want to finish it first.",
                "Start New Game",
                "Cancel");

            if (!confirmed)
            {
                return;
            }
        }

        await Navigation.PushModalAsync(new RulesPage(_currentVariant.Name, StartGameWith));
    }

    /// <summary>Called when RulesPage's Play button confirms a mode selection (start menu only) - closes Rules and launches a fresh game with that variant.</summary>
    private async Task StartGameWith(IGameVariant variant)
    {
        // Committing to the new game now - drop the old save (see the
        // warning in PlayButton_OnClicked) so it doesn't linger as a stale
        // Load Game entry for a round that's being replaced.
        GameProgressStorage.ClearInProgressRound();

        _currentVariant = variant;
        RefreshVariantSummary();

        // Closing RulesPage WITHOUT animation, and that is the whole point.
        //
        // This is the only place in the app that dismisses one modal and
        // presents another back to back, and it is also the one thing you do
        // to start a game - which is why the table could hang on Play while
        // Load Game, which pushes MainPage with nothing to dismiss first,
        // worked. An animated dismissal is still in flight when the next
        // present is issued, and MAUI is known to blank or hang when modal
        // operations are stacked up before the platform transition finishes
        // (dotnet/maui#32310). iOS is the strict one here: UIKit will not
        // present on a controller that is mid-dismiss.
        //
        // An unanimated pop has no transition to collide with. The yields are
        // belt and braces - one turn of the loop each, so the dismissal is
        // fully retired before the present goes out.
        //
        // This file already learned this lesson once: see ExitToMenu, where
        // back-to-back modal pops "outrun each other" and were replaced by
        // resetting the window outright.
        await Navigation.PopModalAsync(animated: false);
        await Task.Yield();
        await Task.Yield();

        await Navigation.PushModalAsync(new MainPage(variant, _deckCount, _handCount));
    }

    /// <summary>
    /// Opens the Table Settings overlay right on top of this same page -
    /// no navigation at all, so it works identically whether this is the
    /// start menu or the mid-game menu. Populates the deck/hand count
    /// pickers from whatever's current before showing it.
    /// </summary>
    private void TableSettingsButton_OnClicked(object? sender, EventArgs e)
    {
        DeckCountPicker.ItemsSource = new List<string> { "1", "2", "3", "4", "5", "6" };
        DeckCountPicker.SelectedIndex = Math.Clamp(_deckCount - 1, 0, 5);

        HandCountPicker.ItemsSource = new List<string> { "1", "2", "3", "4", "5" };
        HandCountPicker.SelectedIndex = Math.Clamp(_handCount - 1, 0, 4);

        SettingsOverlayBackdrop.IsVisible = true;
        SettingsOverlayCard.IsVisible = true;
    }

    /// <summary>Applies the chosen deck/hand counts, forwards them on via SettingsSaved (mid-game only - see the class doc comment), and closes the overlay.</summary>
    private void SettingsSaveButton_OnClicked(object? sender, EventArgs e)
    {
        var deckCount = DeckCountPicker.SelectedIndex + 1; // index 0 = 1 deck
        var handCount = HandCountPicker.SelectedIndex + 1; // index 0 = 1 hand

        _deckCount = deckCount;
        _handCount = handCount;

        // Start menu: nothing else to notify - there's no live game yet,
        // just remembered locally for whenever Play is next tapped.
        // Mid-game: forward on to MainPage so it actually applies (or
        // queues) the change.
        if (!_isStartMenu)
        {
            SettingsSaved?.Invoke(deckCount, handCount);
        }

        HideSettingsOverlay();
    }

    /// <summary>Closes the overlay without applying anything - wired to both the Cancel button and a tap on the backdrop.</summary>
    private void SettingsCancelButton_OnClicked(object? sender, EventArgs e) => HideSettingsOverlay();

    private void HideSettingsOverlay()
    {
        SettingsOverlayBackdrop.IsVisible = false;
        SettingsOverlayCard.IsVisible = false;
    }

    /// <summary>
    /// Mid-game only: abandons the current game entirely (whatever round
    /// is in progress, current wallet balance and all) and returns to the
    /// real start menu, where a brand new game can be started fresh - the
    /// requested way to jump straight from mid-round into picking a new
    /// game rather than having to fully quit and relaunch the app.
    /// </summary>
    private async void ExitToMenuButton_OnClicked(object? sender, EventArgs e)
    {
        // If a hand is still in progress, it was already saved after the
        // last action taken on it (see MainPage.PersistInProgressRound), so
        // leaving via this button doesn't lose it - it'll be waiting on the
        // start menu's Load Game button. Word the confirmation accordingly
        // rather than warning about a loss that no longer happens.
        var hasSavedRound = GameProgressStorage.HasInProgressRound();
        var message = hasSavedRound
            ? "Your current hand will be saved so you can pick it up later with Load Game."
            : "This returns you to the start menu.";

        var confirmed = await DisplayAlertAsync("Exit to Menu?", message, "Exit to Menu", "Cancel");

        if (!confirmed)
        {
            return;
        }

        // Deliberately NOT clearing the in-progress-round save here anymore
        // - Exit to Menu is meant to be resumable via Load Game, not an
        // abandon action (see the confirmation message above). The save is
        // only ever cleared by finishing a round normally (MainPage.EndRound)
        // or by Reset Progress.

        // Unwinding the modal stack page-by-page (this menu, then MainPage,
        // then whatever else got pushed on top) turned out to be unreliable
        // - back-to-back PopModalAsync calls can outrun each other. Instead,
        // just throw away the whole navigation state and start over at a
        // brand new AppShell, which lands on a fresh start-menu GameMenuPage
        // with no modal stack at all. Set via the current Window (rather
        // than the older Application.MainPage shim) since App.CreateWindow
        // is what this app actually starts from.
        if (Application.Current?.Windows.Count > 0)
        {
            Application.Current.Windows[0].Page = new AppShell();
        }
    }

    /// <summary>Start menu only (item 13) - wipes the saved balance and lifetime stats back to a fresh start (see GameProgressStorage.ResetAll), then refreshes the display to show it took.</summary>
    private async void ResetProgressButton_OnClicked(object? sender, EventArgs e)
    {
        var confirmed = await DisplayAlertAsync(
            "Reset Progress?",
            $"This resets your balance back to ${GameProgressStorage.DefaultStartingBalance:N0} and clears your win/loss record. This can't be undone.",
            "Reset",
            "Cancel");

        if (!confirmed)
        {
            return;
        }

        GameProgressStorage.ResetAll();
        RefreshStatsDisplay();
        RefreshLoadGameButton();
    }

    private async void CloseButton_OnClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
