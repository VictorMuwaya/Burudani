#if ANDROID
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.OS;
using Burudani.Services;

namespace Burudani.Platforms.Android.Services;

[Service(ForegroundServiceType = ForegroundService.TypeDataSync, Exported = false)]
public class BurudaniDownloadService : Service
{
    public const string ChannelId = "burudani_downloads_channel";
    public const int NotificationId = 20202;

    public const string ActionStart = "com.burudani.DOWNLOAD_START";
    public const string ActionStop = "com.burudani.DOWNLOAD_STOP";

    private PowerManager.WakeLock? _wakeLock;
    private int _completedCount = 0;

    public override void OnCreate()
    {
        base.OnCreate();
        CreateNotificationChannel();
        AcquireWakeLock();

        Burudani.Services.DownloadService.Instance.ItemProgress += OnItemProgress;
        Burudani.Services.DownloadService.Instance.ItemStarted += OnItemStarted;
        Burudani.Services.DownloadService.Instance.ItemCompleted += OnItemCompleted;
        Burudani.Services.DownloadService.Instance.QueueStatusChanged += OnQueueStatusChanged;
    }

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt >= BuildVersionCodes.O)
        {
            var channel = new NotificationChannel(
                ChannelId,
                "Burudani Downloads",
                NotificationImportance.Low)
            {
                Description = "Burudani background music download status and progress",
                LockscreenVisibility = NotificationVisibility.Public
            };
            var manager = (NotificationManager?)GetSystemService(NotificationService);
            manager?.CreateNotificationChannel(channel);
        }
    }

    private void AcquireWakeLock()
    {
        try
        {
            var pm = (PowerManager?)GetSystemService(PowerService);
            if (pm != null)
            {
                _wakeLock = pm.NewWakeLock(WakeLockFlags.Partial, "Burudani::DownloadWakeLock");
                _wakeLock?.Acquire(10 * 60 * 1000L); // 10 minutes timeout safety
            }
        }
        catch { }
    }

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        if (intent?.Action == ActionStop)
        {
            StopForeground(StopForegroundFlags.Remove);
            StopSelf();
            return StartCommandResult.NotSticky;
        }

        UpdateNotification("Starting download...", 0, "", "");
        return StartCommandResult.Sticky;
    }

    private void OnItemStarted(object? sender, string trackId)
    {
        var track = Burudani.Services.DownloadService.Instance.Queue.FirstOrDefault(t => t.Id == trackId);
        var title = track?.DisplayTitle ?? "Music track";
        UpdateNotification($"Downloading: {title}", 0, "Connecting...", "");
    }

    private void OnItemProgress(object? sender, DownloadProgressEventArgs e)
    {
        var track = Burudani.Services.DownloadService.Instance.Queue.FirstOrDefault(t => t.Id == e.TrackId);
        var title = track?.DisplayTitle ?? "Music track";
        UpdateNotification($"Downloading: {title}", (int)e.Progress, e.SpeedStr, e.EtaStr);
    }

    private void OnItemCompleted(object? sender, DownloadCompletedEventArgs e)
    {
        if (e.Success) _completedCount++;
    }

    private void OnQueueStatusChanged(object? sender, string status)
    {
        if (status.Contains("finished", StringComparison.OrdinalIgnoreCase) ||
            status.Contains("completed", StringComparison.OrdinalIgnoreCase) ||
            !Burudani.Services.DownloadService.Instance.IsBusy)
        {
            CompleteNotification();
        }
    }

    private int GetCustomAppIcon()
    {
        int id = Resources?.GetIdentifier("ic_notification", "drawable", PackageName) ?? 0;
        if (id == 0) id = Resources?.GetIdentifier("ic_notification", "mipmap", PackageName) ?? 0;
        if (id == 0) id = Resources?.GetIdentifier("appicon", "mipmap", PackageName) ?? 0;
        if (id == 0) id = Resources?.GetIdentifier("appicon", "drawable", PackageName) ?? 0;
        if (id == 0) id = ApplicationInfo?.Icon ?? 0;
        if (id == 0) id = global::Android.Resource.Drawable.StatSysDownload;
        return id;
    }

    private void UpdateNotification(string title, int progress, string speed, string eta)
    {
        try
        {
            var openIntent = new Intent(this, typeof(MainActivity));
            var appPendingIntent = PendingIntent.GetActivity(this, 0, openIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            string contentText = !string.IsNullOrEmpty(speed) && !string.IsNullOrEmpty(eta)
                ? $"{speed} • ETA: {eta}"
                : "Downloading audio...";

            var builder = new Notification.Builder(this, ChannelId)
                .SetSmallIcon(GetCustomAppIcon())
                .SetContentTitle(title)
                .SetContentText(contentText)
                .SetSubText("Burudani")
                .SetContentIntent(appPendingIntent)
                .SetOngoing(true)
                .SetProgress(100, progress, false)
                .SetVisibility(NotificationVisibility.Public);

            var notification = builder.Build();

            if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
                StartForeground(NotificationId, notification, ForegroundService.TypeDataSync);
            else
                StartForeground(NotificationId, notification);
        }
        catch { }
    }

    private void CompleteNotification()
    {
        try
        {
            var openIntent = new Intent(this, typeof(MainActivity));
            var appPendingIntent = PendingIntent.GetActivity(this, 0, openIntent, PendingIntentFlags.UpdateCurrent | PendingIntentFlags.Immutable);

            var summary = _completedCount > 0
                ? $"Downloaded {_completedCount} {(_completedCount == 1 ? "track" : "tracks")} to /storage/emulated/0/Burudani"
                : "All queue downloads complete";

            var builder = new Notification.Builder(this, ChannelId)
                .SetSmallIcon(GetCustomAppIcon())
                .SetContentTitle("Downloads Complete")
                .SetContentText(summary)
                .SetSubText("Burudani")
                .SetContentIntent(appPendingIntent)
                .SetOngoing(false)
                .SetAutoCancel(true)
                .SetProgress(0, 0, false)
                .SetVisibility(NotificationVisibility.Public);

            var notification = builder.Build();

            StopForeground(StopForegroundFlags.Detach);
            var nm = (NotificationManager?)GetSystemService(NotificationService);
            nm?.Notify(NotificationId, notification);

            _wakeLock?.Release();
            _wakeLock = null;

            StopSelf();
        }
        catch { }
    }

    public override void OnDestroy()
    {
        Burudani.Services.DownloadService.Instance.ItemProgress -= OnItemProgress;
        Burudani.Services.DownloadService.Instance.ItemStarted -= OnItemStarted;
        Burudani.Services.DownloadService.Instance.ItemCompleted -= OnItemCompleted;
        Burudani.Services.DownloadService.Instance.QueueStatusChanged -= OnQueueStatusChanged;

        _wakeLock?.Release();
        _wakeLock = null;

        base.OnDestroy();
    }

    public override IBinder? OnBind(Intent? intent) => null;
}
#endif
