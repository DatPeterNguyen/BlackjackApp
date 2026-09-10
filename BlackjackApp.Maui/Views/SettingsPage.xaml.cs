using System;
using System.Collections.Generic;
using System.Diagnostics;
using BlackjackApp.core.Variants;

namespace BlackjackApp.Maui.Views;

/// <summary>
/// Deck count, hand count, and variant are all real now - saving fires
/// SettingsSaved so MainPage can swap in the chosen IGameVariant, shoe
/// size, and number of simultaneous player hands (1-5).
/// </summary>
public partial class SettingsPage : ContentPage
{
    /// <summary>Raised when Save is tapped, carrying the chosen variant instance, deck count, and hand count.</summary>
    public event Action<IGameVariant, int, int>? SettingsSaved;

    public SettingsPage(IGameVariant currentVariant, int currentDeckCount, int currentHandCount)
    {
        InitializeComponent();

        DeckCountPicker.ItemsSource = new List<string> { "1", "2", "3", "4", "5", "6" };
        DeckCountPicker.SelectedIndex = Math.Clamp(currentDeckCount - 1, 0, 5);

        HandCountPicker.ItemsSource = new List<string> { "1", "2", "3", "4", "5" };
        HandCountPicker.SelectedIndex = Math.Clamp(currentHandCount - 1, 0, 4);

        VariantPicker.ItemsSource = new List<string>
        {
            "Standard Blackjack",
            "Black Double Down Madness",
            "War Blackjack",
        };
        VariantPicker.SelectedIndex = currentVariant switch
        {
            DoubleDownMadnessVariant => 1,
            WarBlackjackVariant => 2,
            _ => 0,
        };
    }

    private async void SaveButton_OnClicked(object? sender, EventArgs e)
    {
        var deckCount = DeckCountPicker.SelectedIndex + 1; // index 0 = 1 deck
        var handCount = HandCountPicker.SelectedIndex + 1; // index 0 = 1 hand

        IGameVariant variant = VariantPicker.SelectedIndex switch
        {
            1 => new DoubleDownMadnessVariant(),
            2 => new WarBlackjackVariant(),
            _ => new StandardBlackjackVariant(),
        };

        Debug.WriteLine($"Settings saved - Decks: {deckCount}, Hands: {handCount}, Variant: {variant.Name}");
        SettingsSaved?.Invoke(variant, deckCount, handCount);
        await Navigation.PopModalAsync();
    }
}
