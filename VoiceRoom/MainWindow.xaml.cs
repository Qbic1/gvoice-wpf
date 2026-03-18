using System;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using NAudio.CoreAudioApi;

namespace VoiceRoom;

public partial class MainWindow : Window
{
    private const string AppUrl = "https://voice-room.ru";

    public MainWindow()
    {
        InitializeComponent();
        Loaded += async (_, _) => await InitWebViewAsync();
    }

    private async Task InitWebViewAsync()
    {
        var userDataFolder = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "VoiceRoom", "WebView2");

        var env = await CoreWebView2Environment.CreateAsync(
            browserExecutableFolder: null,
            userDataFolder: userDataFolder);

        await webView.EnsureCoreWebView2Async(env);

        // rest of the method unchanged...
        webView.CoreWebView2.PermissionRequested += (s, e) =>
        {
            if (e.PermissionKind is CoreWebView2PermissionKind.Camera
                                 or CoreWebView2PermissionKind.Microphone)
                e.State = CoreWebView2PermissionState.Allow;
        };

        webView.CoreWebView2.NavigationCompleted += (s, e) =>
        {
            if (e.IsSuccess)
                loadingOverlay.Visibility = Visibility.Collapsed;
            else
                statusText.Text = $"Failed to load (error: {e.WebErrorStatus})";
        };

        webView.CoreWebView2.NavigationCompleted += (s, e) =>
        {
            if (e.IsSuccess)
            {
                loadingOverlay.Visibility = Visibility.Collapsed;
                SetAudioSessionName();
            }
            else
            {
                statusText.Text = $"Failed to load (error: {e.WebErrorStatus})";
            }
        };

        webView.CoreWebView2.NavigationStarting += (s, e) =>
        {
            loadingOverlay.Visibility = Visibility.Visible;
            statusText.Text = "Connecting...";
        };

        webView.CoreWebView2.Navigate(AppUrl);
    }

    private void SetAudioSessionName()
    {
        try
        {
            var enumerator = new MMDeviceEnumerator();
            var device = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
            var sessions = device.AudioSessionManager.Sessions;

            for (int i = 0; i < sessions.Count; i++)
            {
                var session = sessions[i];

                // Find the WebView2 process spawned by our app
                try
                {
                    var process = Process.GetProcessById((int)session.GetProcessID);
                    if (process.ProcessName.Contains("msedgewebview2", StringComparison.OrdinalIgnoreCase))
                    {
                        // Check it belongs to our app by checking parent process
                        session.DisplayName = "Voice Room";
                        session.IconPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
                    }
                }
                catch { }
            }
        }
        catch { }
    }
}