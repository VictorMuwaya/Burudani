using Microsoft.Extensions.Logging;
using CommunityToolkit.Maui;
using Microsoft.Maui.LifecycleEvents;

namespace Burudani;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.UseMauiCommunityToolkitMediaElement(false)
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
				fonts.AddFont("MaterialDesignIcons.ttf", "MaterialIcons");
				fonts.AddFont("FontAwesome.ttf", "FontAwesome");
			});

#if WINDOWS
		builder.ConfigureLifecycleEvents(events =>
		{
			events.AddWindows(windows =>
			{
				windows.OnWindowCreated(window =>
				{
					window.ExtendsContentIntoTitleBar = true;
					var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(window);
					if (hWnd != IntPtr.Zero)
					{
						var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
						var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
						if (appWindow?.TitleBar != null)
						{
							var tb = appWindow.TitleBar;
							tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
							tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
							tb.ButtonForegroundColor = Microsoft.UI.Colors.White;
							tb.ButtonInactiveForegroundColor = Microsoft.UI.Colors.DarkGray;
							tb.ButtonHoverBackgroundColor = global::Windows.UI.Color.FromArgb(40, 255, 255, 255);
							tb.ButtonPressedBackgroundColor = global::Windows.UI.Color.FromArgb(60, 255, 255, 255);
						}
					}
				});
			});
		});
#endif

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}

	static MauiProgram()
	{
		// 1. Remove generic underline and focus tint from Entry on Android & Windows
		Microsoft.Maui.Handlers.EntryHandler.Mapper.AppendToMapping("CleanEntry", (handler, view) =>
		{
#if ANDROID
			try
			{
				handler.PlatformView.Background = null;
				handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
				handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
			}
			catch { }
#elif WINDOWS
			try
			{
				handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
			}
			catch { }
#endif
		});

		// 2. Remove underline from Editor
		Microsoft.Maui.Handlers.EditorHandler.Mapper.AppendToMapping("CleanEditor", (handler, view) =>
		{
#if ANDROID
			try
			{
				handler.PlatformView.Background = null;
				handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
				handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
			}
			catch { }
#elif WINDOWS
			try
			{
				handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
			}
			catch { }
#endif
		});

		// 3. Remove underline from SearchBar
		Microsoft.Maui.Handlers.SearchBarHandler.Mapper.AppendToMapping("CleanSearchBar", (handler, view) =>
		{
#if ANDROID
			try
			{
				handler.PlatformView.Background = null;
				handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
				handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
			}
			catch { }
#elif WINDOWS
			try
			{
				handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
			}
			catch { }
#endif
		});

		// 4. Remove default underline from Picker
		Microsoft.Maui.Handlers.PickerHandler.Mapper.AppendToMapping("CleanPicker", (handler, view) =>
		{
#if ANDROID
			try
			{
				handler.PlatformView.Background = null;
				handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
				handler.PlatformView.BackgroundTintList = Android.Content.Res.ColorStateList.ValueOf(Android.Graphics.Color.Transparent);
			}
			catch { }
#elif WINDOWS
			try
			{
				handler.PlatformView.BorderThickness = new Microsoft.UI.Xaml.Thickness(0);
			}
			catch { }
#endif
		});

		// 5. WebView transparency
		Microsoft.Maui.Handlers.WebViewHandler.Mapper.AppendToMapping("TransparentWebView", (handler, view) =>
		{
#if ANDROID
			try
			{
				handler.PlatformView.SetBackgroundColor(Android.Graphics.Color.Transparent);
				handler.PlatformView.Settings.JavaScriptEnabled = true;
				handler.PlatformView.Settings.AllowFileAccess = true;
				handler.PlatformView.Settings.DomStorageEnabled = true;
			}
			catch { }
#elif WINDOWS
			try
			{
				handler.PlatformView.DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
				handler.PlatformView.CoreWebView2Initialized += (s, e) =>
				{
					try
					{
						s.DefaultBackgroundColor = Windows.UI.Color.FromArgb(0, 0, 0, 0);
					}
					catch { }
				};
			}
			catch { }
#endif
		});
	}
}
