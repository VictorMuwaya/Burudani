// MiniPlayerBar.xaml.cs — Bottom persistent audio player bar
using Burudani.Services;

namespace Burudani.Controls;

public partial class MiniPlayerBar : ContentView
{
    public event Action? NavigateToPlayerRequested;

    private bool _isMiniSeeking = false;
    private double _miniSeekWidth = 0;
    private bool _isWindowsSeeking = false;
    private bool _isUpdatingFromPlayer = false;
    private DateTime _lastSeekTime = DateTime.MinValue;

    public MiniPlayerBar()
    {
        InitializeComponent();
        BindingContext = PlayerService.Instance;

        PlayerService.Instance.PropertyChanged += (s, e) =>
        {
            if (e.PropertyName == nameof(PlayerService.PositionSeconds))
            {
                if (!_isMiniSeeking)
                {
                    MainThread.BeginInvokeOnMainThread(UpdateMiniSeekFill);
                }

                if (!_isWindowsSeeking && (DateTime.UtcNow - _lastSeekTime).TotalMilliseconds > 300)
                {
                    MainThread.BeginInvokeOnMainThread(UpdateWindowsSeekProgress);
                }
            }
            else if (e.PropertyName == nameof(PlayerService.DurationSeconds))
            {
                MainThread.BeginInvokeOnMainThread(UpdateWindowsDuration);
            }
        };
    }

    private void UpdateWindowsSeekProgress()
    {
        if (WindowsSeekSlider == null || _isWindowsSeeking) return;
        _isUpdatingFromPlayer = true;
        try
        {
            double dur = PlayerService.Instance.DurationSeconds;
            if (dur > 0 && Math.Abs(WindowsSeekSlider.Maximum - dur) > 0.5)
            {
                WindowsSeekSlider.Maximum = dur;
            }
            WindowsSeekSlider.Value = Math.Clamp(PlayerService.Instance.PositionSeconds, 0, WindowsSeekSlider.Maximum);
            if (WindowsTimeStringLabel != null && !_isWindowsSeeking)
            {
                WindowsTimeStringLabel.Text = PlayerService.Instance.TimeString;
            }
        }
        finally
        {
            _isUpdatingFromPlayer = false;
        }
    }

    private void UpdateWindowsDuration()
    {
        if (WindowsSeekSlider == null) return;
        _isUpdatingFromPlayer = true;
        try
        {
            double dur = Math.Max(1, PlayerService.Instance.DurationSeconds);
            WindowsSeekSlider.Maximum = dur;
            if (WindowsTimeStringLabel != null && !_isWindowsSeeking)
            {
                WindowsTimeStringLabel.Text = PlayerService.Instance.TimeString;
            }
        }
        finally
        {
            _isUpdatingFromPlayer = false;
        }
    }

    private void UpdateMiniSeekFill()
    {
        if (AndroidMiniSeekTrack != null && AndroidMiniSeekFill != null && AndroidMiniSeekTrack.Width > 0)
        {
            double duration = PlayerService.Instance.DurationSeconds;
            if (duration > 0)
            {
                double fraction = Math.Clamp(PlayerService.Instance.PositionSeconds / duration, 0.0, 1.0);
                AndroidMiniSeekFill.WidthRequest = fraction * AndroidMiniSeekTrack.Width;
            }
        }
    }

    private void OnVolumeIconTapped(object? sender, EventArgs e)
    {
        PlayerService.Instance.ToggleMute();
    }

    private void OnPlayPauseClicked(object? sender, EventArgs e)
    {
        PlayerService.Instance.TogglePlayPause();
    }

    private async void OnPrevClicked(object? sender, EventArgs e)
    {
        await PlayerService.Instance.PreviousAsync();
    }

    private async void OnNextClicked(object? sender, EventArgs e)
    {
        await PlayerService.Instance.NextAsync();
    }

    private void OnLoopClicked(object? sender, EventArgs e)
    {
        PlayerService.Instance.CycleRepeat();
    }

    private void OnSeekDragStarted(object? sender, EventArgs e)
    {
        _isWindowsSeeking = true;
    }

    private void OnSeekValueChanged(object? sender, ValueChangedEventArgs e)
    {
        if (_isUpdatingFromPlayer) return;

        double durSec = PlayerService.Instance.DurationSeconds;
        double targetSec = Math.Clamp(e.NewValue, 0, durSec > 0 ? durSec : 100);
        var cur = TimeSpan.FromSeconds(targetSec);
        var dur = TimeSpan.FromSeconds(durSec);

        if (WindowsTimeStringLabel != null)
        {
            WindowsTimeStringLabel.Text = $"{(int)cur.TotalMinutes}:{cur.Seconds:D2} / {(int)dur.TotalMinutes}:{dur.Seconds:D2}";
        }

        // If slider value changed via mouse click without DragStarted/DragCompleted (WinUI slider track click)
        if (!_isWindowsSeeking)
        {
            _lastSeekTime = DateTime.UtcNow;
            PlayerService.Instance.Seek(TimeSpan.FromSeconds(targetSec));
        }
    }

    private void OnSeekCompleted(object? sender, EventArgs e)
    {
        if (sender is Slider slider)
        {
            double durSec = PlayerService.Instance.DurationSeconds;
            double targetSec = Math.Clamp(slider.Value, 0, durSec > 0 ? durSec : 100);
            _lastSeekTime = DateTime.UtcNow;
            PlayerService.Instance.Seek(TimeSpan.FromSeconds(targetSec));
        }

        Task.Delay(350).ContinueWith(_ =>
        {
            MainThread.BeginInvokeOnMainThread(() =>
            {
                _isWindowsSeeking = false;
                UpdateWindowsSeekProgress();
            });
        });
    }

