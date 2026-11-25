using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Burudani.Platforms.Android.Services;
using Burudani.Services;

namespace Burudani;

[Activity(Theme = "@style/Maui.SplashTheme", MainLauncher = true, LaunchMode = LaunchMode.SingleTop, ConfigurationChanges = ConfigChanges.ScreenSize | ConfigChanges.Orientation | ConfigChanges.UiMode | ConfigChanges.ScreenLayout | ConfigChanges.SmallestScreenSize | ConfigChanges.Density)]
public class MainActivity : MauiAppCompatActivity
{
    protected override void OnCreate(Bundle? savedInstanceState)
    {
        // Immediately load persisted theme so splash screen background and system bars match the active theme
        try
        {
            var savedTheme = Preferences.Default.Get("ActiveTheme", "");
            if (string.IsNullOrEmpty(savedTheme))
            {
                var settings = Burudani.Services.StorageService.LoadSettings();
                savedTheme = settings.Theme;
            }

            bool isLight = savedTheme.StartsWith("Light", StringComparison.OrdinalIgnoreCase);

            var splashBg = isLight ? Android.Graphics.Color.White : Android.Graphics.Color.ParseColor("#18181b");
#pragma warning disable CA1422
            Window?.SetStatusBarColor(splashBg);
            Window?.SetNavigationBarColor(splashBg);
#pragma warning restore CA1422
            if (Window?.DecorView != null)
            {
                if (isLight)
                {
                    Window.DecorView.SetBackgroundColor(Android.Graphics.Color.White);
                }
                var controller = AndroidX.Core.View.WindowCompat.GetInsetsController(Window, Window.DecorView);
                if (controller != null)
                {
                    controller.AppearanceLightStatusBars = isLight;
                    controller.AppearanceLightNavigationBars = isLight;
                }
            }
        }
        catch { }

        base.OnCreate(savedInstanceState);

        RequestPermissionsIfNeeded();

        var currentSettings = Burudani.Services.StorageService.LoadSettings();
        ThemeService.ApplyTheme(currentSettings.Theme);
    }

    private void RequestPermissionsIfNeeded()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.Tiramisu)
        {
            var perms = new List<string>();
            if (CheckSelfPermission("android.permission.POST_NOTIFICATIONS") != Permission.Granted)
                perms.Add("android.permission.POST_NOTIFICATIONS");
            if (CheckSelfPermission("android.permission.READ_MEDIA_AUDIO") != Permission.Granted)
                perms.Add("android.permission.READ_MEDIA_AUDIO");

            if (perms.Count > 0)
                RequestPermissions(perms.ToArray(), 1001);
        }
        else if (Build.VERSION.SdkInt >= BuildVersionCodes.M)
        {
            if (CheckSelfPermission(Android.Manifest.Permission.ReadExternalStorage) != Permission.Granted)
            {
                RequestPermissions(new[] { Android.Manifest.Permission.ReadExternalStorage, Android.Manifest.Permission.WriteExternalStorage }, 1002);
            }
        }
    }

    private void StartMediaService()
    {
        try
        {
            var serviceIntent = new Intent(this, typeof(BurudaniMediaService));
            if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
                StartForegroundService(serviceIntent);
            else
                StartService(serviceIntent);
        }
        catch { }
    }
}
