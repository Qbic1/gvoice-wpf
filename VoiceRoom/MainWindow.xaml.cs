using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace VoiceRoom;

public partial class MainWindow : Window
{
    private const string AppUrl = "https://voice-room.ru";

    private MMDeviceEnumerator? _deviceEnumerator;
    private MMDevice? _renderDevice;

    // Process IDs owned by our WebView2 environment. The audio session we want to
    // rename belongs to one of these (a WebView2 utility/renderer child), never to
    // some other app's WebView2. Guarded by _pidLock because it is read from the
    // audio callback thread and written on the UI thread.
    private readonly object _pidLock = new();
    private readonly HashSet<int> _ourPids = new();

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

        webView.CoreWebView2.PermissionRequested += (s, e) =>
        {
            // Only auto-grant camera/microphone to our own app origin. Without this
            // check any third-party page opened in the same WebView2 (redirect,
            // iframe, external link) would silently receive cam/mic access.
            if (e.PermissionKind is CoreWebView2PermissionKind.Camera
                                 or CoreWebView2PermissionKind.Microphone
                && IsTrustedOrigin(e.Uri))
                e.State = CoreWebView2PermissionState.Allow;
        };

        webView.CoreWebView2.NavigationStarting += (s, e) =>
        {
            loadingOverlay.Visibility = Visibility.Visible;
            retryButton.Visibility = Visibility.Collapsed;
            statusText.Text = "Connecting...";
        };

        webView.CoreWebView2.NavigationCompleted += (s, e) =>
        {
            if (e.IsSuccess)
            {
                loadingOverlay.Visibility = Visibility.Collapsed;
            }
            else
            {
                statusText.Text = $"Failed to load (error: {e.WebErrorStatus})";
                retryButton.Visibility = Visibility.Visible;
            }
        };

        // Keep the set of our WebView2 process IDs current, then (re)apply naming.
        webView.CoreWebView2.Environment.ProcessInfosChanged += (_, _) => RefreshOurPids();
        RefreshOurPids();

        SetupAudioSessionNaming();

        webView.CoreWebView2.Navigate(AppUrl);
    }

    /// <summary>
    /// True only for the app's own site (and its subdomains). Used to gate
    /// automatic camera/microphone permission grants.
    /// </summary>
    private static bool IsTrustedOrigin(string? uri)
    {
        if (string.IsNullOrEmpty(uri) || !Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return false;

        if (parsed.Scheme != Uri.UriSchemeHttps)
            return false;

        var host = parsed.Host;
        return host.Equals("voice-room.ru", StringComparison.OrdinalIgnoreCase)
            || host.EndsWith(".voice-room.ru", StringComparison.OrdinalIgnoreCase);
    }

    private void RetryButton_Click(object sender, RoutedEventArgs e)
    {
        if (webView.CoreWebView2 != null)
            webView.CoreWebView2.Navigate(AppUrl);
    }

    /// <summary>
    /// Refreshes the cached set of process IDs that belong to our WebView2
    /// environment and re-applies the audio session name for any that are playing.
    /// Must run on the UI thread (touches CoreWebView2).
    /// </summary>
    private void RefreshOurPids()
    {
        try
        {
            var infos = webView.CoreWebView2.Environment.GetProcessInfos();
            lock (_pidLock)
            {
                _ourPids.Clear();
                foreach (var info in infos)
                    _ourPids.Add(info.ProcessId);
                // Our own process, just in case audio is ever routed through it.
                _ourPids.Add(Process.GetCurrentProcess().Id);
            }
        }
        catch { return; }

        ApplyAudioSessionName();
    }

    private void SetupAudioSessionNaming()
    {
        try
        {
            _deviceEnumerator = new MMDeviceEnumerator();
            _renderDevice = _deviceEnumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);

            // The session only appears once audio actually starts (user joins a
            // call), so react to session creation rather than a one-off scan.
            _renderDevice.AudioSessionManager.OnSessionCreated += OnAudioSessionCreated;
        }
        catch { /* audio naming is best-effort */ }
    }

    private void OnAudioSessionCreated(object sender, IAudioSessionControl newSession)
    {
        // Fired on a COM callback thread — marshal to the UI thread where our
        // WebView2/COM objects live.
        Dispatcher.BeginInvoke(() =>
        {
            try { TryRenameSession(new AudioSessionControl(newSession)); }
            catch { }
        });
    }

    private void ApplyAudioSessionName()
    {
        if (_renderDevice == null)
            return;

        try
        {
            var sessions = _renderDevice.AudioSessionManager.Sessions;
            for (int i = 0; i < sessions.Count; i++)
                TryRenameSession(sessions[i]);
        }
        catch { }
    }

    private void TryRenameSession(AudioSessionControl session)
    {
        try
        {
            if (session.IsSystemSoundsSession)
                return;

            int pid = (int)session.GetProcessID;

            bool isOurs;
            lock (_pidLock)
                isOurs = _ourPids.Contains(pid);

            if (!isOurs)
                return;

            session.DisplayName = "Voice Room";
            session.IconPath = Process.GetCurrentProcess().MainModule?.FileName ?? "";
        }
        catch { /* a session may vanish mid-iteration */ }
    }
}
