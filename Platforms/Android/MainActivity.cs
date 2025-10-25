using Android.App;
using Android.Content.PM;
using Android.OS;
using AndroidX.Core.View;
using Microsoft.Maui.Platform;

namespace OrbanaDrive
{
    [Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true,
          ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode |
                                 ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
    public class MainActivity : MauiAppCompatActivity
    {
        protected override void OnCreate(Bundle? savedInstanceState)
        {
            base.OnCreate(savedInstanceState);

            var window = Window!;
            WindowCompat.SetDecorFitsSystemWindows(window, false);
            window.SetStatusBarColor(Android.Graphics.Color.Transparent);
            window.SetNavigationBarColor(Android.Graphics.Color.Transparent);

            var controller = new WindowInsetsControllerCompat(window, window.DecorView);
            controller.AppearanceLightStatusBars = false;
            controller.AppearanceLightNavigationBars = false;

            // colorea el fondo atrás de las barras con el background de la app
            var bg = Android.Graphics.Color.ParseColor("#0B0F14");
            window.DecorView.SetBackgroundColor(bg);

        }
    }
}
