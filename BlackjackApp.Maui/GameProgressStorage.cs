using BlackjackApp.core.Economy;
using Microsoft.Maui.Storage;

namespace BlackjackApp.Maui;

/// <summary>
/// Auto-saves the player's balance and lifetime stats (item 13 on the
/// polish list) via Microsoft.Maui.Storage.Preferences - simple durable
/// key-value storage that survives app restarts. Deliberately just the
/// balance and stats, not a full game-state save: there's no mid-round
/// resume, a fresh launch (or a new game after Exit to Menu) just picks up
/// the wallet and record where they were left.
///
/// ChipWallet and GameStats both live in BlackjackApp.core, which targets
/// plain net (no MAUI platform surface), so Preferences can't be called
/// from there - this class is the MAUI-side bridge that reads/writes them.
/// Preferences has no native decimal support, so amounts round-trip through
/// double, which is far more precision than chip amounts ever need.
/// </summary>
public static class GameProgressStorage
{
    private const string BalanceKey = "progress_balance";
    private const string WinsKey = "progress_wins";
    private const string LossesKey = "progress_losses";
    private const string PushesKey = "progress_pushes";
    private const string NetProfitKey = "progress_net_profit";
    private const string BiggestWinKey = "progress_biggest_win";
    private const string BiggestLossKey = "progress_biggest_loss";

    /// <summary>Starting balance for a brand new player, or after Reset Progress.</summary>
    public const decimal DefaultStartingBalance = 1000m;

    public static decimal LoadBalance() => (decimal)Preferences.Default.Get(BalanceKey, (double)DefaultStartingBalance);

    public static GameStats LoadStats() => new(
        wins: Preferences.Default.Get(WinsKey, 0),
        losses: Preferences.Default.Get(LossesKey, 0),
        pushes: Preferences.Default.Get(PushesKey, 0),
        netProfit: (decimal)Preferences.Default.Get(NetProfitKey, 0.0),
        biggestWin: (decimal)Preferences.Default.Get(BiggestWinKey, 0.0),
        biggestLoss: (decimal)Preferences.Default.Get(BiggestLossKey, 0.0));

    /// <summary>Persists the current balance and stats - called whenever a round finishes (see MainPage.EndRound).</summary>
    public static void Save(ChipWallet wallet, GameStats stats)
    {
        Preferences.Default.Set(BalanceKey, (double)wallet.Balance);
        Preferences.Default.Set(WinsKey, stats.Wins);
        Preferences.Default.Set(LossesKey, stats.Losses);
        Preferences.Default.Set(PushesKey, stats.Pushes);
        Preferences.Default.Set(NetProfitKey, (double)stats.NetProfit);
        Preferences.Default.Set(BiggestWinKey, (double)stats.BiggestWin);
        Preferences.Default.Set(BiggestLossKey, (double)stats.BiggestLoss);
    }

    /// <summary>Wipes every saved value back to defaults - the Reset Progress button on the start menu.</summary>
    public static void ResetAll()
    {
        Preferences.Default.Remove(BalanceKey);
        Preferences.Default.Remove(WinsKey);
        Preferences.Default.Remove(LossesKey);
        Preferences.Default.Remove(PushesKey);
        Preferences.Default.Remove(NetProfitKey);
        Preferences.Default.Remove(BiggestWinKey);
        Preferences.Default.Remove(BiggestLossKey);
    }
}
