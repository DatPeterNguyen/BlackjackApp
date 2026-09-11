using System;
using BlackjackApp.core.Variants;

namespace BlackjackApp.Maui.Views;

/// <summary>
/// The game menu (item 4 on the polish list) - MainPage's top-bar Menu
/// button opens this instead of jumping straight to Settings. Just two
/// destinations for now: a rules/how-to-play reference (RulesPage) and a
/// shortcut into the existing deck/hand/variant Settings page.
/// </summary>
public partial class GameMenuPage : ContentPage
{
    private readonly IGameVariant _currentVariant;
    private readonly int _deckCount;
    private readonly int _handCount;

    /// <summary>Forwarded straight through from SettingsPage.SettingsSaved when Table Settings is used from here, so MainPage only ever needs to listen to this one event regardless of which page the save actually came from.</summary>
    public event Action<IGameVariant, int, int>? SettingsSaved;

    public GameMenuPage(IGameVariant currentVariant, int deckCount, int handCount)
    {
        InitializeComponent();
        _currentVariant = currentVariant;
        _deckCount = deckCount;
        _handCount = handCount;
        VariantSummaryLabel.Text = $"Currently playing: {currentVariant.Name}";
    }

    private async void HowToPlayButton_OnClicked(object? sender, EventArgs e)
    {
        await Navigation.PushModalAsync(new RulesPage(_currentVariant.Name));
    }

    /// <summary>
    /// Pops the menu itself before pushing Settings, so Settings ends up
    /// sitting directly on top of MainPage - the same modal depth as the
    /// old standalone Settings button - rather than stacking Settings on
    /// top of a lingering Menu page underneath it.
    /// </summary>
    private async void TableSettingsButton_OnClicked(object? sender, EventArgs e)
    {
        var settingsPage = new SettingsPage(_currentVariant, _deckCount, _handCount);
        settingsPage.SettingsSaved += (variant, deckCount, handCount) => SettingsSaved?.Invoke(variant, deckCount, handCount);

        await Navigation.PopModalAsync();
        await Navigation.PushModalAsync(settingsPage);
    }

    private async void CloseButton_OnClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
