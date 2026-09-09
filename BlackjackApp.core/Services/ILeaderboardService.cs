namespace BlackjackApp.core.Services;

// Compares chip totals / P&L against friends. Stub locally for now,
// swap for a real backend-backed implementation later.
public interface ILeaderboardService
{
    void SubmitScore(string playerName, decimal balance);
}
