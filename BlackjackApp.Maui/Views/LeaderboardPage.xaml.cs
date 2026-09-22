using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BlackjackApp.core.Services;
using BlackjackApp.Maui.Services;
using Microsoft.Maui.Controls.Shapes;

namespace BlackjackApp.Maui.Views;

/// <summary>
/// Local-only leaderboard - see the class doc comment in
/// LeaderboardPage.xaml. Opened from the start menu (GameMenuPage's
/// LeaderboardButton); reads GameProgressStorage.LoadLeaderboard fresh
/// every time it appears, so it's always current with whatever
/// GameProgressStorage.RecordBalanceForLeaderboard has recorded so far.
/// </summary>
public partial class LeaderboardPage : ContentPage
{
    /// <summary>Cancels an in-flight fetch if the page is closed before it lands, so a slow network cannot write rows into a page nobody is looking at.</summary>
    private CancellationTokenSource? _fetch;

    public LeaderboardPage()
    {
        InitializeComponent();

        // Draw the local board immediately. The online one replaces it when
        // and if it arrives - the player should never be looking at a spinner
        // where their own record could already be.
        BuildRows();
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();

        if (AppServices.Leaderboard.IsConfigured)
        {
            _ = LoadOnlineStandingsAsync();
        }
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();

        _fetch?.Cancel();
        _fetch?.Dispose();
        _fetch = null;
    }

    /// <summary>
    /// Replaces the local rows with the global board.
    ///
    /// Failure here is silent on purpose: GetTopAsync already turns being
    /// offline, unreachable or unconfigured into an empty list, and an empty
    /// list simply leaves the local board on screen. Someone playing on a
    /// train should see their own record, not an error.
    /// </summary>
    private async Task LoadOnlineStandingsAsync()
    {
        _fetch?.Cancel();
        _fetch?.Dispose();
        _fetch = new CancellationTokenSource();

        var token = _fetch.Token;

        StatusLabel.Text = "Loading the global board...";
        StatusLabel.IsVisible = true;

        // Re-publish this device's best before reading the board. A submission
        // made while offline is otherwise lost for good: publishing is only
        // triggered by a new personal best, so nothing retries until the
        // player beats themselves again. Re-sending is free - the server keeps
        // whichever value is higher - and this is the one screen where being
        // absent from the board is actually visible.
        var localBest = GameProgressStorage.LoadLocalBestBalance();

        if (localBest > 0)
        {
            await AppServices.Leaderboard.SubmitBestBalanceAsync(
                GameProgressStorage.LoadOnlinePlayerId(),
                GameProgressStorage.LoadOnlinePlayerName(),
                localBest,
                token);
        }

        if (token.IsCancellationRequested)
        {
            return;
        }

        var standings = await AppServices.Leaderboard.GetTopAsync(50, token);

        if (token.IsCancellationRequested)
        {
            return;
        }

        if (standings.Count == 0)
        {
            StatusLabel.Text = "Couldn't reach the global board - showing your own records.";
            return;
        }

        StatusLabel.IsVisible = false;
        BuildOnlineRows(standings);
    }

    /// <summary>
    /// Draws the global board, marking whichever row belongs to this install.
    /// Matched on player id rather than name, since names are editable and
    /// nothing stops two players choosing the same one.
    /// </summary>
    private void BuildOnlineRows(System.Collections.Generic.IReadOnlyList<LeaderboardStanding> standings)
    {
        RowsLayout.Children.Clear();
        EmptyLabel.IsVisible = false;
        RowsLayout.IsVisible = true;

        var localPlayerId = GameProgressStorage.LoadOnlinePlayerId();

        for (var i = 0; i < standings.Count; i++)
        {
            var standing = standings[i];

            RowsLayout.Children.Add(CreateRow(
                rank: i + 1,
                new LeaderboardEntry
                {
                    PlayerName = standing.PlayerId == localPlayerId
                        ? $"{standing.PlayerName} (you)"
                        : standing.PlayerName,
                    HighestBalance = standing.BestBalance,
                    AchievedAtUtc = standing.AchievedAtUtc,
                },
                isLocalPlayer: standing.PlayerId == localPlayerId));
        }
    }

    /// <summary>(Re)builds one row per leaderboard entry, highest balance first - or shows EmptyLabel instead if there's nothing recorded yet.</summary>
    private void BuildRows()
    {
        RowsLayout.Children.Clear();

        var entries = GameProgressStorage.LoadLeaderboard();

        EmptyLabel.IsVisible = entries.Count == 0;
        RowsLayout.IsVisible = entries.Count > 0;

        for (var i = 0; i < entries.Count; i++)
        {
            RowsLayout.Children.Add(CreateRow(rank: i + 1, entries[i]));
        }
    }

    /// <summary>One ranked row: place, player name, their record balance, and the local date/time they reached it. isLocalPlayer rings this install's own row on the global board, where it might be anywhere down the list.</summary>
    private static Border CreateRow(int rank, LeaderboardEntry entry, bool isLocalPlayer = false)
    {
        var rankLabel = new Label
        {
            Text = $"#{rank}",
            TextColor = rank == 1 ? Color.FromArgb("#FFC400") : Color.FromArgb("#E6F3C8"),
            FontSize = 20,
            FontFamily = AppFonts.Display,
            WidthRequest = 36,
            VerticalOptions = LayoutOptions.Center,
        };

        var nameLabel = new Label
        {
            Text = entry.PlayerName,
            TextColor = Color.FromArgb("White"),
            FontSize = 20,
            FontFamily = AppFonts.Display,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.Fill,
            LineBreakMode = LineBreakMode.TailTruncation,
        };

        var dateLabel = new Label
        {
            // ToLocalTime since AchievedAtUtc is stored in UTC (see
            // GameProgressStorage.RecordBalanceForLeaderboard) - shown in
            // whatever timezone the player is actually reading this in.
            Text = entry.AchievedAtUtc.ToLocalTime().ToString("MMM d, yyyy"),
            TextColor = Color.FromArgb("#E6F3C8"),
            FontSize = 11,
            VerticalOptions = LayoutOptions.Center,
        };

        var nameAndDate = new VerticalStackLayout
        {
            Spacing = 1,
            HorizontalOptions = LayoutOptions.Fill,
            Children = { nameLabel, dateLabel },
        };

        var balanceLabel = new Label
        {
            Text = $"${entry.HighestBalance:N0}",
            TextColor = Color.FromArgb("#FFC400"),
            FontSize = 22,
            FontFamily = AppFonts.Display,
            VerticalOptions = LayoutOptions.Center,
            HorizontalOptions = LayoutOptions.End,
        };

        var content = new Grid
        {
            ColumnDefinitions =
            {
                new ColumnDefinition { Width = GridLength.Auto },
                new ColumnDefinition { Width = GridLength.Star },
                new ColumnDefinition { Width = GridLength.Auto },
            },
            ColumnSpacing = 10,
            Children = { rankLabel, nameAndDate, balanceLabel },
        };
        Grid.SetColumn(rankLabel, 0);
        Grid.SetColumn(nameAndDate, 1);
        Grid.SetColumn(balanceLabel, 2);

        return new Border
        {
            Stroke = rank == 1 || isLocalPlayer ? Color.FromArgb("#FFC400") : Color.FromArgb("#6F7D55"),
            StrokeThickness = isLocalPlayer ? 2 : 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Color.FromArgb("#59000000"),
            Padding = 12,
            Content = content,
        };
    }

    private async void CloseButton_OnClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
