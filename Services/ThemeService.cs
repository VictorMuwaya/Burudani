// ThemeService.cs — Manages application themes dynamically for Windows and Android
namespace Burudani.Services;

public static class ThemeService
{
    public static event Action<AppTheme>? ThemeChanged;

    public static void ApplyTheme(string theme)
    {
        var appTheme = theme switch
        {
            "Light" or "Light Mode" => AppTheme.Light,
            "Dark" or "Dark Mode" => AppTheme.Dark,
            _ => AppTheme.Unspecified
        };

        if (Application.Current != null)
        {
            Application.Current.UserAppTheme = appTheme;
        }

        try
        {
            Preferences.Default.Set("ActiveTheme", theme);
            var settings = StorageService.LoadSettings();
            settings.Theme = theme.StartsWith("Light") ? "Light" : theme.StartsWith("System") ? "System" : "Dark";
            StorageService.SaveSettings(settings);
        }
        catch { }

        UpdatePlatformStatusBar(appTheme);
        ThemeChanged?.Invoke(appTheme);
    }

    public static void UpdatePlatformStatusBar(AppTheme theme)
    {
#if ANDROID
        try
        {
            var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
            if (activity?.Window != null)
            {
                bool isLight = theme == AppTheme.Light ||
                    (theme == AppTheme.Unspecified && Application.Current?.RequestedTheme == AppTheme.Light);

                var barColor = isLight ? Android.Graphics.Color.White : Android.Graphics.Color.ParseColor("#09090b");
#pragma warning disable CA1422
                activity.Window.SetStatusBarColor(barColor);
                activity.Window.SetNavigationBarColor(barColor);
#pragma warning restore CA1422

                var insetsController = AndroidX.Core.View.WindowCompat.GetInsetsController(activity.Window, activity.Window.DecorView);
                if (insetsController != null)
                {
                    insetsController.AppearanceLightStatusBars = isLight;
                    insetsController.AppearanceLightNavigationBars = isLight;
                }
            }
        }
        catch { }
#endif
    }
}
