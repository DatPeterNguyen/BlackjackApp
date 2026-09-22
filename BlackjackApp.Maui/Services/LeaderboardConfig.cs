namespace BlackjackApp.Maui.Services;

/// <summary>
/// Where the online leaderboard's backend lives.
///
/// ====================================================================
///  FILL THESE IN to turn the online board on. Both come from the
///  Supabase dashboard, under Project Settings -> API:
///
///    ProjectUrl -> "Project URL"      e.g. https://abcdefgh.supabase.co
///    AnonKey    -> "anon public" key  (the long one labelled "public")
///
///  Leave them blank and the app simply shows the local board instead -
///  nothing errors, nothing hangs. See SupabaseLeaderboardService.
///
///  Run Docs/leaderboard-schema.sql against the project first, or the
///  table these point at will not exist.
/// ====================================================================
///
/// The anon key is meant to be public and ships inside the app; that is what
/// it is for. It is NOT a secret, and anyone can extract it from a built app,
/// which is exactly why the table's policies have to assume that - see the
/// schema file for what they do and do not protect. Never put the service
/// role key here: that one bypasses every policy.
/// </summary>
internal static class LeaderboardConfig
{
    public const string ProjectUrl = "";

    public const string AnonKey = "";

    /// <summary>Whether both values above have been filled in.</summary>
    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(ProjectUrl) && !string.IsNullOrWhiteSpace(AnonKey);

    /// <summary>The table the standings live in, as named by the schema file.</summary>
    public const string TableName = "leaderboard";
}
