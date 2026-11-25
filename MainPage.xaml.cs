// MainPage.xaml.cs — Unified application container with Windows TitleBar integration
using Burudani.Services;
using Burudani.Models;

namespace Burudani;

public partial class MainPage : ContentPage
{
    private static readonly Color ActiveBg = Color.FromArgb("#ea580c");
    private static readonly Color InactiveBg = Color.FromArgb("#18181b");
    private static readonly Color InactiveBorder = Color.FromArgb("#27272a");
    private static readonly Color ActiveText = Colors.White;
    private static readonly Color InactiveText = Color.FromArgb("#94a3b8");

    // ── Android Bottom Sheet State ────────────────────────────────────────────
    private enum SheetState { Hidden, Collapsed, HalfExpanded, FullExpanded }
    private SheetState _sheetState = SheetState.Hidden;
    private double _sheetDragStartHeight = 0;
    private double _screenHeight = 0;
    private const double CollapsedHeight = 72.0;   // mini-bar height in dp
    private bool _isSheetSeeking = false;
    private double _sheetSeekWidth = 0;

    public MainPage()
    {
        InitializeComponent();

        // Set binding context of the Android bottom sheet to PlayerService.Instance
        AndroidPlayerSheet.BindingContext = PlayerService.Instance;

        GlobalMiniPlayer.NavigateToPlayerRequested += () =>
        {
#if WINDOWS
            SwitchPage(3);
#else
            ExpandSheet(SheetState.FullExpanded, animate: true);
#endif
        };

        // Listen for queue changes to update the queue badge count
        DownloadService.Instance.QueueStatusChanged += (s, e) => MainThread.BeginInvokeOnMainThread(UpdateQueueBadge);
        UpdateQueueBadge();

        // Listen to player service: show/hide Android bottom sheet when track is loaded
        PlayerService.Instance.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PlayerService.CurrentTrack))
            {
                MainThread.BeginInvokeOnMainThread(() =>
                {
#if WINDOWS
                    GlobalMiniPlayer.IsVisible = !PlayerView.IsVisible && PlayerService.Instance.CurrentTrack != null;
#else
                    bool hasTrack = PlayerService.Instance.CurrentTrack != null;
                    if (hasTrack && _sheetState == SheetState.Hidden)
                        ExpandSheet(SheetState.Collapsed, animate: true);
                    else if (!hasTrack)
                        HideSheet(animate: true);
#endif
                });
            }
            else if (e.PropertyName == nameof(PlayerService.PositionSeconds) && !_isSheetSeeking)
            {
                MainThread.BeginInvokeOnMainThread(UpdateSheetSeekFill);
            }
        };

        // Listen to custom dialog service
        CustomDialogService.StateChanged += OnCustomDialogStateChanged;

        SizeChanged += OnMainPageSizeChanged;
#if WINDOWS
        Loaded += OnMainPageLoadedWindows;
