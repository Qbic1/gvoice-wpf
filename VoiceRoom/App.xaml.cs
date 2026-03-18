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
    private MainWindow? _mainWindow;
    private CancellationTokenSource? _pipeCts;

    [DllImport("user32.dll")]
    private static extern bool SetForegroundWindow(IntPtr hWnd);

    protected override void OnStartup(StartupEventArgs e)
    {
        _mutex = new Mutex(true, "VoiceRoom_SingleInstance", out bool isNewInstance);

        if (!isNewInstance)
        {
            // Signal the running instance to show itself
            try
            {
                using var client = new NamedPipeClientStream(".", "VoiceRoom_Pipe", PipeDirection.Out);
                client.Connect(1000);
                using var writer = new StreamWriter(client);
                writer.WriteLine("show");
            }
            catch { }

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
            catch { /* ignore pipe errors, keep listening */ }
        }
    }

    private void OnDispatcherException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        LogError(e.Exception);
        e.Handled = true;
        System.Windows.MessageBox.Show($"Error: {e.Exception.Message}\n\nSee log on Desktop.",
            "Voice Room Error", MessageBoxButton.OK, MessageBoxImage.Error);
    }

    private void OnDomainException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
            LogError(ex);
    }

    private static void LogError(Exception ex)
    {
        var path = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            "VoiceRoom-crash.log");
        File.AppendAllText(path, $"[{DateTime.Now}]\n{ex}\n\n");
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _pipeCts?.Cancel();
        _tray?.Dispose();
        _mutex?.ReleaseMutex();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}