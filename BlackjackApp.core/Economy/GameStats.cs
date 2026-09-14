namespace BlackjackApp.core.Economy;

/// <summary>Classification of a single resolved blackjack hand, for the player's win/loss/push record.</summary>
public enum HandResult
{
    Win,
    Loss,
    Push,
}

/// <summary>
/// The player's lifetime record across every hand and round ever played -
/// win/loss/push counts, profit tracking (lifetime net, biggest single
/// round win, biggest single round loss), and net profit broken out by
/// week/month/year-to-date on top of the lifetime figure. Deliberately
/// separate from ChipWallet's Balance: the balance is "what you have right
/// now", this is "how you've done over time" - the two move together
/// during play, but Reset() clears both independently (see MainPage's
/// Reset Progress button, which resets a ChipWallet and a GameStats
/// together for a true fresh start).
///
/// The period figures (weekly/monthly/YTD) are pure and date-driven, same
/// as BlackjackApp.core.Services.DailyCheckIn - RecordRoundNet takes
/// "today" as an explicit argument rather than reading the system clock
/// itself, so rollover behaviour is fully unit-testable. Each period
/// tracks its own "anchor" (the start-of-week/month/year date its current
/// total covers); when RecordRoundNet is called with a date past that
/// anchor, the period's total resets to zero before the new round's net
/// is added, rather than accumulating across the boundary.
/// </summary>
public class GameStats
{
    public int Wins { get; private set; }
    public int Losses { get; private set; }
    public int Pushes { get; private set; }

    /// <summary>Lifetime net win/loss across every round ever played - can go negative.</summary>
    public decimal NetProfit { get; private set; }

    /// <summary>The single biggest round win ever recorded (0 if none yet).</summary>
    public decimal BiggestWin { get; private set; }

    /// <summary>The single biggest round loss ever recorded, as a positive magnitude (0 if none yet).</summary>
    public decimal BiggestLoss { get; private set; }

    /// <summary>Net win/loss so far in the current calendar week (Monday-Sunday). Resets to 0 the first time RecordRoundNet is called with a date in a new week.</summary>
    public decimal WeeklyNetProfit { get; private set; }

    /// <summary>Net win/loss so far in the current calendar month. Resets the same way as WeeklyNetProfit, on the month boundary.</summary>
    public decimal MonthlyNetProfit { get; private set; }

    /// <summary>Net win/loss so far in the current calendar year (year-to-date). Resets the same way as WeeklyNetProfit, on the year boundary.</summary>
    public decimal YearToDateNetProfit { get; private set; }

    /// <summary>The Monday that starts the week WeeklyNetProfit currently covers. DateOnly.MinValue until the first round is ever recorded, which guarantees the very first RecordRoundNet call always establishes a real anchor instead of silently accumulating against a meaningless default.</summary>
    public DateOnly WeekAnchor { get; private set; }

    /// <summary>The first-of-month date that MonthlyNetProfit currently covers. See WeekAnchor.</summary>
    public DateOnly MonthAnchor { get; private set; }

    /// <summary>The January 1st that YearToDateNetProfit currently covers. See WeekAnchor.</summary>
    public DateOnly YearAnchor { get; private set; }

    public int HandsPlayed => Wins + Losses + Pushes;

    public GameStats()
    {
    }

    /// <summary>Rehydrates a previously-persisted stat line (see GameProgressStorage in the MAUI project) - the loading path, not for normal in-game use.</summary>
    public GameStats(
        int wins,
        int losses,
        int pushes,
        decimal netProfit,
        decimal biggestWin,
        decimal biggestLoss,
        decimal weeklyNetProfit = 0m,
        DateOnly weekAnchor = default,
        decimal monthlyNetProfit = 0m,
        DateOnly monthAnchor = default,
        decimal yearToDateNetProfit = 0m,
        DateOnly yearAnchor = default)
    {
        Wins = wins;
        Losses = losses;
        Pushes = pushes;
        NetProfit = netProfit;
        BiggestWin = biggestWin;
        BiggestLoss = biggestLoss;
        WeeklyNetProfit = weeklyNetProfit;
        WeekAnchor = weekAnchor;
        MonthlyNetProfit = monthlyNetProfit;
        MonthAnchor = monthAnchor;
        YearToDateNetProfit = yearToDateNetProfit;
        YearAnchor = yearAnchor;
    }

    /// <summary>Records one resolved hand's outcome toward the win/loss/push counts - call once per hand, separately from RecordRoundNet.</summary>
    public void RecordHand(HandResult result)
    {
        switch (result)
        {
            case HandResult.Win:
                Wins++;
                break;
            case HandResult.Loss:
                Losses++;
                break;
            case HandResult.Push:
                Pushes++;
                break;
        }
    }

    /// <summary>
    /// Records one round's overall net wallet movement - every hand's
    /// payout plus any War side bet, however many hands were in play, all
    /// in one number. Call once per round (not once per hand). Rolls the
    /// weekly/monthly/YTD figures over onto a fresh zero total whenever
    /// today has moved past the period each one currently covers, then
    /// folds this round's net into the lifetime total and all three period
    /// totals together.
    /// </summary>
    public void RecordRoundNet(decimal net, DateOnly today)
    {
        NetProfit += net;

        if (net > BiggestWin)
        {
            BiggestWin = net;
        }

        if (-net > BiggestLoss)
        {
            BiggestLoss = -net;
        }

        var weekStart = StartOfWeek(today);
        if (weekStart != WeekAnchor)
        {
            WeekAnchor = weekStart;
            WeeklyNetProfit = 0m;
        }

        WeeklyNetProfit += net;

        var monthStart = new DateOnly(today.Year, today.Month, 1);
        if (monthStart != MonthAnchor)
        {
            MonthAnchor = monthStart;
            MonthlyNetProfit = 0m;
        }

        MonthlyNetProfit += net;

        var yearStart = new DateOnly(today.Year, 1, 1);
        if (yearStart != YearAnchor)
        {
            YearAnchor = yearStart;
            YearToDateNetProfit = 0m;
        }

        YearToDateNetProfit += net;
    }

    /// <summary>The Monday on or before the given date - the ISO-style start of that calendar week.</summary>
    private static DateOnly StartOfWeek(DateOnly date)
    {
        var daysSinceMonday = ((int)date.DayOfWeek - (int)DayOfWeek.Monday + 7) % 7;
        return date.AddDays(-daysSinceMonday);
    }

    /// <summary>Wipes every stat back to zero, including the weekly/monthly/YTD figures and their anchors.</summary>
    public void Reset()
    {
        Wins = 0;
        Losses = 0;
        Pushes = 0;
        NetProfit = 0m;
        BiggestWin = 0m;
        BiggestLoss = 0m;
        WeeklyNetProfit = 0m;
        WeekAnchor = default;
        MonthlyNetProfit = 0m;
        MonthAnchor = default;
        YearToDateNetProfit = 0m;
        YearAnchor = default;
    }
}