#endif
    }

    private void OnMainPageSizeChanged(object? sender, EventArgs e)
    {
        _screenHeight = Height;
    }

    // ── Sheet Snap Logic ─────────────────────────────────────────────────────

    private void ExpandSheet(SheetState target, bool animate)
    {
        if (AndroidPlayerSheet == null) return;

        double targetH = target switch
        {
            SheetState.Collapsed => CollapsedHeight,
            SheetState.HalfExpanded => Math.Max(CollapsedHeight, _screenHeight * 0.55),
            SheetState.FullExpanded => Math.Max(CollapsedHeight, _screenHeight),
            _ => 0
        };

        _sheetState = target;
        AndroidPlayerSheet.IsVisible = target != SheetState.Hidden;

        if (!AndroidPlayerSheet.IsVisible) return;

        // When expanded (Half or Full), the bottom tabs bar must disappear!
        bool isCollapsed = target == SheetState.Collapsed;
        bool isHalfExpanded = target == SheetState.HalfExpanded;
        bool isFullExpanded = target == SheetState.FullExpanded;
        bool isExpanded = isHalfExpanded || isFullExpanded;

        if (BottomNavBar != null)
        {
            BottomNavBar.IsVisible = isCollapsed;
        }

        if (GlobalFloatingPlaylistButton != null)
        {
            GlobalFloatingPlaylistButton.IsVisible = isCollapsed;
            if (isCollapsed)
            {
                GlobalFloatingPlaylistButton.Opacity = 1;
            }
        }

        AndroidPlayerSheet.Margin = isExpanded
            ? new Thickness(0)
            : new Thickness(0, 0, 0, 56);

        // Mutually exclusive UI states to eliminate repeating elements:
        // Collapsed: only SheetMiniContent is visible
        // HalfExpanded (Queue view - Images 1 & 2): SheetMiniContent docked at top; dome artwork & transport are hidden
        // FullExpanded (Full Player - Image 3): SheetMiniContent is hidden; dome artwork, title, seekbar, and transport controls are visible
        if (SheetMiniContent != null)
        {
            SheetMiniContent.IsVisible = !isFullExpanded;
        }

        if (SheetExpandedContent != null)
        {
            SheetExpandedContent.IsVisible = isExpanded;
        }

        if (FullPlayerTopBar != null)
        {
            FullPlayerTopBar.IsVisible = isFullExpanded;
        }

        if (SheetLargeArtwork != null)
        {
            SheetLargeArtwork.IsVisible = isFullExpanded;
        }

        if (SheetFullTrackInfo != null)
        {
            SheetFullTrackInfo.IsVisible = isFullExpanded;
        }

        if (SheetSeekContainer != null)
        {
            SheetSeekContainer.IsVisible = isFullExpanded;
        }

        if (SheetTransportControls != null)
        {
            SheetTransportControls.IsVisible = isFullExpanded;
        }

        AndroidPlayerSheet.HeightRequest = targetH;

        if (animate)
        {
            SheetExpandedContent?.FadeToAsync(isExpanded ? 1 : 0, 180);
        }
        else
        {
            if (SheetExpandedContent != null) SheetExpandedContent.Opacity = isExpanded ? 1 : 0;
        }
    }

    private void HideSheet(bool animate)
    {
        _sheetState = SheetState.Hidden;
        if (BottomNavBar != null)
        {
            BottomNavBar.IsVisible = true;
        }

        if (GlobalFloatingPlaylistButton != null)
        {
            GlobalFloatingPlaylistButton.IsVisible = true;
            GlobalFloatingPlaylistButton.Opacity = 1;
        }

        if (animate)
        {
            AndroidPlayerSheet?.FadeToAsync(0, 180).ContinueWith(_ =>
                MainThread.BeginInvokeOnMainThread(() =>
                {
                    if (AndroidPlayerSheet != null)
                        AndroidPlayerSheet.IsVisible = false;
                }));
        }
        else if (AndroidPlayerSheet != null)
        {
            AndroidPlayerSheet.IsVisible = false;
        }
    }

    private SheetState SnapStateFromHeight(double h)
    {
        double halfH = _screenHeight * 0.55;
        if (h < CollapsedHeight * 1.3) return SheetState.Collapsed;
        if (h < halfH * 1.25) return SheetState.HalfExpanded;
        return SheetState.FullExpanded;
    }

    // ── Sheet Pan Gesture ────────────────────────────────────────────────────

    private void OnAndroidSheetPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (AndroidPlayerSheet == null) return;
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _sheetDragStartHeight = AndroidPlayerSheet.HeightRequest > 0
                    ? AndroidPlayerSheet.HeightRequest
                    : CollapsedHeight;
                break;
            case GestureStatus.Running:
                double newH = Math.Clamp(_sheetDragStartHeight - e.TotalY, CollapsedHeight, _screenHeight);
                AndroidPlayerSheet.HeightRequest = newH;

                // Toggling bottom tabs bar during drag
                if (BottomNavBar != null && newH > CollapsedHeight + 20)
                {
                    BottomNavBar.IsVisible = false;
                    AndroidPlayerSheet.Margin = new Thickness(0);
                }

                if (SheetExpandedContent != null && !SheetExpandedContent.IsVisible)
                {
                    SheetExpandedContent.IsVisible = true;
                }

                // Live opacity of expanded content
                double expandFraction = Math.Clamp((newH - CollapsedHeight) / ((_screenHeight * 0.55) - CollapsedHeight), 0, 1);
                double fullFraction = Math.Clamp((newH - _screenHeight * 0.55) / (_screenHeight * 0.45), 0, 1);
                if (SheetExpandedContent != null) SheetExpandedContent.Opacity = expandFraction;
                if (SheetLargeArtwork != null) SheetLargeArtwork.Opacity = fullFraction;
                if (SheetFullTrackInfo != null) SheetFullTrackInfo.Opacity = fullFraction;
                if (SheetTransportControls != null) SheetTransportControls.Opacity = fullFraction;
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                var snap = SnapStateFromHeight(AndroidPlayerSheet.HeightRequest);
                if (snap == SheetState.Collapsed && e.TotalY > 80)
                    snap = SheetState.Hidden; // drag down hard to dismiss
                if (snap == SheetState.Hidden)
                    HideSheet(animate: true);
                else
                    ExpandSheet(snap, animate: true);
                break;
        }
    }

    private void OnAndroidSheetHandleTapped(object? sender, TappedEventArgs e)
    {
        // Tap mini bar: expand directly into the full player
        if (_sheetState == SheetState.Collapsed)
            ExpandSheet(SheetState.FullExpanded, animate: true);
        else
            ExpandSheet(SheetState.Collapsed, animate: true);
    }

    private void OnSheetCollapseClicked(object? sender, EventArgs e)
    {
        ExpandSheet(SheetState.Collapsed, animate: true);
    }

    private async void OnSheetMenuClicked(object? sender, EventArgs e)
    {
        var track = PlayerService.Instance.CurrentTrack;
        if (track == null) return;

        var options = new List<string> { "Add to Playlist", "Download Track", "View Album", "Share" };
        var choice = await CustomDialogService.ShowOptionsAsync("Track Options", options, o => o);
        if (choice == "Download Track")
        {
            DownloadService.Instance.Enqueue(track);
        }
        else if (choice == "Add to Playlist")
        {
            OnSheetSaveClicked(sender, e);
        }
    }

    private async void OnSheetSaveClicked(object? sender, EventArgs e)
    {
        var track = PlayerService.Instance.CurrentTrack;
        if (track == null) return;

        var playlists = StorageService.GetPlaylists().Keys.ToList();
        if (playlists.Count == 0)
        {
            playlists.AddRange(StorageService.PresetPlaylists);
        }

        var selected = await CustomDialogService.ShowOptionsAsync("Save to Playlist", playlists, p => p);
        if (!string.IsNullOrEmpty(selected))
        {
            StorageService.AddTrackToPlaylist(selected, track);
        }
    }

    private async void OnQueueTrackTapped(object? sender, TappedEventArgs e)
    {
        if (sender is BindableObject bo && bo.BindingContext is TrackItem track)
        {
            await PlayerService.Instance.PlayTrackAsync(track, PlayerService.Instance.Playlist);
        }
    }

    // ── Sheet Seek Bar ───────────────────────────────────────────────────────

    private void UpdateSheetSeekFill()
    {
        double dur = PlayerService.Instance.DurationSeconds;
        if (dur <= 0) return;
        double fraction = Math.Clamp(PlayerService.Instance.PositionSeconds / dur, 0, 1);

        if (SheetSeekTrack != null && SheetSeekFill != null && SheetSeekTrack.Width > 0)
        {
            SheetSeekFill.WidthRequest = fraction * SheetSeekTrack.Width;
        }

        if (SheetMiniProgressFill != null)
        {
            double miniWidth = SheetMiniProgressTrack?.Width > 0 ? SheetMiniProgressTrack.Width : SheetMiniContent?.Width ?? 0;
            if (miniWidth > 0)
            {
                SheetMiniProgressFill.WidthRequest = fraction * miniWidth;
            }
        }
    }

    private async void OnSheetSeekTapped(object? sender, TappedEventArgs e)
    {
        if (sender is not Border track || track.Width <= 0) return;
        track.HeightRequest = 7;
        var pos = e.GetPosition(track);
        if (pos.HasValue)
        {
            double fraction = Math.Clamp(pos.Value.X / track.Width, 0, 1);
            SheetSeekFill.WidthRequest = fraction * track.Width;
            PlayerService.Instance.Seek(TimeSpan.FromSeconds(fraction * PlayerService.Instance.DurationSeconds));
        }
        await Task.Delay(200);
        track.HeightRequest = 4;
    }

    private void OnSheetSeekPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (sender is not Border track) return;
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _isSheetSeeking = true;
                _sheetSeekWidth = track.Width;
                track.HeightRequest = 7; // Tactile expansion on touch
                break;
            case GestureStatus.Running:
                if (_sheetSeekWidth > 0 && SheetSeekFill != null)
                {
                    double newW = Math.Clamp(SheetSeekFill.WidthRequest + e.TotalX, 0, _sheetSeekWidth);
                    SheetSeekFill.WidthRequest = newW;
                }
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _isSheetSeeking = false;
                track.HeightRequest = 4; // Reset to sleek line
                if (_sheetSeekWidth > 0 && SheetSeekFill != null)
                {
                    double fraction = Math.Clamp(SheetSeekFill.WidthRequest / _sheetSeekWidth, 0, 1);
                    PlayerService.Instance.Seek(TimeSpan.FromSeconds(fraction * PlayerService.Instance.DurationSeconds));
                }
                break;
        }
    }

    // ── Quick Playlists Access ───────────────────────────────────────────────

    private async void OnQuickPlaylistsTapped(object? sender, TappedEventArgs e)
    {
        if (sender is VisualElement fab)
        {
            await fab.ScaleToAsync(0.88, 70, Easing.CubicOut);
            await fab.ScaleToAsync(1.0, 90, Easing.CubicIn);
        }

        var playlists = StorageService.GetPlaylists().Keys.ToList();
        if (playlists.Count == 0)
        {
            playlists.AddRange(StorageService.PresetPlaylists);
        }

        var actions = new List<string> { "➕ Create New Playlist" };
        actions.AddRange(playlists);

        var picked = await CustomDialogService.ShowOptionsAsync("Playlists", actions, p => p);
        if (string.IsNullOrEmpty(picked)) return;

        if (picked == "➕ Create New Playlist")
        {
            var newName = await CustomDialogService.ShowPromptAsync("New Playlist", "Enter playlist name:", "", "Create", "Cancel");
            if (!string.IsNullOrWhiteSpace(newName))
            {
                StorageService.CreatePlaylist(newName.Trim());
            }
        }
        else
        {
            var all = StorageService.GetPlaylists();
            if (all.TryGetValue(picked, out var tracks) && tracks.Count > 0)
            {
                var opt = await CustomDialogService.ShowOptionsAsync(
                    $"{picked} ({tracks.Count} tracks)",
                    new List<string> { "▶ Play All", "🔀 Shuffle Play", "Cancel" },
                    o => o);

                if (opt == "▶ Play All")
                {
                    await PlayerService.Instance.PlayTrackAsync(tracks[0], tracks);
                }
                else if (opt == "🔀 Shuffle Play")
                {
                    PlayerService.Instance.IsShuffle = true;
                    var randIdx = Random.Shared.Next(0, tracks.Count);
                    await PlayerService.Instance.PlayTrackAsync(tracks[randIdx], tracks);
                }
            }
            else
            {
                await CustomDialogService.ShowAlertAsync(picked, "This playlist has no songs yet. Tap '+' on any playing song to add it here.");
            }
        }
    }

    // ── Sheet Transport Controls ─────────────────────────────────────────────

    private void OnSheetPlayPauseClicked(object? sender, EventArgs e) => PlayerService.Instance.TogglePlayPause();
    private async void OnSheetPrevClicked(object? sender, EventArgs e) => await PlayerService.Instance.PreviousAsync();
    private async void OnSheetNextClicked(object? sender, EventArgs e) => await PlayerService.Instance.NextAsync();
    private void OnSheetShuffleClicked(object? sender, EventArgs e) => PlayerService.Instance.ToggleShuffle();
    private void OnSheetRepeatClicked(object? sender, EventArgs e) => PlayerService.Instance.CycleRepeat();

    private async void OnMiniSwipeLeft(object? sender, SwipedEventArgs e) => await PlayerService.Instance.NextAsync();
    private async void OnMiniSwipeRight(object? sender, SwipedEventArgs e) => await PlayerService.Instance.PreviousAsync();

    private void OnCapsuleAudioTapped(object? sender, EventArgs e)
    {
        if (CapsuleAudioBorder != null && CapsuleVideoBorder != null)
        {
            CapsuleAudioBorder.BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Light ? Colors.White : Color.FromArgb("#353f47");
            CapsuleAudioLabel.TextColor = Application.Current?.RequestedTheme == AppTheme.Light ? Colors.Black : Colors.White;
            CapsuleVideoBorder.BackgroundColor = Colors.Transparent;
            CapsuleVideoLabel.TextColor = Color.FromArgb("#9ca3af");
        }
    }

    private void OnCapsuleVideoTapped(object? sender, EventArgs e)
    {
        if (CapsuleAudioBorder != null && CapsuleVideoBorder != null)
        {
            CapsuleVideoBorder.BackgroundColor = Application.Current?.RequestedTheme == AppTheme.Light ? Colors.White : Color.FromArgb("#353f47");
            CapsuleVideoLabel.TextColor = Application.Current?.RequestedTheme == AppTheme.Light ? Colors.Black : Colors.White;
            CapsuleAudioBorder.BackgroundColor = Colors.Transparent;
            CapsuleAudioLabel.TextColor = Color.FromArgb("#9ca3af");
        }
    }

