using System.Globalization;
using BlackjackApp.core.Economy;
using Microsoft.Maui.Storage;

namespace BlackjackApp.Maui;

/// <summary>
/// Auto-saves the player's balance and lifetime stats (item 13 on the
/// polish list) plus their daily check-in streak (item 14) via
/// Microsoft.Maui.Storage.Preferences - simple durable key-value storage
/// that survives app restarts. Deliberately just the balance, stats and
/// streak, not a full game-state save: there's no mid-round resume, a fresh
/// launch (or a new game after Exit to Menu) just picks up the wallet and
/// record where they were left.
///
/// ChipWallet and GameStats both live in BlackjackApp.core, which targets
/// plain net (no MAUI platform surface), so Preferences can't be called
/// from there - this class is the MAUI-side bridge that reads/writes them.
/// Preferences has no native decimal support, so amounts round-trip through
/// double, which is far more precision than chip amounts ever need; dates
/// round-trip through an invariant yyyy-MM-dd string for the same reason.
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
    private const string LastCheckInKey = "progress_last_check_in";
    private const string CheckInStreakDayKey = "progress_check_in_streak_day";

    private const string DateFormat = "yyyy-MM-dd";

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

    /// <summary>The calendar date of the player's most recent check-in claim, or null if they've never claimed one.</summary>
    public static DateOnly? LoadLastCheckIn()
    {
        var stored = Preferences.Default.Get(LastCheckInKey, string.Empty);

        return DateOnly.TryParseExact(stored, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary>Which day of the 7-day cycle the last claim was. 0 for a player who has never claimed.</summary>
    public static int LoadCheckInStreakDay() => Preferences.Default.Get(CheckInStreakDayKey, 0);

    /// <summary>Persists a freshly claimed check-in so tomorrow's claim knows where the streak stands.</summary>
    public static void SaveCheckIn(DateOnly claimedOn, int streakDay)
    {
        Preferences.Default.Set(LastCheckInKey, claimedOn.ToString(DateFormat, CultureInfo.InvariantCulture));
        Preferences.Default.Set(CheckInStreakDayKey, streakDay);
    }

    /// <summary>Persists the balance on its own - used by the check-in reward, which pays out from the menu with no live game or stats to save alongside it.</summary>
    public static void SaveBalance(decimal balance) => Preferences.Default.Set(BalanceKey, (double)balance);

    /// <summary>Persists the current balance and stats - called whenever a round finishes (see MainPage.EndRound).</summary>
    public static void Save(ChipWallet wallet, GameStats stats)
    {
        SaveBalance(wallet.Balance);
        Preferences.Default.Set(WinsKey, stats.Wins);
        Preferences.Default.Set(LossesKey, stats.Losses);
        Preferences.Default.Set(PushesKey, stats.Pushes);
        Preferences.Default.Set(NetProfitKey, (double)stats.NetProfit);
        Preferences.Default.Set(BiggestWinKey, (double)stats.BiggestWin);
        Preferences.Default.Set(BiggestLossKey, (double)stats.BiggestLoss);
    }

    /// <summary>
    /// Wipes the saved balance and lifetime stats back to defaults - the
    /// Reset Progress button on the start menu. The check-in streak resets
    /// with them, but the last claim DATE deliberately survives: clearing it
    /// too would let today's reward be claimed a second time just by hitting
    /// Reset, which is a free-money button rather than a fresh start.
    /// </summary>
    public static void ResetAll()
    {
        Preferences.Default.Remove(BalanceKey);
        Preferences.Default.Remove(WinsKey);
        Preferences.Default.Remove(LossesKey);
        Preferences.Default.Remove(PushesKey);
        Preferences.Default.Remove(NetProfitKey);
        Preferences.Default.Remove(BiggestWinKey);
        Preferences.Default.Remove(BiggestLossKey);
        Preferences.Default.Remove(CheckInStreakDayKey);
    }
}
