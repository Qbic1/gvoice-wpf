using System.Windows;

namespace VoiceRoom;

public partial class App : System.Windows.Application
{
    private TrayIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        var window = new MainWindow();
        _tray = new TrayIcon(window);
        window.Closing += (s, args) => { args.Cancel = true; window.Hide(); };
        window.Show();
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        base.OnExit(e);
    }
}
