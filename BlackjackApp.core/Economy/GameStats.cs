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
/// win/loss/push counts, plus profit tracking (lifetime net, biggest single
/// round win, biggest single round loss). Deliberately separate from
/// ChipWallet's Balance: the balance is "what you have right now", this is
/// "how you've done over time" - the two move together during play, but
/// Reset() clears both independently (see MainPage's Reset Progress button,
/// which resets a ChipWallet and a GameStats together for a true fresh
/// start).
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

    public int HandsPlayed => Wins + Losses + Pushes;

    public GameStats()
    {
    }

    /// <summary>Rehydrates a previously-persisted stat line (see GameProgressStorage in the MAUI project) - the loading path, not for normal in-game use.</summary>
    public GameStats(int wins, int losses, int pushes, decimal netProfit, decimal biggestWin, decimal biggestLoss)
    {
        Wins = wins;
        Losses = losses;
        Pushes = pushes;
        NetProfit = netProfit;
        BiggestWin = biggestWin;
        BiggestLoss = biggestLoss;
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
    /// in one number. Call once per round (not once per hand).
    /// </summary>
    public void RecordRoundNet(decimal net)
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
    }

    /// <summary>Wipes every stat back to zero.</summary>
    public void Reset()
    {
        Wins = 0;
        Losses = 0;
        Pushes = 0;
        NetProfit = 0m;
        BiggestWin = 0m;
        BiggestLoss = 0m;
    }
}
