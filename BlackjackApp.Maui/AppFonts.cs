namespace BlackjackApp.Maui;

/// <summary>
/// The two typefaces the app uses, by the aliases they're registered under
/// in MauiProgram.CreateMauiApp.
///
/// XAML gets at the display face through Resources/Styles/Casino.xaml's
/// "DisplayFont" string resource instead of this class; these constants are
/// for the views that are built in code rather than markup - the hand slots
/// (MainPage.CreateHandSlotView), the check-in day strip
/// (CheckInPage.BuildDayStrip) and the leaderboard rows
/// (LeaderboardPage.CreateRow). Both spellings have to name the same font,
/// which is the whole reason this isn't a bare string literal repeated in
/// three files.
/// </summary>
internal static class AppFonts
{
    /// <summary>
    /// Bebas Neue - the face the CraftPix kit's own button art is lettered
    /// in. All-caps and single-weight, so it's for buttons, headings and
    /// money/score readouts only, and never asks for bold.
    /// </summary>
    public const string Display = "BebasNeue";

    /// <summary>Open Sans - anything sentence-length.</summary>
    public const string Body = "OpenSansRegular";
}
