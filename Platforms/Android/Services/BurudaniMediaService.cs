#if ANDROID
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Graphics;
using Android.Media;
using Android.Media.Session;
using Android.OS;
using Burudani.Models;
using Burudani.Services;

namespace Burudani.Platforms.Android.Services;

[Service(ForegroundServiceType = ForegroundService.TypeMediaPlayback, Exported = false)]
public class BurudaniMediaService : Service
{
    public const string ChannelId = "burudani_playback_channel";
    public const int NotificationId = 10101;

    public const string ActionPlay = "com.burudani.ACTION_PLAY";
    public const string ActionPause = "com.burudani.ACTION_PAUSE";
    public const string ActionNext = "com.burudani.ACTION_NEXT";
    public const string ActionPrevious = "com.burudani.ACTION_PREVIOUS";
    public const string ActionStop = "com.burudani.ACTION_STOP";
    public const string ActionNotificationDismissed = "com.burudani.ACTION_NOTIFICATION_DISMISSED";

    private MediaSession? _mediaSession;
    private PowerManager.WakeLock? _wakeLock;
    private Bitmap? _cachedArtwork;
    private string _cachedArtworkUrl = string.Empty;

    public override void OnCreate()
    {
        base.OnCreate();
        CreateNotificationChannel();
        InitMediaSession();
        AcquireWakeLock();

        // Immediately call startForeground synchronously to satisfy Android 8+ contract
        StartForegroundImmediately();

        PlayerService.Instance.PropertyChanged += OnPlayerServicePropertyChanged;
    }

    private int GetCustomAppIcon()
    {
        int id = Resources?.GetIdentifier("ic_notification", "drawable", PackageName) ?? 0;
        if (id == 0) id = Resources?.GetIdentifier("ic_notification", "mipmap", PackageName) ?? 0;
        if (id == 0) id = Resources?.GetIdentifier("appicon", "mipmap", PackageName) ?? 0;
        if (id == 0) id = Resources?.GetIdentifier("appicon", "drawable", PackageName) ?? 0;
        if (id == 0) id = ApplicationInfo?.Icon ?? 0;
        if (id == 0) id = global::Android.Resource.Drawable.IcMediaPlay;
        return id;
    }

    private void StartForegroundImmediately()
    {
        try
        {
            var openAppIntent = new Intent(this, typeof(MainActivity));
            var contentPendingIntent = PendingIntent.GetActivity(this, 0, openAppIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            var builder = new Notification.Builder(this, ChannelId)
                .SetSmallIcon(GetCustomAppIcon())
                .SetContentTitle("Burudani")
                .SetContentText("Music playback ready")
                .SetContentIntent(contentPendingIntent)
                .SetOngoing(false)
                .SetVisibility(NotificationVisibility.Public);

            var notif = builder.Build();
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                StartForeground(NotificationId, notif, ForegroundService.TypeMediaPlayback);
            else
                StartForeground(NotificationId, notif);
        }
        catch { }
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(
                ChannelId,
                "Burudani Playback",
                NotificationImportance.Low)
            {
                Description = "Burudani background music playback and interactive controls",
                LockscreenVisibility = NotificationVisibility.Public
            };
            var manager = (NotificationManager?)GetSystemService(NotificationService);
            manager?.CreateNotificationChannel(channel);
        }
    }

    private void InitMediaSession()
    {
        _mediaSession = new MediaSession(this, "BurudaniMediaSession");
        _mediaSession.SetFlags(MediaSessionFlags.HandlesMediaButtons | MediaSessionFlags.HandlesTransportControls);
        _mediaSession.SetCallback(new MediaSessionCallback());
        _mediaSession.Active = true;
    }

    private void AcquireWakeLock()
    {
        try
        {
            var pm = (PowerManager?)GetSystemService(PowerService);
            if (pm != null)
            {
                _wakeLock = pm.NewWakeLock(WakeLockFlags.Partial, "Burudani::AudioWakeLock");
                _wakeLock?.Acquire();
            }
        }
        catch { }
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action != null)
        {
            switch (intent.Action)
            {
                case ActionPlay:
                    PlayerService.Instance.Play();
                    break;
                case ActionPause:
                    PlayerService.Instance.Pause();
                    break;
                case ActionNext:
                    _ = PlayerService.Instance.NextAsync();
                    break;
                case ActionPrevious:
                    _ = PlayerService.Instance.PreviousAsync();
                    break;
                case ActionStop:
                    PlayerService.Instance.Stop();
                    StopForeground(StopForegroundFlags.Remove);
                    StopSelf();
                    return StartCommandResult.NotSticky;
                case ActionNotificationDismissed:
                    PlayerService.Instance.Stop();
                    _wakeLock?.Release();
                    _wakeLock = null;
                    StopForeground(StopForegroundFlags.Remove);
                    StopSelf();
                    return StartCommandResult.NotSticky;
            }
        }

