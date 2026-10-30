using Android.App;
using Android.Content.PM;
using Android.OS;

namespace SecureMesh.App;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        base.OnCreate(savedInstanceState);

        // Android 15 (API 35) enforces edge-to-edge for apps targeting SDK 35+.
        // Opt in explicitly so the app draws under the system bars; the Shell
        // navigation chrome and per-page Padding provide the visual safe-area
        // insets for the current screens.
        if (Build.VERSION.SdkInt >= BuildVersionCodes.VanillaIceCream)
        {
            Window?.SetDecorFitsSystemWindows(false);
        }
    }
}
