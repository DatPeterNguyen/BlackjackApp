using BlackjackApp.core.Services;

namespace BlackjackApp.Maui.Services;

/// <summary>
/// Where the app's cross-cutting services live.
///
/// Plain static properties rather than dependency injection because this app
/// has no container - pages are constructed directly with `new MainPage(...)`
/// from GameMenuPage, and GameProgressStorage and DailyCheckIn are static
/// too. Threading services through those constructors purely for these two
/// would make them the odd ones out.
///
/// Both default to the inert implementation, so anything that runs without
/// MauiProgram having set them up - a unit test, a design-time preview, a
/// build with no backend configured - gets "no ads" and "no online board"
/// rather than a null reference. MauiProgram.CreateMauiApp assigns the real
/// ones once, at startup.
/// </summary>
internal static class AppServices
{
    /// <summary>The rewarded video the player can trade for chips - see BlackjackApp.core.Services.ChipRescue.</summary>
    public static IAdService Ads { get; set; } = new NoOpAdService();

    /// <summary>The online leaderboard. Inert until LeaderboardConfig is filled in.</summary>
    public static ILeaderboardService Leaderboard { get; set; } = new OfflineLeaderboardService();
}
