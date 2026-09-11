using System;
using System.Threading.Tasks;
using BlackjackApp.core.Variants;
using BlackjackApp.Maui;

namespace BlackjackApp.Maui.Views;

/// <summary>
/// The game menu (item 4 on the polish list) - doubles as two different
/// screens depending on _isStartMenu:
///  - Start menu (the app's actual first screen now, wired up as
///    AppShell's ShellContent instead of MainPage - see the parameterless
///    constructor and AppShell.xaml): shows Play/How to Play/Table
///    Settings. Play opens RulesPage as a mode picker - tap a variant to
///    select it, then RulesPage's own Play button confirms and actually
///    starts a new game with it (see StartGameWith). How to Play opens the
///    same rules content purely for reference (no selection, no Play
///    button there).
///  - Mid-game menu (opened from MainPage's own top-bar Menu button): no
///    Play button (there's already a game in progress) - just How to Play
///    (reference only - picking a different mode mid-hand belongs in Table
///    Settings, which already knows how to queue the change until the
///    round finishes), Table Settings, Exit to Menu (abandons the current
///    game and resets all the way back to the real start menu - see
///    ExitToMenuButton_OnClicked), and Close to get back to the game in
///    progress.
/// </summary>
public partial class GameMenuPage : ContentPage
{
    private readonly bool _isStartMenu;
    private IGameVariant _currentVariant;
    private int _deckCount;
    private int _handCount;

    /// <summary>Forwarded straight through from SettingsPage.SettingsSaved when Table Settings is used from the mid-game menu, so MainPage only ever needs to listen to this one event regardless of which page the save actually came from. Not used in start-menu mode - there's no live game yet to apply anything to.</summary>
    public event Action<IGameVariant, int, int>? SettingsSaved;

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
        StatsBorder.IsVisible = isStartMenu;
        ResetProgressButton.IsVisible = isStartMenu;
        ExitToMenuButton.IsVisible = !isStartMenu;
        CloseButton.IsVisible = !isStartMenu;
        RefreshVariantSummary();

        if (isStartMenu)
        {
            RefreshStatsDisplay();
        }
    }

    private void RefreshVariantSummary() => VariantSummaryLabel.Text = $"Currently playing: {_currentVariant.Name}";

    /// <summary>Start menu only (item 13) - reloads the saved balance/stats and refreshes the record/lifetime-profit/biggest-win-loss labels from them.</summary>
    private void RefreshStatsDisplay()
    {
        var stats = GameProgressStorage.LoadStats();

        RecordLabel.Text = $"Record: {stats.Wins}W - {stats.Losses}L - {stats.Pushes}P";

        NetProfitLabel.Text = stats.NetProfit switch
        {
            > 0 => $"Lifetime: +${stats.NetProfit:N0}",
            < 0 => $"Lifetime: -${-stats.NetProfit:N0}",
            _ => "Lifetime: $0",
        };
        NetProfitLabel.TextColor = stats.NetProfit switch
        {
            > 0 => Color.FromArgb("#4CAF50"),
            < 0 => Color.FromArgb("#FF6B6B"),
            _ => Color.FromArgb("#8FBFA9"),
        };

        BiggestWinLossLabel.Text = $"Biggest win: ${stats.BiggestWin:N0}  |  Biggest loss: ${stats.BiggestLoss:N0}";
    }

    /// <summary>
    /// Start menu only: opens RulesPage in mode-picker mode - shows every
    /// variant's rules, lets the player tap one to select it, and its own
    /// Play button confirms and actually starts the game (see
    /// StartGameWith). This is the sole way to start a new game now, so
    /// reading (or at least glancing at) the rules always happens first.
    /// </summary>
    private async void PlayButton_OnClicked(object? sender, EventArgs e)
    {
        await Navigation.PushModalAsync(new RulesPage(_currentVariant.Name, StartGameWith));
    }

    /// <summary>Pure reference in both contexts - no selection, no bottom Play button. Starting/changing a mode always goes through PlayButton_OnClicked (start menu) or Table Settings (mid-game) instead.</summary>
    private async void HowToPlayButton_OnClicked(object? sender, EventArgs e)
    {
        await Navigation.PushModalAsync(new RulesPage(_currentVariant.Name));
    }

    /// <summary>Called when RulesPage's Play button confirms a mode selection (start menu only) - closes Rules and launches a fresh game with that variant.</summary>
    private async Task StartGameWith(IGameVariant variant)
    {
        _currentVariant = variant;
        RefreshVariantSummary();

        await Navigation.PopModalAsync(); // close RulesPage, back to the start menu
        await Navigation.PushModalAsync(new MainPage(variant, _deckCount, _handCount));
    }

    /// <summary>
    /// Just pushes Settings on top of THIS page - deliberately the same
    /// single push in both start-menu and mid-game contexts, rather than
    /// popping this menu first and immediately pushing Settings in its
    /// place. That pop-then-push sequence turned out to be unreliable (two
    /// modal navigation calls racing back to back), so this instead leaves
    /// the mid-game menu underneath Settings the whole time; Settings pops
    /// only itself on Save, landing back on this menu, and Close (already
    /// wired up) is what actually returns to the game in progress.
    /// </summary>
    private async void TableSettingsButton_OnClicked(object? sender, EventArgs e)
    {
        var settingsPage = new SettingsPage(_currentVariant, _deckCount, _handCount);
        settingsPage.SettingsSaved += (variant, deckCount, handCount) =>
        {
            _currentVariant = variant;
            _deckCount = deckCount;
            _handCount = handCount;
            RefreshVariantSummary();

            // Start menu: nothing else to notify - there's no live game yet,
            // just remembered locally for whenever Play is next tapped.
            // Mid-game: forward on to MainPage so it actually applies (or
            // queues) the change.
            if (!_isStartMenu)
            {
                SettingsSaved?.Invoke(variant, deckCount, handCount);
            }
        };

        await Navigation.PushModalAsync(settingsPage);
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
        var confirmed = await DisplayAlert(
            "Exit to Menu?",
            "This ends your current game. Any bet in progress is lost, and your balance reverts to whatever was last saved (right after your last finished round).",
            "Exit to Menu",
            "Cancel");

        if (!confirmed)
        {
            return;
        }

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
        var confirmed = await DisplayAlert(
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
    }

    private async void CloseButton_OnClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
