using Microsoft.Extensions.Logging;

namespace BlackjackApp.Maui;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
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

		return builder.Build();
	}
}
