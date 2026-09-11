using System;

namespace BlackjackApp.Maui.Views;

/// <summary>
/// Rules reference for all three variants (game menu item 4) - opened from
/// GameMenuPage's "How to Play" button. All three rule sets are always
/// shown (so switching variants in Settings never leaves you looking at the
/// wrong page), with whichever one is currently active highlighted in gold.
/// </summary>
public partial class RulesPage : ContentPage
{
    public RulesPage(string currentVariantName)
    {
        InitializeComponent();
        HighlightCurrentVariant(currentVariantName);
    }

    private void HighlightCurrentVariant(string currentVariantName)
    {
        var sections = new (string Name, Border Border, Label TitleLabel)[]
        {
            ("Standard Blackjack", StandardSection, StandardTitleLabel),
            ("Black Double Down Madness", MadnessSection, MadnessTitleLabel),
            ("War Blackjack", WarSection, WarTitleLabel),
        };

        foreach (var (name, border, titleLabel) in sections)
        {
            var isCurrent = name == currentVariantName;
            border.Stroke = isCurrent ? Color.FromArgb("#FFD700") : Color.FromArgb("#3A6B57");
            border.StrokeThickness = isCurrent ? 2 : 1;
            titleLabel.Text = isCurrent ? $"{name} (Currently Playing)" : name;
        }
    }

    private async void CloseButton_OnClicked(object? sender, EventArgs e)
    {
        await Navigation.PopModalAsync();
    }
}
