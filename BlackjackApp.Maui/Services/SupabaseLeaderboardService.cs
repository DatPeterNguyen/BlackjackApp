using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BlackjackApp.core.Services;

namespace BlackjackApp.Maui.Services;

/// <summary>
/// The online leaderboard, backed by Supabase over its PostgREST HTTP API.
///
/// Plain HttpClient and System.Text.Json rather than the Supabase SDK: the
/// two calls this needs are a GET and an upsert POST, which is far less
/// surface than a client library, and it keeps the app free of another
/// dependency to keep current across four target platforms.
///
/// Every failure path ends in "no online board" rather than an exception.
/// Losing the network, a project that is down, a schema that was never
/// created - all of it degrades to the local board, because the game is
/// fully playable offline and an online ranking is not worth costing anyone
/// a round over.
/// </summary>
public sealed class SupabaseLeaderboardService : ILeaderboardService
{
    /// <summary>
    /// Kept for the app's lifetime. A new HttpClient per call is the classic
    /// way to exhaust sockets under repeated use.
    /// </summary>
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    /// <summary>Every column is pinned by an explicit [JsonPropertyName] on Row below, so this needs no naming policy of its own.</summary>
    private static readonly JsonSerializerOptions Json = new();

    public bool IsConfigured => LeaderboardConfig.IsConfigured && HasUsableProjectUrl;

    /// <summary>
    /// Whether ProjectUrl is actually a usable absolute http(s) URL.
    ///
    /// Checked because a plausible typo - pasting "abcdefgh.supabase.co"
    /// without the scheme - builds a relative request URI, and HttpClient
    /// throws InvalidOperationException on that BEFORE any of this class's
    /// catch blocks are reached. Treating it as unconfigured keeps the
    /// promise that a bad setup degrades to the local board instead of
    /// crashing the page.
    /// </summary>
    private static bool HasUsableProjectUrl =>
        Uri.TryCreate(LeaderboardConfig.ProjectUrl, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    /// <summary>The wire shape of one row, matching the column names in Docs/leaderboard-schema.sql.</summary>
    private sealed class Row
    {
        [JsonPropertyName("player_id")]
        public string PlayerId { get; set; } = string.Empty;

        [JsonPropertyName("player_name")]
        public string PlayerName { get; set; } = string.Empty;

        [JsonPropertyName("best_balance")]
        public decimal BestBalance { get; set; }

        [JsonPropertyName("achieved_at")]
        public DateTime AchievedAt { get; set; }
    }

    public async Task<bool> SubmitBestBalanceAsync(
        Guid playerId,
        string playerName,
        decimal bestBalance,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return false;
        }

        var row = new Row
        {
            PlayerId = playerId.ToString(),
            PlayerName = playerName,
            BestBalance = bestBalance,
            AchievedAt = DateTime.UtcNow,
        };

        // on_conflict + merge-duplicates makes this an upsert: the player's
        // first submission creates their row, every later one updates it in
        // place. A submission that is somehow LOWER than what is already
        // stored is not a problem to guard against here - the table's trigger
        // keeps the higher value, so the score can never go backwards even if
        // a stale client sends an old number.
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{Root}/{LeaderboardConfig.TableName}?on_conflict=player_id")
        {
            Content = new StringContent(
                JsonSerializer.Serialize(row, Json), Encoding.UTF8, "application/json"),
        };

        AddAuth(request);
        request.Headers.TryAddWithoutValidation("Prefer", "resolution=merge-duplicates,return=minimal");

        using var response = await SendAsync(request, cancellationToken);

        return response is { IsSuccessStatusCode: true };
    }

    public async Task<IReadOnlyList<LeaderboardStanding>> GetTopAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (!IsConfigured)
        {
            return Array.Empty<LeaderboardStanding>();
        }

        var url = $"{Root}/{LeaderboardConfig.TableName}"
            + "?select=player_id,player_name,best_balance,achieved_at"
            + "&order=best_balance.desc,achieved_at.asc"
            + $"&limit={Math.Clamp(limit, 1, 100)}";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        AddAuth(request);

        using var response = await SendAsync(request, cancellationToken);

        if (response is not { IsSuccessStatusCode: true })
        {
            return Array.Empty<LeaderboardStanding>();
        }

        try
        {
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            var rows = JsonSerializer.Deserialize<List<Row>>(body, Json) ?? [];

            var standings = new List<LeaderboardStanding>(rows.Count);

            foreach (var item in rows)
            {
                // A row whose id will not parse is somebody else's bad data,
                // not a reason to show the player an empty board - skip it.
                if (Guid.TryParse(item.PlayerId, out var id))
                {
                    standings.Add(new LeaderboardStanding(
                        id,
                        item.PlayerName,
                        item.BestBalance,
                        // ToUniversalTime, not SpecifyKind. PostgREST writes
                        // timestamptz with a numeric offset, which the parser
                        // turns into a Local DateTime already converted - and
                        // relabelling that as Utc without converting would let
                        // the page's ToLocalTime apply the offset a second
                        // time, dating records a day out. ToUniversalTime is
                        // correct whichever Kind the parser produced.
                        item.AchievedAt.ToUniversalTime()));
                }
            }

            return standings;
        }
        catch (JsonException)
        {
            // The project answered with something that is not the shape we
            // expect - most likely the schema was never applied.
            return Array.Empty<LeaderboardStanding>();
        }
        catch (HttpRequestException)
        {
            // The connection dropped partway through the body. SendAsync only
            // guards the headers, so this has to be caught here too or it
            // escapes into a fire-and-forget caller as an unobserved fault.
            return Array.Empty<LeaderboardStanding>();
        }
        catch (OperationCanceledException)
        {
            // The page closed while the body was still streaming. Expected,
            // not an error - and the caller discards this task, so letting it
            // throw would fault a task nobody awaits.
            return Array.Empty<LeaderboardStanding>();
        }
    }

    private static string Root => $"{LeaderboardConfig.ProjectUrl.TrimEnd('/')}/rest/v1";

    /// <summary>
    /// PostgREST wants the anon key twice: once as the API key and once as a
    /// bearer token, which is what the table's policies are evaluated against.
    /// </summary>
    private static void AddAuth(HttpRequestMessage request)
    {
        request.Headers.TryAddWithoutValidation("apikey", LeaderboardConfig.AnonKey);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", LeaderboardConfig.AnonKey);
    }

    /// <summary>
    /// Sends a request, turning every transport-level failure into null.
    /// Timeouts surface as TaskCanceledException even when nobody cancelled,
    /// which is why that is caught here alongside HttpRequestException.
    /// </summary>
    private static async Task<HttpResponseMessage?> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        try
        {
            return await Http.SendAsync(request, cancellationToken);
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (TaskCanceledException)
        {
            return null;
        }
    }
}
