using Android.App;
using Android.Content.PM;
using Android.OS;

namespace BlackjackApp.Maui;

// SensorLandscape rather than plain Landscape so the table still flips
// between the two landscape orientations to follow the device, it just
// never rotates into portrait - the table is laid out as one wide
// composition (see MainPage.xaml) and has no portrait form.
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ScreenOrientation = ScreenOrientation.SensorLandscape, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
}
