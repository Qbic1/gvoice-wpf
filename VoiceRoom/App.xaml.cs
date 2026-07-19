using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;

namespace VoiceRoom;

public partial class App : System.Windows.Application
{
    private TrayIcon? _tray;
    private Mutex? _mutex;
    private bool _ownsMutex;
    private MainWindow? _mainWindow;
    private CancellationTokenSource? _pipeCts;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    [DllImport("user32.dll")]
    private static extern bool AllowSetForegroundWindow(int dwProcessId);

    private const int ASFW_ANY = -1;

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "VoiceRoom_SingleInstance", out bool isNewInstance);
        _ownsMutex = isNewInstance;

        if (!isNewInstance)
        {
            // Signal the running instance to show itself
            try
            {
                // Let the already-running instance take the foreground.
                AllowSetForegroundWindow(ASFW_ANY);

                using var client = new NamedPipeClientStream(".", "VoiceRoom_Pipe", PipeDirection.Out);
                client.Connect(1000);
                using var writer = new StreamWriter(client);
                writer.WriteLine("show");
            }
            catch { }

            // This instance never owned the mutex, so it must not be released.
            _mutex.Dispose();
            _mutex = null;

            Shutdown();
            return;
        }

        // In OnStartup, before creating MainWindow
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceRoom", "WebView2");

        Environment.SetEnvironmentVariable("WEBVIEW2_USER_DATA_FOLDER", userDataFolder);

        DispatcherUnhandledException += OnDispatcherException;
        AppDomain.CurrentDomain.UnhandledException += OnDomainException;

        base.OnStartup(e);

        _mainWindow = new MainWindow();
        _tray = new TrayIcon(_mainWindow);
        _mainWindow.Closing += (s, args) => { args.Cancel = true; _mainWindow.Hide(); };
        _mainWindow.Show();

        // Start listening for "show" signals from new instances
        _pipeCts = new CancellationTokenSource();
        _ = ListenForShowSignalAsync(_pipeCts.Token);
    }

    private async Task ListenForShowSignalAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var server = new NamedPipeServerStream("VoiceRoom_Pipe", PipeDirection.In);
                await server.WaitForConnectionAsync(ct);
                using var reader = new StreamReader(server);
                var message = await reader.ReadLineAsync(ct);

                if (message == "show")
                {
                    // Marshal back to UI thread
                    Dispatcher.Invoke(() =>
                    {
                        if (_mainWindow != null)
                        {
                            _mainWindow.Show();
                            _mainWindow.WindowState = WindowState.Normal;
                            _mainWindow.Activate();
                            SetForegroundWindow(new System.Windows.Interop.WindowInteropHelper(_mainWindow).Handle);
                        }
                    });
                }
            }
            catch (OperationCanceledException) { break; }
            catch
            {
                // A transient pipe error (name temporarily held, ACL hiccup) must not
                // turn into a 100%-CPU busy-loop: back off briefly before retrying.
                try { await Task.Delay(500, ct); }
                catch (OperationCanceledException) { break; }
            }
        }
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        var logPath = LogError(e.Exception);
        e.Handled = true;
        System.Windows.MessageBox.Show($"Error: {e.Exception.Message}\n\nSee log:\n{logPath}",
            "Voice Room Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogError(ex);
    }

    private static string LogError(Exception ex)
    {
        var dir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceRoom");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, "VoiceRoom-crash.log");
        try
        {
            File.AppendAllText(path, $"[{DateTime.Now}]\n{ex}\n\n");
        }
        catch { /* logging must never throw */ }
        return path;
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeCts?.Cancel();
        _tray?.Dispose();
        if (_mutex != null)
        {
            if (_ownsMutex)
                _mutex.ReleaseMutex();
            _mutex.Dispose();
        }
        base.OnExit(e);
    }
}