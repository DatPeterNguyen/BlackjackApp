using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using BlackjackApp.Maui.Services;
using BlackjackApp.core.Economy;
using Microsoft.Maui.Storage;

namespace BlackjackApp.Maui;

/// <summary>
/// Auto-saves the player's balance and lifetime stats (item 13 on the
/// polish list), their daily check-in streak (item 14), and - since a
/// player can close the app mid-hand - a full snapshot of whatever round
/// was still in progress (see InProgressRoundState), all via
/// Microsoft.Maui.Storage.Preferences - simple durable key-value storage
/// that survives app restarts.
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
    private const string WeeklyNetProfitKey = "progress_weekly_net_profit";
    private const string WeekAnchorKey = "progress_week_anchor";
    private const string MonthlyNetProfitKey = "progress_monthly_net_profit";
    private const string MonthAnchorKey = "progress_month_anchor";
    private const string YearToDateNetProfitKey = "progress_ytd_net_profit";
    private const string YearAnchorKey = "progress_year_anchor";
    private const string LastCheckInKey = "progress_last_check_in";
    private const string LastCheckInUtcKey = "progress_last_check_in_utc";
    private const string CheckInStreakDayKey = "progress_check_in_streak_day";
    private const string LastChipRescueKey = "progress_last_chip_rescue";
    private const string ChipRescuesOnThatDayKey = "progress_chip_rescues_on_that_day";
    private const string OnlinePlayerIdKey = "progress_online_player_id";
    private const string OnlinePlayerNameKey = "progress_online_player_name";
    private const string InProgressRoundKey = "progress_in_progress_round_json";
    private const string LeaderboardKey = "progress_leaderboard_json";

    private const string DateFormat = "yyyy-MM-dd";

    /// <summary>Starting balance for a brand new player, or after Reset Progress.</summary>
    public const decimal DefaultStartingBalance = 1000m;

    /// <summary>
    /// The best balance this device has recorded locally, or zero if none yet.
    ///
    /// Exists so a submission that failed can be retried later. Publishing is
    /// only triggered by a NEW personal best, and the local record is written
    /// first - so if the network was down at that moment, every later save
    /// takes the "not a new record" early return and the online row stays
    /// stale until the player happens to beat their own best again. Handing
    /// the stored figure back lets the leaderboard page re-publish it on open.
    /// </summary>
    public static decimal LoadLocalBestBalance() =>
        LoadLeaderboard().FirstOrDefault(entry => entry.PlayerName == LocalPlayerName)?.HighestBalance ?? 0m;

    /// <summary>The one local player's display name on the leaderboard (see LeaderboardEntry) until real accounts exist - see BlackjackApp.core.Services.ILeaderboardService.</summary>
    public const string LocalPlayerName = "You";

    /// <summary>How long a name may be on the online board - matched by a CHECK constraint on the table, so anything longer would be rejected by the server anyway.</summary>
    public const int MaxOnlinePlayerNameLength = 24;

    /// <summary>
    /// This install's id on the online leaderboard, generated once and kept
    /// forever after.
    ///
    /// Deliberately NOT the same thing as LocalPlayerName above. The local
    /// board identifies its single row by the literal name "You", and the
    /// online board identifies players by this id - which is what lets the
    /// online name be freely editable and non-unique without the local board
    /// losing track of its own row. Keeping them apart means adding the
    /// online board changed nothing about how the local one works.
    ///
    /// A random GUID with no device identifiers in it: enough to recognise a
    /// returning player, not enough to identify a person. It is reset by
    /// nothing, including Reset Progress - see ResetAll - because losing it
    /// would orphan the player's existing row and let them re-enter the board
    /// from scratch.
    /// </summary>
    public static Guid LoadOnlinePlayerId()
    {
        var stored = Preferences.Default.Get(OnlinePlayerIdKey, string.Empty);

        if (Guid.TryParse(stored, out var existing))
        {
            return existing;
        }

        var created = Guid.NewGuid();
        Preferences.Default.Set(OnlinePlayerIdKey, created.ToString());
        return created;
    }

    /// <summary>
    /// The name shown beside this player on the online board. Defaults to
    /// "Player" plus a short slice of their id, so two people who never set a
    /// name are still told apart on the board.
    /// </summary>
    public static string LoadOnlinePlayerName()
    {
        var stored = Preferences.Default.Get(OnlinePlayerNameKey, string.Empty);

        return string.IsNullOrWhiteSpace(stored)
            ? $"Player {LoadOnlinePlayerId().ToString()[..4]}"
            : stored;
    }

    /// <summary>Stores the player's chosen online name, trimmed and clamped to what the table accepts. Blank clears it back to the generated default.</summary>
    public static void SaveOnlinePlayerName(string name)
    {
        var trimmed = (name ?? string.Empty).Trim();

        if (trimmed.Length > MaxOnlinePlayerNameLength)
        {
            trimmed = trimmed[..MaxOnlinePlayerNameLength];
        }

        Preferences.Default.Set(OnlinePlayerNameKey, trimmed);
    }

    public static decimal LoadBalance() => (decimal)Preferences.Default.Get(BalanceKey, (double)DefaultStartingBalance);

    public static GameStats LoadStats() => new(
        wins: Preferences.Default.Get(WinsKey, 0),
        losses: Preferences.Default.Get(LossesKey, 0),
        pushes: Preferences.Default.Get(PushesKey, 0),
        netProfit: (decimal)Preferences.Default.Get(NetProfitKey, 0.0),
        biggestWin: (decimal)Preferences.Default.Get(BiggestWinKey, 0.0),
        biggestLoss: (decimal)Preferences.Default.Get(BiggestLossKey, 0.0),
        weeklyNetProfit: (decimal)Preferences.Default.Get(WeeklyNetProfitKey, 0.0),
        weekAnchor: LoadDateOrDefault(WeekAnchorKey),
        monthlyNetProfit: (decimal)Preferences.Default.Get(MonthlyNetProfitKey, 0.0),
        monthAnchor: LoadDateOrDefault(MonthAnchorKey),
        yearToDateNetProfit: (decimal)Preferences.Default.Get(YearToDateNetProfitKey, 0.0),
        yearAnchor: LoadDateOrDefault(YearAnchorKey));

    /// <summary>Reads back a DateOnly stored the same way LoadLastCheckIn reads LastCheckInKey - default(DateOnly) (0001-01-01) if it was never saved, which GameStats treats as "no period recorded yet" and rolls over on the very next round.</summary>
    private static DateOnly LoadDateOrDefault(string key)
    {
        var stored = Preferences.Default.Get(key, string.Empty);

        return DateOnly.TryParseExact(stored, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : default;
    }

    /// <summary>
    /// The UTC instant of the player's most recent check-in claim, or null if
    /// they have never claimed one. This is what drives the 24-hour gate - see
    /// BlackjackApp.core.Services.DailyCheckIn.
    ///
    /// Falls back to the older calendar-date key when the timestamp is absent,
    /// reading that date as midnight UTC. That is the migration path off the
    /// previous calendar-day scheme: without it every existing player would
    /// look like they had never claimed and be handed an immediate free
    /// reward. Midnight is the earliest the old date could have meant, so the
    /// conversion errs toward unlocking sooner rather than locking someone out
    /// on a technicality - a claim recorded yesterday lands 24-48 hours back,
    /// which is exactly the window that keeps a streak alive.
    /// </summary>
    public static DateTime? LoadLastCheckInUtc()
    {
        var stored = Preferences.Default.Get(LastCheckInUtcKey, string.Empty);

        if (DateTime.TryParse(stored, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var timestamp))
        {
            return timestamp.ToUniversalTime();
        }

        var legacy = Preferences.Default.Get(LastCheckInKey, string.Empty);

        return DateOnly.TryParseExact(legacy, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc)
            : null;
    }

    /// <summary>Which day of the 7-day cycle the last claim was. 0 for a player who has never claimed.</summary>
    public static int LoadCheckInStreakDay() => Preferences.Default.Get(CheckInStreakDayKey, 0);

    /// <summary>The calendar date the chip-rescue counter below belongs to, or null if the player has never traded an ad for chips. Stored and read exactly like LastCheckInKey above.</summary>
    public static DateOnly? LoadLastChipRescue()
    {
        var stored = Preferences.Default.Get(LastChipRescueKey, string.Empty);

        return DateOnly.TryParseExact(stored, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
            ? date
            : null;
    }

    /// <summary>How many rewarded videos were traded for chips on LoadLastChipRescue's date. Only meaningful together with that date - ChipRescue is what decides whether it still counts against today.</summary>
    public static int LoadChipRescuesOnThatDay() => Preferences.Default.Get(ChipRescuesOnThatDayKey, 0);

    /// <summary>Persists the running count of chip rescues, so the daily cap survives the app being closed. Takes the pair straight from ChipRescue.RecordWatch rather than incrementing here, so the rollover rule lives in one place.</summary>
    public static void SaveChipRescue(DateOnly watchedOn, int countThatDay)
    {
        Preferences.Default.Set(LastChipRescueKey, watchedOn.ToString(DateFormat, CultureInfo.InvariantCulture));
        Preferences.Default.Set(ChipRescuesOnThatDayKey, countThatDay);
    }

    /// <summary>
    /// Persists a freshly claimed check-in so the next one knows when the
    /// 24-hour clock started and where the streak stands.
    ///
    /// Round-trip ("O") format, parsed back with RoundtripKind, so the stored
    /// instant survives a timezone or DST change unchanged - the whole point
    /// of keeping this in UTC.
    ///
    /// The legacy calendar-date key is written too. It costs nothing and means
    /// a build rolled back to the previous version still sees the claim rather
    /// than offering it again.
    /// </summary>
    public static void SaveCheckIn(DateTime claimedAtUtc, int streakDay)
    {
        var utc = claimedAtUtc.ToUniversalTime();

        Preferences.Default.Set(LastCheckInUtcKey, utc.ToString("O", CultureInfo.InvariantCulture));
        Preferences.Default.Set(LastCheckInKey, DateOnly.FromDateTime(utc).ToString(DateFormat, CultureInfo.InvariantCulture));
        Preferences.Default.Set(CheckInStreakDayKey, streakDay);
    }

    /// <summary>Persists the balance on its own - used by the check-in reward, which pays out from the menu with no live game or stats to save alongside it. Also the one place every balance change eventually flows through, which is why it's what checks for a new leaderboard record rather than scattering that check across every caller.</summary>
    public static void SaveBalance(decimal balance)
    {
        Preferences.Default.Set(BalanceKey, (double)balance);
        RecordBalanceForLeaderboard(balance);
    }

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
        Preferences.Default.Set(WeeklyNetProfitKey, (double)stats.WeeklyNetProfit);
        Preferences.Default.Set(WeekAnchorKey, stats.WeekAnchor.ToString(DateFormat, CultureInfo.InvariantCulture));
        Preferences.Default.Set(MonthlyNetProfitKey, (double)stats.MonthlyNetProfit);
        Preferences.Default.Set(MonthAnchorKey, stats.MonthAnchor.ToString(DateFormat, CultureInfo.InvariantCulture));
        Preferences.Default.Set(YearToDateNetProfitKey, (double)stats.YearToDateNetProfit);
        Preferences.Default.Set(YearAnchorKey, stats.YearAnchor.ToString(DateFormat, CultureInfo.InvariantCulture));
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
        Preferences.Default.Remove(WeeklyNetProfitKey);
        Preferences.Default.Remove(WeekAnchorKey);
        Preferences.Default.Remove(MonthlyNetProfitKey);
        Preferences.Default.Remove(MonthAnchorKey);
        Preferences.Default.Remove(YearToDateNetProfitKey);
        Preferences.Default.Remove(YearAnchorKey);
        Preferences.Default.Remove(CheckInStreakDayKey);
        ClearInProgressRound();

        // Deliberately NOT cleared: LastCheckInKey, LastChipRescueKey and
        // ChipRescuesOnThatDayKey. Each is a "once per day" gate, and wiping
        // them here would turn Reset Progress into a way to farm today's
        // check-in and today's ad rescues over and over. The streak day above
        // is cleared because that is progress the player is choosing to give
        // up, not a gate. Resetting already hands back the full starting
        // stake, so there is nothing to soften.
        //
        // The same applies to LastCheckInUtcKey, which is the key that
        // actually drives the 24-hour gate now.
        //
        // OnlinePlayerIdKey and OnlinePlayerNameKey are kept for a different
        // reason: they are identity, not progress. Clearing them would strand
        // the player's existing row on the online board under an id nothing
        // can reach any more, and let one person occupy the board repeatedly.
    }

    /// <summary>True while a round is saved as still in progress - drives whether GameMenuPage's start menu shows the Load Game button.</summary>
    public static bool HasInProgressRound() => !string.IsNullOrEmpty(Preferences.Default.Get(InProgressRoundKey, string.Empty));

    /// <summary>Snapshots an in-progress round - see MainPage.PersistInProgressRound, called after every action that changes round state while a round is active.</summary>
    public static void SaveInProgressRound(InProgressRoundState state) =>
        Preferences.Default.Set(InProgressRoundKey, JsonSerializer.Serialize(state));

    /// <summary>
    /// Reads back the saved in-progress round, or null if there isn't one
    /// (including if the saved JSON is somehow corrupt/from an incompatible
    /// older shape - treated as "no save" rather than crashing the start
    /// menu on launch, and the bad entry is cleared so HasInProgressRound
    /// stops reporting it).
    /// </summary>
    public static InProgressRoundState? LoadInProgressRound()
    {
        var json = Preferences.Default.Get(InProgressRoundKey, string.Empty);

        if (string.IsNullOrEmpty(json))
        {
            return null;
        }

        try
        {
            return JsonSerializer.Deserialize<InProgressRoundState>(json);
        }
        catch (JsonException)
        {
            ClearInProgressRound();
            return null;
        }
    }

    /// <summary>Called once a round is no longer "in progress" - either it finished normally (see MainPage.EndRound) or the player abandoned it (GameMenuPage's Exit to Menu).</summary>
    public static void ClearInProgressRound() => Preferences.Default.Remove(InProgressRoundKey);

    /// <summary>
    /// Every leaderboard entry (see LeaderboardEntry and Views/
    /// LeaderboardPage), highest balance first. Empty - never a crash - if
    /// nothing's been recorded yet, or the saved JSON is somehow corrupt or
    /// from an incompatible older shape (the bad entry is cleared so it
    /// doesn't keep failing to parse on every future read).
    /// </summary>
    public static List<LeaderboardEntry> LoadLeaderboard()
    {
        var json = Preferences.Default.Get(LeaderboardKey, string.Empty);

        if (string.IsNullOrEmpty(json))
        {
            return new List<LeaderboardEntry>();
        }

        try
        {
            var entries = JsonSerializer.Deserialize<List<LeaderboardEntry>>(json) ?? new List<LeaderboardEntry>();
            return entries.OrderByDescending(entry => entry.HighestBalance).ToList();
        }
        catch (JsonException)
        {
            Preferences.Default.Remove(LeaderboardKey);
            return new List<LeaderboardEntry>();
        }
    }

    /// <summary>
    /// Records balance as the local player's new leaderboard entry if - and
    /// only if - it beats whatever they already have on the board (or they
    /// don't have an entry yet). Called from SaveBalance, so it's safe to
    /// leave wired in everywhere balance already gets saved; anything short
    /// of a new record is simply a no-op. Deliberately NOT reset by Reset
    /// Progress - a high score earned before a reset stays a high score,
    /// the same as an arcade machine's table survives a new game starting.
    /// </summary>
    public static void RecordBalanceForLeaderboard(decimal balance)
    {
        var entries = LoadLeaderboard();
        var existing = entries.FirstOrDefault(entry => entry.PlayerName == LocalPlayerName);

        if (existing is not null && existing.HighestBalance >= balance)
        {
            return;
        }

        entries.RemoveAll(entry => entry.PlayerName == LocalPlayerName);
        entries.Add(new LeaderboardEntry
        {
            PlayerName = LocalPlayerName,
            HighestBalance = balance,
            AchievedAtUtc = DateTime.UtcNow,
        });

        Preferences.Default.Set(LeaderboardKey, JsonSerializer.Serialize(entries));

        PublishBestBalance(balance);
    }

    /// <summary>
    /// Pushes a brand new personal best to the online board, if one is
    /// configured. Only ever reached from the line above, which is already
    /// past every "this is not a new record" early return - so this fires on
    /// genuine records only, not on every balance change.
    ///
    /// Deliberately fire-and-forget. Recording a local record is a
    /// synchronous operation on the hot path at the end of a round, and it
    /// must not wait on a network call, fail because of one, or care whether
    /// the player is even online - the local board is the source of truth and
    /// the online one is a copy that catches up on the next record.
    /// </summary>
    private static void PublishBestBalance(decimal balance)
    {
        if (!AppServices.Leaderboard.IsConfigured)
        {
            return;
        }

        var playerId = LoadOnlinePlayerId();
        var playerName = LoadOnlinePlayerName();

        _ = Task.Run(async () =>
        {
            try
            {
                await AppServices.Leaderboard.SubmitBestBalanceAsync(playerId, playerName, balance);
            }
            catch
            {
                // The service already swallows its own transport failures;
                // this is the backstop for anything it did not, because an
                // exception escaping a fire-and-forget task takes the app
                // down with it.
            }
        });
    }
}
