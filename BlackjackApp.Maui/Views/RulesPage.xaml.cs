using System;
using System.Threading.Tasks;
using BlackjackApp.core.Variants;

namespace BlackjackApp.Maui.Views;

/// <summary>
/// Rules reference for all three variants (game menu item 4) - opened from
/// GameMenuPage's "How to Play" AND "Play" buttons, which put it in one of
/// two modes:
///
///  - Reference mode (onPlaySelected is null - "How to Play", both start
///    menu and mid-game): just shows the rules, with whichever variant is
///    currently being played tagged "(Currently Playing)". Sections aren't
///    selectable and there's no bottom Play button.
///
///  - Mode-picker mode (onPlaySelected is provided - "Play", start menu
///    only): tapping a section selects it (tagged "(Selected)",
///    highlighted in gold - starts on whatever's currently configured),
///    and the bottom Play button confirms, calling onPlaySelected with the
///    currently selected variant to actually start a game with it.
/// </summary>
public partial class RulesPage : ContentPage
{
    private readonly Func<IGameVariant, Task>? _onPlaySelected;
    private string _selectedVariantName;

    public RulesPage(string currentVariantName, Func<IGameVariant, Task>? onPlaySelected = null)
    {
        InitializeComponent();
        _onPlaySelected = onPlaySelected;
        _selectedVariantName = currentVariantName;

        var isModePicker = onPlaySelected is not null;
        SelectionHintLabel.IsVisible = isModePicker;
        PlayButton.IsVisible = isModePicker;

        RefreshSectionHighlights();
    }

    private void RefreshSectionHighlights()
    {
        var isModePicker = _onPlaySelected is not null;
        var tag = isModePicker ? "Selected" : "Currently Playing";

        var sections = new (string Name, Border Border, Label TitleLabel)[]
        {
            ("Standard Blackjack", StandardSection, StandardTitleLabel),
            ("Black Double Down Madness", MadnessSection, MadnessTitleLabel),
            ("War Blackjack", WarSection, WarTitleLabel),
        };

        foreach (var (name, border, titleLabel) in sections)
        {
            var isSelected = name == _selectedVariantName;
            border.Stroke = isSelected ? Color.FromArgb("#FFC400") : Color.FromArgb("#6F7D55");
            border.StrokeThickness = isSelected ? 2 : 1;
            titleLabel.Text = isSelected ? $"{name} ({tag})" : name;
        }
    }

    private void SelectVariant(string variantName)
    {
        // Tapping a section only means anything in mode-picker mode -
        // reference mode's highlight always just reflects whatever's
        // actually being played, so it isn't tap-driven.
        if (_onPlaySelected is null)
        {
            return;
        }

        _selectedVariantName = variantName;
        RefreshSectionHighlights();
    }

    private void StandardSection_OnTapped(object? sender, EventArgs e) => SelectVariant("Standard Blackjack");

    private void MadnessSection_OnTapped(object? sender, EventArgs e) => SelectVariant("Black Double Down Madness");

    private void WarSection_OnTapped(object? sender, EventArgs e) => SelectVariant("War Blackjack");

    private static IGameVariant CreateVariant(string variantName) => variantName switch
    {
        "Black Double Down Madness" => new DoubleDownMadnessVariant(),
        "War Blackjack" => new WarBlackjackVariant(),
        _ => new StandardBlackjackVariant(),
    };

    private async void PlayButton_OnClicked(object? sender, EventArgs e)
    {
        if (_onPlaySelected is { } onPlaySelected)
        {
            await onPlaySelected(CreateVariant(_selectedVariantName));
        }
    }

    private async void CloseButton_OnClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
