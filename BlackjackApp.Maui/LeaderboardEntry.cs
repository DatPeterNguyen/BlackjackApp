using System;

namespace BlackjackApp.Maui;

/// <summary>
/// One row on the local leaderboard (see Views/LeaderboardPage) - a
/// player's highest wallet balance ever reached, and when they reached it.
/// Only one entry exists today (PlayerName is always
/// GameProgressStorage.LocalPlayerName, since there's only one local
/// player right now), but keeping a PlayerName on the record from the
/// start - rather than bolting it on later - means
/// BlackjackApp.core.Services.ILeaderboardService (an intentional stub for
/// a future real, account-backed leaderboard - see its own doc comment)
/// can eventually submit other players' scores onto this same list
/// without reshaping it. Deliberately plain data only, so it round-trips
/// through System.Text.Json with no custom converter (see
/// GameProgressStorage.LoadLeaderboard/RecordBalanceForLeaderboard).
/// </summary>
public sealed class LeaderboardEntry
{
    public required string PlayerName { get; init; }
    public required decimal HighestBalance { get; init; }
    public required DateTime AchievedAtUtc { get; init; }
}
