using System.IO;

namespace VoiceRoom;

public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _iconImage;
    private readonly bool _ownsIconImage;
    private readonly MainWindow _window;

    public TrayIcon(MainWindow window)
    {
        _window = window;

        // Resolve the icon relative to the executable, not the current working
        // directory — at Windows startup the CWD is system32, not the app folder.
        var iconPath = Path.Combine(AppContext.BaseDirectory, "Resources", "app-icon.ico");
        if (File.Exists(iconPath))
        {
            _iconImage = new Icon(iconPath);
            _ownsIconImage = true;
        }
        else
        {
            // Shared system icon — must not be disposed.
            _iconImage = SystemIcons.Application;
            _ownsIconImage = false;
        }

        _icon = new NotifyIcon
        {
            Icon = _iconImage,
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

    public void Dispose()
    {
        _icon.Dispose();
        if (_ownsIconImage)
            _iconImage.Dispose();
    }
}
