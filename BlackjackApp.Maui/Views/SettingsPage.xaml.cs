using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace BlackjackApp.Maui.Views;

/// <summary>
/// MOBILE PORT of WPF's SettingsWindow.xaml.cs. Save still just logs the
/// selected values and closes - nothing here touches BlackjackApp.core yet.
/// That wiring happens once the engine and page are both ready to consume
/// these choices (same status as the desktop version).
/// </summary>
public partial class SettingsPage : ContentPage
{
    public SettingsPage()
    {
        InitializeComponent();

        DeckCountPicker.ItemsSource = new List<string> { "1", "2", "3", "4", "5", "6" };
        DeckCountPicker.SelectedIndex = 3; // default 4 decks, per the design doc

        HandCountPicker.ItemsSource = new List<string> { "1", "2", "3", "4", "5" };
        HandCountPicker.SelectedIndex = 0; // default 1 hand

        VariantPicker.ItemsSource = new List<string>
        {
            "Standard Blackjack",
            "Black Double Down Madness",
            "War Blackjack",
        };
        VariantPicker.SelectedIndex = 0;
    }

    private async void SaveButton_OnClicked(object? sender, EventArgs e)
    {
        var deckCount = DeckCountPicker.SelectedIndex + 1; // index 0 = 1 deck
        var handCount = HandCountPicker.SelectedIndex + 1; // index 0 = 1 hand
        var variant = VariantPicker.SelectedItem as string;

        Debug.WriteLine($"Settings saved (not yet applied) - Decks: {deckCount}, Hands: {handCount}, Variant: {variant}");
        await Navigation.PopModalAsync();
    }
}
