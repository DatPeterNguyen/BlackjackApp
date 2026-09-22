using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BlackjackApp.core.Services;

namespace BlackjackApp.Maui.Services;

/// <summary>
/// The leaderboard service used when no backend is configured: there is no
/// online board, and it says so rather than pretending to be one.
///
/// Mirrors NoOpAdService. It is what makes an unconfigured build behave
/// exactly like the app did before the online board existed - the page falls
/// back to the local list, and nothing anywhere has to null-check a service.
/// </summary>
public sealed class OfflineLeaderboardService : ILeaderboardService
{
    public bool IsConfigured => false;

    public Task<bool> SubmitBestBalanceAsync(
        Guid playerId,
        string playerName,
        decimal bestBalance,
        CancellationToken cancellationToken = default) => Task.FromResult(false);

    public Task<IReadOnlyList<LeaderboardStanding>> GetTopAsync(
        int limit,
        CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<LeaderboardStanding>>(Array.Empty<LeaderboardStanding>());
}
