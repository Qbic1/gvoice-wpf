namespace VoiceRoom;

public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly MainWindow _window;

    public TrayIcon(MainWindow window)
    {
        _window = window;
        _icon = new NotifyIcon
        {
            Icon = new Icon("Resources/app-icon.ico"),
            Text = "Voice Room",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _icon.DoubleClick += (_, _) => Show();
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => Show());
        menu.Items.Add("Exit", null, (_, _) => Exit());
        return menu;
    }

    private void Show()
    {
        _window.Show();
        _window.WindowState = System.Windows.WindowState.Normal;
        _window.Activate();
    }

    private void Exit()
    {
        _icon.Visible = false;
        System.Windows.Application.Current.Shutdown();
    }

    public void Dispose() => _icon.Dispose();
}
