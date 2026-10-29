using System.Windows;

namespace WeatherWidget;

public partial class App : Application
{
    private Mutex? _mutex;

    protected override void OnStartup(StartupEventArgs e)
    {
        int snap = Array.IndexOf(e.Args, "--snapshot");
        string? snapshotPath = snap >= 0 && snap + 1 < e.Args.Length ? e.Args[snap + 1] : null;

        // 중복 실행 방지 (점검용 스냅샷 실행은 예외)
        _mutex = new Mutex(true, "WeatherWidget_SingleInstance", out bool createdNew);
        if (!createdNew && snapshotPath is null)
        {
            Shutdown();
            return;
        }
        base.OnStartup(e);
        var w = new MainWindow { SnapshotPath = snapshotPath };
        MainWindow = w;
        w.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
