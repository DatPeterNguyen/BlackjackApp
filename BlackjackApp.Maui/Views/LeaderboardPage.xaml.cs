using System;
using System.Linq;
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
    public LeaderboardPage()
    {
        InitializeComponent();
        BuildRows();
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

    /// <summary>One ranked row: place, player name, their record balance, and the local date/time they reached it.</summary>
    private static Border CreateRow(int rank, LeaderboardEntry entry)
    {
        var rankLabel = new Label
        {
            Text = $"#{rank}",
            TextColor = rank == 1 ? Color.FromArgb("#FFD700") : Color.FromArgb("#8FBFA9"),
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
            WidthRequest = 36,
            VerticalOptions = LayoutOptions.Center,
        };

        var nameLabel = new Label
        {
            Text = entry.PlayerName,
            TextColor = Color.FromArgb("White"),
            FontSize = 16,
            FontAttributes = FontAttributes.Bold,
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
            TextColor = Color.FromArgb("#8FBFA9"),
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
            TextColor = Color.FromArgb("#FFD700"),
            FontSize = 17,
            FontAttributes = FontAttributes.Bold,
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
            Stroke = rank == 1 ? Color.FromArgb("#FFD700") : Color.FromArgb("#3A6B57"),
            StrokeThickness = 1,
            StrokeShape = new RoundRectangle { CornerRadius = 8 },
            BackgroundColor = Color.FromArgb("#0E4433"),
            Padding = 12,
            Content = content,
        };
    }

    private async void CloseButton_OnClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