        UpdateNotification();
        return StartCommandResult.Sticky;
    }

    private async void OnPlayerServicePropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(PlayerService.CurrentTrack))
        {
            await LoadArtworkAsync();
            UpdateNotification();
        }
        else if (e.PropertyName == nameof(PlayerService.IsPlaying))
        {
            UpdateNotification();
        }
        else if (e.PropertyName == nameof(PlayerService.Position))
        {
            UpdatePlaybackState();
        }
    }

    private async Task LoadArtworkAsync()
    {
        var track = PlayerService.Instance.CurrentTrack;
        if (track == null || string.IsNullOrEmpty(track.ThumbnailUrl))
        {
            _cachedArtwork = null;
            return;
        }

        if (track.ThumbnailUrl == _cachedArtworkUrl && _cachedArtwork != null)
            return;

        try
        {
            if (track.ThumbnailUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                track.ThumbnailUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
                var bytes = await client.GetByteArrayAsync(track.ThumbnailUrl);
                _cachedArtwork = await BitmapFactory.DecodeByteArrayAsync(bytes, 0, bytes.Length);
                _cachedArtworkUrl = track.ThumbnailUrl;
            }
            else if (System.IO.File.Exists(track.ThumbnailUrl))
            {
                var ext = System.IO.Path.GetExtension(track.ThumbnailUrl).ToLowerInvariant();
                if (ext is ".mp3" or ".m4a" or ".aac" or ".flac" or ".wav" or ".webm")
                {
                    var artBytes = ArtworkService.Instance.GetRamCached(track.ThumbnailUrl)
                                ?? ArtworkService.Instance.ExtractArtworkFromAudioFile(track.ThumbnailUrl);
                    if (artBytes != null && artBytes.Length > 0)
                    {
                        _cachedArtwork = await BitmapFactory.DecodeByteArrayAsync(artBytes, 0, artBytes.Length);
                        _cachedArtworkUrl = track.ThumbnailUrl;
                    }
                }
                else
                {
                    _cachedArtwork = await BitmapFactory.DecodeFileAsync(track.ThumbnailUrl);
                    _cachedArtworkUrl = track.ThumbnailUrl;
                }
            }
        }
        catch
        {
            _cachedArtwork = null;
        }
    }

    private Bitmap? _fallbackBitmap;
    private Bitmap? GetFallbackBrandBitmap()
    {
        if (_fallbackBitmap != null && !_fallbackBitmap.IsRecycled)
            return _fallbackBitmap;

        try
        {
            var resId = Resources?.GetIdentifier("burudani", "drawable", PackageName) ?? 0;
            if (resId != 0)
            {
                _fallbackBitmap = BitmapFactory.DecodeResource(Resources, resId);
                if (_fallbackBitmap != null) return _fallbackBitmap;
            }
        }
        catch { }

        try
        {
            var bmp = Bitmap.CreateBitmap(256, 256, Bitmap.Config.Argb8888!);
            using var canvas = new Canvas(bmp);
            using var paint = new global::Android.Graphics.Paint { AntiAlias = true };
            canvas.DrawColor(new global::Android.Graphics.Color(24, 24, 27)); // #18181b Dark background
            paint.Color = new global::Android.Graphics.Color(234, 88, 12); // #ea580c Burudani orange
            canvas.DrawCircle(128, 128, 90, paint);
            _fallbackBitmap = bmp;
            return _fallbackBitmap;
        }
        catch
        {
            return null;
        }
    }

    private void UpdatePlaybackState()
    {
        if (_mediaSession == null) return;

        var state = PlayerService.Instance.IsPlaying ? PlaybackStateCode.Playing : PlaybackStateCode.Paused;
        long pos = (long)PlayerService.Instance.Position.TotalMilliseconds;
        float speed = PlayerService.Instance.IsPlaying ? 1.0f : 0.0f;

        var actions = PlaybackState.ActionPlay |
                      PlaybackState.ActionPause |
                      PlaybackState.ActionPlayPause |
                      PlaybackState.ActionSkipToNext |
                      PlaybackState.ActionSkipToPrevious |
                      PlaybackState.ActionSeekTo |
                      PlaybackState.ActionStop;

#pragma warning disable CS8602
        var stateBuilder = new PlaybackState.Builder()
            .SetActions(actions)
            .SetState(state, pos, speed, SystemClock.ElapsedRealtime());

        _mediaSession?.SetPlaybackState(stateBuilder?.Build());
#pragma warning restore CS8602
    }

    private void UpdateNotification()
    {
        var track = PlayerService.Instance.CurrentTrack;
        if (track == null)
        {
            StartForegroundImmediately();
            if (!PlayerService.Instance.IsPlaying)
            {
                StopForeground(StopForegroundFlags.Detach);
            }
            return;
        }

        UpdatePlaybackState();

        // Update Metadata
#pragma warning disable CS8602
        var metaBuilder = new MediaMetadata.Builder()
            .PutString(MediaMetadata.MetadataKeyTitle, track.DisplayTitle ?? "Burudani")
            .PutString(MediaMetadata.MetadataKeyArtist, track.Artist ?? "")
            .PutString(MediaMetadata.MetadataKeyAlbum, string.IsNullOrEmpty(track.Album) ? "Burudani" : track.Album)
            .PutLong(MediaMetadata.MetadataKeyDuration, (long)PlayerService.Instance.Duration.TotalMilliseconds);

        var effectiveArtwork = _cachedArtwork ?? GetFallbackBrandBitmap();
        if (effectiveArtwork != null && metaBuilder != null)
        {
            metaBuilder.PutBitmap(MediaMetadata.MetadataKeyAlbumArt, effectiveArtwork);
            metaBuilder.PutBitmap(MediaMetadata.MetadataKeyArt, effectiveArtwork);
            metaBuilder.PutBitmap(MediaMetadata.MetadataKeyDisplayIcon, effectiveArtwork);
        }

        if (metaBuilder != null)
        {
            _mediaSession?.SetMetadata(metaBuilder.Build());
        }
#pragma warning restore CS8602

        // PendingIntent for main activity
        var openIntent = new Intent(this, typeof(MainActivity));
        var appPendingIntent = PendingIntent.GetActivity(this, 0, openIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        // Action PendingIntents
        var prevPendingIntent = PendingIntent.GetService(this, 1, new Intent(this, typeof(BurudaniMediaService)).SetAction(ActionPrevious), PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var playPauseAction = PlayerService.Instance.IsPlaying ? ActionPause : ActionPlay;
        var playPausePendingIntent = PendingIntent.GetService(this, 2, new Intent(this, typeof(BurudaniMediaService)).SetAction(playPauseAction), PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);
        var nextPendingIntent = PendingIntent.GetService(this, 3, new Intent(this, typeof(BurudaniMediaService)).SetAction(ActionNext), PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var playPauseIcon = PlayerService.Instance.IsPlaying ? global::Android.Resource.Drawable.IcMediaPause : global::Android.Resource.Drawable.IcMediaPlay;

        var dismissIntent = new Intent(this, typeof(BurudaniMediaService)).SetAction(ActionNotificationDismissed);
        var dismissPendingIntent = PendingIntent.GetService(this, 99, dismissIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

        var builder = new Notification.Builder(this, ChannelId)
            .SetSmallIcon(GetCustomAppIcon())
            .SetContentTitle(track.DisplayTitle ?? "Burudani")
            .SetContentText(track.Artist ?? "")
            .SetSubText(string.IsNullOrEmpty(track.Album) ? "Burudani" : track.Album)
            .SetContentIntent(appPendingIntent)
            .SetDeleteIntent(dismissPendingIntent)
            .SetOngoing(PlayerService.Instance.IsPlaying)
            .SetVisibility(NotificationVisibility.Public)
#pragma warning disable CA1422
            .AddAction(new Notification.Action.Builder(global::Android.Resource.Drawable.IcMediaPrevious, new Java.Lang.String("Previous"), prevPendingIntent).Build())
            .AddAction(new Notification.Action.Builder(playPauseIcon, new Java.Lang.String(PlayerService.Instance.IsPlaying ? "Pause" : "Play"), playPausePendingIntent).Build())
            .AddAction(new Notification.Action.Builder(global::Android.Resource.Drawable.IcMediaNext, new Java.Lang.String("Next"), nextPendingIntent).Build());
#pragma warning restore CA1422

        if (effectiveArtwork != null)
        {
            builder.SetLargeIcon(effectiveArtwork);
        }

        if (_mediaSession?.SessionToken != null)
        {
#pragma warning disable CS8602
            var mediaStyle = new Notification.MediaStyle()
                .SetMediaSession(_mediaSession.SessionToken)
                .SetShowActionsInCompactView(0, 1, 2);

            builder.SetStyle(mediaStyle);
#pragma warning restore CS8602
        }

        var notification = builder.Build();

        if (PlayerService.Instance.IsPlaying)
        {
            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                StartForeground(NotificationId, notification, ForegroundService.TypeMediaPlayback);
            else
                StartForeground(NotificationId, notification);
        }
        else
        {
            // Paused: detach from foreground so notification can be dismissed by swiping away
            StopForeground(StopForegroundFlags.Detach);
            var nm = (NotificationManager?)GetSystemService(NotificationService);
            nm?.Notify(NotificationId, notification);
        }
    }

    public override void OnDestroy()
    {
        PlayerService.Instance.PropertyChanged -= OnPlayerServicePropertyChanged;

        _wakeLock?.Release();
        _wakeLock = null;

        _mediaSession?.Release();
        _mediaSession = null;

        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => null;

    private class MediaSessionCallback : MediaSession.Callback
    {
        public override void OnPlay() => PlayerService.Instance.Play();
        public override void OnPause() => PlayerService.Instance.Pause();
        public override void OnSkipToNext() => _ = PlayerService.Instance.NextAsync();
        public override void OnSkipToPrevious() => _ = PlayerService.Instance.PreviousAsync();
        public override void OnSeekTo(long pos) => PlayerService.Instance.Seek(TimeSpan.FromMilliseconds(pos));
        public override void OnStop() => PlayerService.Instance.Stop();
    }
}
#endif
