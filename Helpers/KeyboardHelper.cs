// KeyboardHelper.cs — Utility for dismissing the on-screen soft keyboard reliably
namespace Burudani.Helpers;

public static class KeyboardHelper
{
    public static void HideKeyboard()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
#if ANDROID
            try
            {
                var activity = Microsoft.Maui.ApplicationModel.Platform.CurrentActivity;
                if (activity != null)
                {
                    var imm = (Android.Views.InputMethods.InputMethodManager?)
                        activity.GetSystemService(Android.Content.Context.InputMethodService);
                    var view = activity.CurrentFocus ?? activity.Window?.DecorView;
                    if (view?.WindowToken != null && imm != null)
                    {
                        imm.HideSoftInputFromWindow(view.WindowToken, Android.Views.InputMethods.HideSoftInputFlags.None);
                    }
                }
            }
            catch { }
#elif WINDOWS
            try
            {
                var inputPane = Windows.UI.ViewManagement.InputPane.GetForCurrentView();
                inputPane?.TryHide();
            }
            catch { }
#endif
        });
    }
}
