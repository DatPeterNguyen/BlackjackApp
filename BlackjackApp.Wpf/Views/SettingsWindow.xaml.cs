using System.Diagnostics;
using System.Windows;

namespace BlackjackApp.Wpf.Views;

/// <summary>
/// Interaction logic for SettingsWindow.xaml.
///
/// HOUR 6.5-7.5 SHELL: Save currently just logs the selected values and
/// closes the window. Nothing here touches BlackjackApp.core yet - no deck
/// gets resized, no variant gets swapped. That wiring happens once the
/// engine and table view are both ready to consume these choices.
/// </summary>
public partial class SettingsWindow : Window
{
    public SettingsWindow()
    {
        InitializeComponent(); // Runs the window 
    }

    private void SaveButton_OnClick(object sender, RoutedEventArgs e)
    {
        /*
         * Allows updating of the game settings.
         */
        var deckCount = DeckCountComboBox.SelectedIndex + 1; // index 0 = 1 deck
        var handCount = HandCountComboBox.SelectedIndex + 1; // index 0 = 1 hand
        var variant = (VariantComboBox.SelectedItem as System.Windows.Controls.ComboBoxItem)?.Content;

        Debug.WriteLine($"Settings saved (not yet applied) - Decks: {deckCount}, Hands: {handCount}, Variant: {variant}");
        Close();
    }
}
