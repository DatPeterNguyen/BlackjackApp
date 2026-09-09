using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using BlackjackApp.Wpf.Views;

namespace BlackjackApp.Wpf;

/// <summary>
/// Interaction logic for MainWindow.xaml.
///
/// HOUR 0-4 SCAFFOLDING: everything below is placeholder wiring for the Day 1
/// UI-first pass. The bet total is a throwaway field, not the real
/// BlackjackApp.core.Models.ChipWallet, and every action button just logs to
/// the Debug output instead of touching real game state. This proves the
/// interaction pattern (click chip -> bet grows, click action -> something
/// happens) before any of it is wired to the actual engine.
/// </summary>
public partial class MainWindow : Window
{
    // Placeholder only - will be replaced by a real ChipWallet-backed
    // bet amount once betting logic exists.
    private int _currentBet;

    public MainWindow()
    {
        InitializeComponent();
    }

    private void ChipButton_OnClick(object sender, RoutedEventArgs e)
    {
        if (sender is Button { Tag: string tagValue } && int.TryParse(tagValue, out var chipValue))
        {
            _currentBet += chipValue;
            CurrentBetText.Text = $"Current Bet: ${_currentBet:N0}";
        }
    }

    private void ClearBetButton_OnClick(object sender, RoutedEventArgs e)
    {
        _currentBet = 0;
        CurrentBetText.Text = "Current Bet: $0";
    }

    private void SettingsButton_OnClick(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow { Owner = this };
        settingsWindow.ShowDialog();
    }

    private void DealButton_OnClick(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine($"Deal clicked - would deal with bet of ${_currentBet}.");
    }

    private void HitButton_OnClick(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("Hit clicked - no hand logic yet.");
    }

    private void StandButton_OnClick(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("Stand clicked - no hand logic yet.");
    }

    private void DoubleButton_OnClick(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("Double clicked - no hand logic yet.");
    }

    private void SplitButton_OnClick(object sender, RoutedEventArgs e)
    {
        Debug.WriteLine("Split clicked - no hand logic yet.");
    }
}
