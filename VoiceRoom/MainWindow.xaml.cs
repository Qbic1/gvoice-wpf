using System;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Web.WebView2.Core;

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
        await webView.EnsureCoreWebView2Async();

        webView.CoreWebView2.PermissionRequested += (s, e) =>
        {
            if (e.PermissionKind is CoreWebView2PermissionKind.Camera
                                 or CoreWebView2PermissionKind.Microphone)
                e.State = CoreWebView2PermissionState.Allow;
        };

        webView.Source = new Uri(AppUrl);
    }
}
