using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BlackjackApp.core.Services;

/// <summary>
/// One player's place on the online board, as returned by
/// <see cref="ILeaderboardService.GetTopAsync"/>.
/// </summary>
/// <param name="PlayerId">
/// The stable per-install id the score was submitted under. This is what
/// identifies a player, NOT the name - names are freely editable and not
/// unique, so the UI uses this to work out which row is the local player's.
/// </param>
/// <param name="PlayerName">Whatever the player chose to be called. Display only.</param>
/// <param name="BestBalance">The highest balance they have ever reached.</param>
/// <param name="AchievedAtUtc">When they reached it, in UTC.</param>
public readonly record struct LeaderboardStanding(
    Guid PlayerId,
    string PlayerName,
    decimal BestBalance,
    DateTime AchievedAtUtc);

/// <summary>
/// The online leaderboard: every player's highest balance, ranked.
///
/// Async and cancellable throughout, because every implementation worth
/// having crosses a network - the previous shape of this interface was a
/// single synchronous void SubmitScore, which no real backend could honour.
///
/// Both methods are explicitly allowed to fail quietly. An online board is a
/// nicety on top of a game that is fully playable offline, so losing the
/// network must never cost the player a round or a local record: submissions
/// return false and reads return an empty list rather than throwing.
///
/// Lives in core with no HTTP or MAUI types anywhere in it, so the callers
/// stay testable against a fake - BlackjackApp.Maui supplies the real
/// Supabase-backed implementation.
/// </summary>
public interface ILeaderboardService
{
    /// <summary>
    /// Whether a backend is actually configured. False means the app has no
    /// online board at all and should show the local one instead - not that
    /// something went wrong.
    /// </summary>
    bool IsConfigured { get; }

    /// <summary>
    /// Publishes this player's best balance, creating their row or updating
    /// it in place. Safe to call with a score lower than one already
    /// submitted; the backend keeps the higher of the two.
    /// </summary>
    /// <returns>True if the score reached the backend. False on any failure, including not being configured.</returns>
    Task<bool> SubmitBestBalanceAsync(
        Guid playerId,
        string playerName,
        decimal bestBalance,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// The top <paramref name="limit"/> players by best balance, highest
    /// first.
    /// </summary>
    /// <returns>The standings, or an empty list if unconfigured or unreachable.</returns>
    Task<IReadOnlyList<LeaderboardStanding>> GetTopAsync(
        int limit,
        CancellationToken cancellationToken = default);
}
