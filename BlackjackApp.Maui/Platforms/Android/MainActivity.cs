using Android.App;
using Android.Content.PM;
using Android.OS;

namespace BlackjackApp.Maui;

// No orientation lock: the table has a portrait form as well as a
// landscape one, and on a phone portrait is the better of the two - it
// has roughly twice the height to spend, which is what lets the type be
// a readable size. See MainPage.xaml.cs ApplyAdaptiveMetrics.
[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
}
