// PlayerPage.xaml.cs — In-app audio player code behind
using CommunityToolkit.Maui.Views;
using Microsoft.Maui.Controls.Shapes;
using Burudani.Services;

namespace Burudani.Views;

public partial class PlayerPage : ContentView
{
    private double _playerDragProgress = 0.0;
    private double _seekbarWidth = 0.0;
    private bool _isSeeking = false;
    private readonly IDispatcherTimer _syncHeartbeatTimer;

    public PlayerPage()
    {
        InitializeComponent();

        PlayerService.Instance.MediaSourceRequested += OnMediaSourceRequested;
        PlayerService.Instance.PlayRequested += OnPlayRequested;
        PlayerService.Instance.PauseRequested += OnPauseRequested;
        PlayerService.Instance.StopRequested += OnStopRequested;
        PlayerService.Instance.SeekRequested += OnSeekRequested;
        PlayerService.Instance.VolumeChanged += vol =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                MediaElementPlayer.Volume = vol;
            });
        };

        MediaElementPlayer.PositionChanged += (s, e) =>
        {
            PlayerService.Instance.UpdatePosition(e.Position, MediaElementPlayer.Duration);
            MainThread.BeginInvokeOnMainThread(() =>
            {
                if (!_isSeeking && ExpandingSeekTrack != null && ExpandingSeekTrack.Width > 0 && MediaElementPlayer.Duration > TimeSpan.Zero)
                {
                    double fraction = e.Position.TotalSeconds / MediaElementPlayer.Duration.TotalSeconds;
                    UpdateSeekFill(fraction, ExpandingSeekTrack.Width);
                }
            });
        };

        _syncHeartbeatTimer = Dispatcher.CreateTimer();
        _syncHeartbeatTimer.Interval = TimeSpan.FromMilliseconds(250);
        _syncHeartbeatTimer.Tick += (s, e) =>
        {
            if (PlayerService.Instance.IsPlaying && MediaElementPlayer != null)
            {
                var pos = MediaElementPlayer.Position;
                var dur = MediaElementPlayer.Duration;
                if (dur > TimeSpan.Zero || (PlayerService.Instance.CurrentTrack != null && PlayerService.Instance.CurrentTrack.DurationSec > 0))
                {
                    PlayerService.Instance.UpdatePosition(pos, dur);
                }
            }
        };
        _syncHeartbeatTimer.Start();

        MediaElementPlayer.MediaEnded += async (s, e) =>
        {
            await PlayerService.Instance.OnMediaEndedAsync();
        };

        MediaElementPlayer.MediaFailed += (s, e) =>
        {
            System.Diagnostics.Debug.WriteLine($"[MediaElement MediaFailed] Error: {e.ErrorMessage}");
        };
    }

    private void OnMediaSourceRequested(string source)
    {
        MainThread.BeginInvokeOnMainThread(() =>
        {
            try
            {
                if (source.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                    source.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                {
                    MediaElementPlayer.Source = MediaSource.FromUri(new Uri(source));
                }
                else if (File.Exists(source))
                {
                    MediaElementPlayer.Source = MediaSource.FromFile(source);
                }

                MediaElementPlayer.Play();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[MediaElement] Set source error: {ex.Message}");
            }
        });
    }

    private void OnPlayRequested()
    {
        MainThread.BeginInvokeOnMainThread(() => MediaElementPlayer.Play());
    }

    private void OnPauseRequested()
    {
        MainThread.BeginInvokeOnMainThread(() => MediaElementPlayer.Pause());
    }

    private void OnStopRequested()
    {
        MainThread.BeginInvokeOnMainThread(() => MediaElementPlayer.Stop());
    }

    private void OnSeekRequested(TimeSpan position)
    {
        MainThread.BeginInvokeOnMainThread(() => MediaElementPlayer.SeekTo(position));
    }

    private void OnSliderDragStarted(object? sender, EventArgs e)
    {
        ViewModel.OnSeekStarted();
    }

    private void OnSliderValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (ViewModel.IsUserSeeking)
        {
            ViewModel.PositionStr = TimeSpan.FromSeconds(e.NewValue).ToString(@"mm\:ss");
        }
    }

    private void OnSliderDragCompleted(object? sender, EventArgs e)
    {
        if (sender is Slider slider)
        {
            ViewModel.OnSeekCompleted(slider.Value);
        }
    }

    private void OnDesktopSeekTapped(object? sender, TappedEventArgs e)
    {
        if (sender is VisualElement track && track.Width > 0)
        {
            var pos = e.GetPosition(track);
            if (pos.HasValue)
            {
                double fraction = Math.Clamp(pos.Value.X / track.Width, 0.0, 1.0);
                ViewModel.SeekFraction(fraction);
            }
        }
    }

    // ── Fluid Motion Pan & Morphing for Android Mobile ──

    private async void OnPlayerPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        switch (e.StatusType)
        {
            case GestureStatus.Running:
                double totalH = MobilePlayerRoot?.Height ?? 600;
                if (totalH <= 0) totalH = 600;
                double delta = -e.TotalY / totalH;
                double newProgress = Math.Clamp(_playerDragProgress + delta, 0.0, 1.0);
                ApplyPlayerMorphProgress(newProgress);
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                double snapTarget = _playerDragProgress > 0.4 ? 1.0 : 0.0;
                await AnimateToSnapTarget(snapTarget);
                break;
        }
    }

    private void ApplyPlayerMorphProgress(double progress)
    {
        _playerDragProgress = progress;
        double screenWidth = MobilePlayerRoot?.Width ?? 360;
        if (screenWidth <= 0) screenWidth = 360;

        // Artwork morphs horizontally from 220 to screen width, height reduces
        double artW = 220 + (screenWidth - 260) * progress;
        double artH = 220 - (100 * progress);
        double artCorner = 16 - (16 * progress);

        if (ArtworkBorder != null)
        {
            ArtworkBorder.WidthRequest = Math.Max(artW, 120);
            ArtworkBorder.HeightRequest = Math.Max(artH, 120);
            if (ArtworkBorder.StrokeShape is RoundRectangle rr)
            {
                rr.CornerRadius = Math.Max(0, artCorner);
            }
        }

        if (TrackInfoContainer != null) TrackInfoContainer.Opacity = Math.Clamp(1.0 - (progress * 1.5), 0.0, 1.0);
        if (TransportControlsContainer != null) TransportControlsContainer.Opacity = Math.Clamp(1.0 - (progress * 1.5), 0.0, 1.0);

        if (RevealedQueueContainer != null)
        {
            RevealedQueueContainer.IsVisible = progress > 0.05;
            RevealedQueueContainer.Opacity = Math.Clamp((progress - 0.05) / 0.95, 0.0, 1.0);
        }
    }

    private async Task AnimateToSnapTarget(double target)
    {
        uint duration = 250;
        var tcs = new TaskCompletionSource<bool>();
        var anim = new Animation(v => ApplyPlayerMorphProgress(v), _playerDragProgress, target, Easing.CubicOut);
        anim.Commit(this, "PlayerSnap", length: duration, finished: (v, c) => tcs.TrySetResult(true));
        await tcs.Task;
    }

    private void OnSeekbarTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border seekTrack)
        {
            var tapPoint = e.GetPosition(seekTrack);
            if (tapPoint.HasValue && seekTrack.Width > 0)
            {
                double fraction = Math.Clamp(tapPoint.Value.X / seekTrack.Width, 0.0, 1.0);
                UpdateSeekFill(fraction, seekTrack.Width);
                ViewModel.SeekFraction(fraction);
            }
        }
    }

    private void OnSeekbarPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (sender is not Border seekTrack) return;
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _isSeeking = true;
                _seekbarWidth = seekTrack.Width;
                ExpandSeekbar();
                ViewModel.OnSeekStarted();
                break;
            case GestureStatus.Running:
                if (_seekbarWidth > 0)
                {
                    double currentX = Math.Clamp(e.TotalX + (SeekProgressFill?.WidthRequest ?? 0), 0.0, _seekbarWidth);
                    double fraction = Math.Clamp(currentX / _seekbarWidth, 0.0, 1.0);
                    UpdateSeekFill(fraction, _seekbarWidth);
                }
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _isSeeking = false;
                CollapseSeekbar();
                if (_seekbarWidth > 0 && SeekProgressFill != null)
                {
                    double fraction = Math.Clamp(SeekProgressFill.WidthRequest / _seekbarWidth, 0.0, 1.0);
                    ViewModel.SeekFraction(fraction);
                }
                break;
        }
    }

    private void ExpandSeekbar()
    {
        if (ExpandingSeekTrack != null)
        {
            new Animation(v => ExpandingSeekTrack.HeightRequest = v, ExpandingSeekTrack.HeightRequest, 14, Easing.CubicOut)
                .Commit(this, "ExpandSeek", length: 150);
        }
    }

    private void CollapseSeekbar()
    {
        if (ExpandingSeekTrack != null)
        {
            new Animation(v => ExpandingSeekTrack.HeightRequest = v, ExpandingSeekTrack.HeightRequest, 6, Easing.CubicOut)
                .Commit(this, "CollapseSeek", length: 150);
        }
    }

    private void UpdateSeekFill(double fraction, double trackWidth)
    {
        if (SeekProgressFill != null && trackWidth > 0)
        {
            SeekProgressFill.WidthRequest = Math.Clamp(fraction * trackWidth, 0, trackWidth);
        }
    }
}
