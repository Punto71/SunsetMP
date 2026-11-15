using Android.Content;
using SunsetMP.Services;

namespace SunsetMP;

public class AndroidDownloadHost : IDownloadHost
{
    public event Action? CancelRequested;

    public AndroidDownloadHost()
    {
        DownloadForegroundService.CancelRequested +=
            () => MainThread.BeginInvokeOnMainThread(() => CancelRequested?.Invoke());
    }

    public void Show(string text, int percent)
    {
        DownloadForegroundService.NowText = text;
        DownloadForegroundService.NowPercent = percent;

        MainThread.BeginInvokeOnMainThread(() =>
        {
            var svc = DownloadForegroundService.Instance;
            if (svc != null) { svc.Refresh(); return; }
            try
            {
                var ctx = Android.App.Application.Context;
                ctx.StartForegroundService(new Intent(ctx, typeof(DownloadForegroundService)));
            }
            catch { }
        });
    }

    public void Hide() => MainThread.BeginInvokeOnMainThread(() =>
    {
        try
        {
            DownloadForegroundService.Instance?.Shutdown();
            var ctx = Android.App.Application.Context;
            ctx.StopService(new Intent(ctx, typeof(DownloadForegroundService)));
        }
        catch { }
    });
}