#if WINDOWS
    private void OnMainPageLoadedWindows(object? sender, EventArgs e)
    {
        try
        {
            var window = App.Current?.Windows.FirstOrDefault();
            if (window?.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow)
            {
                nativeWindow.ExtendsContentIntoTitleBar = true;

                if (BrandTitleBarArea.Handler?.PlatformView is Microsoft.UI.Xaml.UIElement brandElement)
                {
                    nativeWindow.SetTitleBar(brandElement);
                }

                var hWnd = WinRT.Interop.WindowNative.GetWindowHandle(nativeWindow);
                var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(hWnd);
                var appWindow = Microsoft.UI.Windowing.AppWindow.GetFromWindowId(windowId);
                if (appWindow?.TitleBar != null)
                {
                    var tb = appWindow.TitleBar;
                    tb.ExtendsContentIntoTitleBar = true;
                    tb.ButtonBackgroundColor = Microsoft.UI.Colors.Transparent;
                    tb.ButtonInactiveBackgroundColor = Microsoft.UI.Colors.Transparent;
                    tb.ButtonForegroundColor = Microsoft.UI.Colors.White;
                    tb.ButtonInactiveForegroundColor = Microsoft.UI.Colors.DarkGray;
                    tb.ButtonHoverBackgroundColor = global::Windows.UI.Color.FromArgb(40, 255, 255, 255);
                    tb.ButtonPressedBackgroundColor = global::Windows.UI.Color.FromArgb(60, 255, 255, 255);
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[TitleBar Setup Error] {ex.Message}");
        }
    }
#endif

    private void UpdateQueueBadge()
    {
        int count = DownloadService.Instance.Queue.Count;
        QueueBadgeLabel.Text = $"Queue: {count} {(count == 1 ? "track" : "tracks")}";
    }

    public void SwitchPage(int index)
    {
        // 0 = Discovery, 1 = Search, 2 = Downloads, 3 = Player, 4 = Settings
        DiscoveryView.IsVisible = index == 0;
        SearchView.IsVisible = index == 1;
        DownloadsView.IsVisible = index == 2;
        PlayerView.IsVisible = index == 3;
        SettingsView.IsVisible = index == 4;

#if WINDOWS
        // Hide mini-player on PlayerView (index 3), show on other pages if track exists
        GlobalMiniPlayer.IsVisible = index != 3 && PlayerService.Instance.CurrentTrack != null;
#else
        // On Android, the bottom sheet is always visible when a track is loaded (regardless of page)
        // Keep bottom sheet visible; it's layered above pages
#endif

        UpdateNavPill(NavDiscoveryBorder, NavDiscoveryIcon, NavDiscoveryLabel, index == 0);
        UpdateNavPill(NavSearchBorder, NavSearchIcon, NavSearchLabel, index == 1);
        UpdateNavPill(NavDownloadsBorder, NavDownloadsIcon, NavDownloadsLabel, index == 2);
        UpdateNavPill(NavPlayerBorder, NavPlayerIcon, NavPlayerLabel, index == 3);
        UpdateNavPill(NavSettingsBorder, NavSettingsIcon, NavSettingsLabel, index == 4);

        UpdateBottomTab(BottomNavDiscoveryIcon, BottomNavDiscoveryLabel, index == 0);
        UpdateBottomTab(BottomNavSearchIcon, BottomNavSearchLabel, index == 1);
        UpdateBottomTab(BottomNavDownloadsIcon, BottomNavDownloadsLabel, index == 2);
    }

    private static void UpdateNavPill(Border border, Label icon, Label label, bool isActive)
    {
        var isLight = Application.Current?.RequestedTheme == AppTheme.Light;
        border.BackgroundColor = isActive ? ActiveBg : (isLight ? Color.FromArgb("#ffffff") : InactiveBg);
        border.Stroke = isActive ? ActiveBg : (isLight ? Color.FromArgb("#e2e8f0") : InactiveBorder);
        icon.TextColor = isActive ? ActiveText : (isLight ? Color.FromArgb("#64748b") : InactiveText);
        label.TextColor = isActive ? ActiveText : (isLight ? Color.FromArgb("#64748b") : InactiveText);
    }

    private static void UpdateBottomTab(Label icon, Label label, bool isActive)
    {
        var activeColor = Color.FromArgb("#ea580c");
        var isLight = Application.Current?.RequestedTheme == AppTheme.Light;
        var inactiveColor = isLight ? Color.FromArgb("#64748b") : Color.FromArgb("#94a3b8");

        icon.TextColor = isActive ? activeColor : inactiveColor;
        label.TextColor = isActive ? activeColor : inactiveColor;
    }

    private void OnNavDiscoveryTapped(object? sender, TappedEventArgs e) => SwitchPage(0);
    private void OnNavSearchTapped(object? sender, TappedEventArgs e) => SwitchPage(1);
    private void OnNavDownloadsTapped(object? sender, TappedEventArgs e) => SwitchPage(2);
    private void OnNavPlayerTapped(object? sender, TappedEventArgs e) => SwitchPage(3);
    private void OnNavSettingsTapped(object? sender, TappedEventArgs e) => SwitchPage(4);

    // ── Custom In-App Dialog Handlers ─────────────────────────────────────────

    private void OnCustomDialogStateChanged()
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            if (!CustomDialogService.IsVisible)
            {
                DialogOverlay.IsVisible = false;
                return;
            }

            DialogTitleLabel.Text = CustomDialogService.Title;
            DialogMessageLabel.Text = CustomDialogService.Message;
            DialogMessageLabel.IsVisible = !string.IsNullOrEmpty(CustomDialogService.Message);

            // 1. Prompt mode
            DialogInputBorder.IsVisible = CustomDialogService.IsPrompt;
            if (CustomDialogService.IsPrompt)
            {
                DialogInputEntry.Text = CustomDialogService.InputText;
                DialogInputEntry.Focus();
            }

            // 2. Options mode
            DialogOptionsScroll.IsVisible = CustomDialogService.IsOptions;
            if (CustomDialogService.IsOptions)
            {
                DialogOptionsList.Children.Clear();
                var isLight = Application.Current?.RequestedTheme == AppTheme.Light;

                foreach (var opt in CustomDialogService.Options)
                {
                    var optBorder = new Border
                    {
                        BackgroundColor = opt.IsSelected
                            ? Color.FromArgb("#ea580c")
                            : (isLight ? Color.FromArgb("#f1f5f9") : Color.FromArgb("#1c1c22")),
                        Stroke = opt.IsSelected
                            ? Color.FromArgb("#ea580c")
                            : (isLight ? Color.FromArgb("#e2e8f0") : Color.FromArgb("#27272a")),
                        StrokeThickness = 1,
                        Padding = new Thickness(14, 10),
                        StrokeShape = new Microsoft.Maui.Controls.Shapes.RoundRectangle { CornerRadius = new CornerRadius(10) }
                    };

                    var grid = new Grid
                    {
                        ColumnDefinitions = [new ColumnDefinition(GridLength.Star), new ColumnDefinition(GridLength.Auto)]
                    };

                    var textLabel = new Label
                    {
                        Text = opt.DisplayText,
                        TextColor = opt.IsSelected
                            ? Colors.White
                            : (isLight ? Color.FromArgb("#0f172a") : Color.FromArgb("#f4f4f5")),
                        FontSize = 13,
                        FontAttributes = opt.IsSelected ? FontAttributes.Bold : FontAttributes.None,
                        VerticalOptions = LayoutOptions.Center
                    };

                    grid.Add(textLabel);
                    Grid.SetColumn(textLabel, 0);

                    if (opt.IsSelected)
                    {
                        var checkLabel = new Label
                        {
                            Text = "✓",
                            TextColor = Colors.White,
                            FontSize = 14,
                            FontAttributes = FontAttributes.Bold,
                            VerticalOptions = LayoutOptions.Center
                        };
                        grid.Add(checkLabel);
                        Grid.SetColumn(checkLabel, 1);
                    }

                    optBorder.Content = grid;

                    var tap = new TapGestureRecognizer();
                    var capturedOpt = opt;
                    tap.Tapped += (s, e) => CustomDialogService.SelectOption(capturedOpt);
                    optBorder.GestureRecognizers.Add(tap);

                    DialogOptionsList.Children.Add(optBorder);
                }
            }

            // 3. Buttons Grid
            DialogConfirmButton.Text = CustomDialogService.ConfirmButtonText;

            // In Options mode, hide confirm button (tapping an option immediately selects)
            DialogConfirmButton.IsVisible = !CustomDialogService.IsOptions;

            DialogOverlay.IsVisible = true;
        });
    }

    private void OnDialogConfirmClicked(object? sender, EventArgs e)
    {
        if (CustomDialogService.IsPrompt)
        {
            CustomDialogService.InputText = DialogInputEntry.Text ?? string.Empty;
        }
        CustomDialogService.Confirm();
    }

    private void OnDialogCancelClicked(object? sender, EventArgs e)
    {
        CustomDialogService.Cancel();
    }
}
