using BlackjackApp.Maui.Services;
using Microsoft.Extensions.Logging;
using Plugin.MauiMtAdmob;

namespace BlackjackApp.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
#if !NO_ADS
			.UseMauiMTAdmob()
#endif
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");

				// The display face for buttons, headings and money/score
				// readouts - see Resources/Styles/Casino.xaml. It's the same
				// font the CraftPix kit's own button art is lettered in (its
				// readme.txt names it), so our labels match the text baked into
				// btn_hit/btn_stand/btn_double/btn_split rather than merely
				// sitting near it. Open Sans above stays the body face: Bebas
				// is all-caps and far too tight for the rules paragraphs.
				fonts.AddFont("BebasNeue-Regular.ttf", "BebasNeue");
			});

#if DEBUG
		builder.Logging.AddDebug();
#endif

		// The one ad surface in the app: a rewarded video the player can
		// choose to watch for chips once they have run out - see
		// BlackjackApp.core.Services.ChipRescue for when that is offered.
		//
		// Assigned here rather than injected because the app has no DI
		// container (see AppServices). If anything about the SDK is not
		// working, AdMobRewardedAdService simply reports that no ad is
		// ready, the offer is never made, and the game plays exactly as it
		// did before ads existed.
		//
		// NO_ADS (build with -p:Ads=false - see the csproj) leaves the SDK
		// untouched entirely, to test whether it's what freezes the table on
		// iOS.
#if NO_ADS
		AppServices.Ads = new NoOpAdService();
#else
		AppServices.Ads = new AdMobRewardedAdService();
#endif

		// The online leaderboard. Inert until LeaderboardConfig has a project
		// URL and anon key in it, at which point this starts serving the real
		// board - see LeaderboardConfig for where those come from.
		AppServices.Leaderboard = new SupabaseLeaderboardService();

		return builder.Build();
	}
}