    private void OnWindowsSeekTrackTapped(object? sender, TappedEventArgs e)
    {
        if (sender is VisualElement track && track.Width > 0 && WindowsSeekSlider != null)
        {
            var pos = e.GetPosition(track);
            if (pos.HasValue)
            {
                double fraction = Math.Clamp(pos.Value.X / track.Width, 0.0, 1.0);
                double durSec = PlayerService.Instance.DurationSeconds;
                double targetSec = fraction * (durSec > 0 ? durSec : 100);

                _isWindowsSeeking = true;
                _isUpdatingFromPlayer = true;
                try
                {
                    WindowsSeekSlider.Value = targetSec;
                }
                finally
                {
                    _isUpdatingFromPlayer = false;
                }

                _lastSeekTime = DateTime.UtcNow;
                PlayerService.Instance.Seek(TimeSpan.FromSeconds(targetSec));

                var cur = TimeSpan.FromSeconds(targetSec);
                var dur = TimeSpan.FromSeconds(durSec);
                if (WindowsTimeStringLabel != null)
                {
                    WindowsTimeStringLabel.Text = $"{(int)cur.TotalMinutes}:{cur.Seconds:D2} / {(int)dur.TotalMinutes}:{dur.Seconds:D2}";
                }

                Task.Delay(350).ContinueWith(_ =>
                {
                    MainThread.BeginInvokeOnMainThread(() =>
                    {
                        _isWindowsSeeking = false;
                        UpdateWindowsSeekProgress();
                    });
                });
            }
        }
    }

    // ── Headless Expanding Seek Bar for Android ──

    private void OnAndroidMiniSeekTapped(object? sender, TappedEventArgs e)
    {
        if (sender is Border track)
        {
            AnimateMiniSeekbarTactile();
            var pos = e.GetPosition(track);
            if (pos.HasValue && track.Width > 0)
            {
                double fraction = Math.Clamp(pos.Value.X / track.Width, 0.0, 1.0);
                SetAndroidMiniSeekFraction(fraction, track.Width);
            }
        }
    }

    private void OnAndroidMiniSeekPanUpdated(object? sender, PanUpdatedEventArgs e)
    {
        if (sender is not Border track) return;
        switch (e.StatusType)
        {
            case GestureStatus.Started:
                _isMiniSeeking = true;
                _miniSeekWidth = track.Width;
                AnimateMiniSeekbarExpanded(true);
                break;
            case GestureStatus.Running:
                if (_miniSeekWidth > 0 && AndroidMiniSeekFill != null)
                {
                    double currentX = Math.Clamp(e.TotalX + AndroidMiniSeekFill.WidthRequest, 0, _miniSeekWidth);
                    AndroidMiniSeekFill.WidthRequest = currentX;
                }
                break;
            case GestureStatus.Completed:
            case GestureStatus.Canceled:
                _isMiniSeeking = false;
                AnimateMiniSeekbarExpanded(false);
                if (_miniSeekWidth > 0 && AndroidMiniSeekFill != null)
                {
                    double fraction = Math.Clamp(AndroidMiniSeekFill.WidthRequest / _miniSeekWidth, 0.0, 1.0);
                    SetAndroidMiniSeekFraction(fraction, _miniSeekWidth);
                }
                break;
        }
    }

    private void SetAndroidMiniSeekFraction(double fraction, double trackWidth)
    {
        if (AndroidMiniSeekFill != null)
        {
            AndroidMiniSeekFill.WidthRequest = fraction * trackWidth;
        }
        double targetSec = fraction * PlayerService.Instance.DurationSeconds;
        PlayerService.Instance.Seek(TimeSpan.FromSeconds(targetSec));
    }

    private void AnimateMiniSeekbarTactile()
    {
        if (AndroidMiniSeekTrack == null) return;
        new Animation(v => AndroidMiniSeekTrack.HeightRequest = v, 4, 10, Easing.CubicOut)
            .Commit(this, "MiniSeekExpand", length: 120, finished: (v, c) =>
            {
                new Animation(v2 => AndroidMiniSeekTrack.HeightRequest = v2, 10, 4, Easing.CubicIn)
                    .Commit(this, "MiniSeekCollapse", length: 180);
            });
    }

    private void AnimateMiniSeekbarExpanded(bool expand)
    {
        if (AndroidMiniSeekTrack == null) return;
        double target = expand ? 8 : 4;
        new Animation(v => AndroidMiniSeekTrack.HeightRequest = v, AndroidMiniSeekTrack.HeightRequest, target, Easing.CubicOut)
            .Commit(this, "MiniSeekHold", length: 120);
    }

    private void OnCloseClicked(object? sender, EventArgs e)
    {
        PlayerService.Instance.Stop();
        IsVisible = false;
    }

    private async void OnSwipeLeft(object? sender, SwipedEventArgs e)
    {
        await PlayerService.Instance.NextAsync();
    }

    private async void OnSwipeRight(object? sender, SwipedEventArgs e)
    {
        await PlayerService.Instance.PreviousAsync();
    }

    private void OnNavigateToPlayerTapped(object? sender, EventArgs e)
    {
        NavigateToPlayerRequested?.Invoke();
    }
}
