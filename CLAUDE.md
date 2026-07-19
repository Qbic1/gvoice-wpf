# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Overview

Voice Room is a Windows desktop shell (WPF, .NET 10) that wraps the web app at
`https://voice-room.ru` in an embedded WebView2 browser. The native shell exists to
provide desktop-only behavior the web app can't: single-instance enforcement, a
system-tray presence, auto-granted camera/microphone permissions, and a named
audio session in the Windows volume mixer. All actual application UI is the remote
website — this repo contains no in-app pages.

## Build & Run

Requires the .NET 10 SDK on Windows (the project targets `net10.0-windows` with WPF
and WinForms; it cannot build or run on non-Windows platforms).

```powershell
dotnet build                                  # debug build
dotnet run --project VoiceRoom                # build & launch
dotnet publish -c Release -r win-x64 -o publish\win-x64   # produce installer payload
```

There are no tests in this repository.

### Building the installer

The installer is an Inno Setup script (`installer.iss`). Its `[Files]` section
expects `dotnet publish` output in `publish\win-x64\` and the WebView2 bootstrapper
(`MicrosoftEdgeWebView2RuntimeInstallerX64.exe`) in the repo root. Bump `AppVersion`
at the top of `installer.iss` when releasing. Compile with the Inno Setup Compiler
(`iscc installer.iss`); output lands in `installer-output\`. The installer only runs
the WebView2 runtime installer when `WebView2Installed` (a registry check) is false.

## Architecture

Single project, `VoiceRoom/`, with a small, deliberate set of files:

- **App.xaml.cs** — application entry point and lifecycle owner. `App.xaml` has no
  `StartupUri`; the `MainWindow`, `TrayIcon`, and the crash-log handlers are all
  created manually in `OnStartup`. This class implements two cross-cutting concerns:
  - **Single-instance**: a named `Mutex` (`VoiceRoom_SingleInstance`) detects a
    second launch. The second process connects to a `NamedPipeServerStream`
    (`VoiceRoom_Pipe`), sends `"show"`, and exits; the first process listens on that
    pipe and restores/foregrounds its window (via `SetForegroundWindow` P/Invoke).
  - **Crash logging**: `DispatcherUnhandledException` and `AppDomain.UnhandledException`
    both append to `VoiceRoom-crash.log` on the user's Desktop.
- **MainWindow.xaml / .cs** — hosts the `WebView2` control plus a loading overlay
  that is collapsed on first successful navigation. On init it:
  - creates the WebView2 environment against a user-data folder under
    `%LocalAppData%\VoiceRoom\WebView2`,
  - auto-allows camera/microphone permission requests,
  - navigates to `AppUrl` (`https://voice-room.ru` — the single source of the app URL),
  - after load, `SetAudioSessionName()` walks NAudio's audio sessions to find the
    spawned `msedgewebview2` process and renames its volume-mixer entry to "Voice Room".
- **TrayIcon.cs** — WinForms `NotifyIcon` with an Open/Exit context menu and
  double-click-to-show. "Exit" is the only path that truly shuts the app down.

### Close-to-tray behavior

`MainWindow.Closing` is intercepted in `App.OnStartup` (`args.Cancel = true; Hide()`),
so clicking the window's X hides it to the tray rather than exiting. The app only
terminates via the tray "Exit" item (`Application.Current.Shutdown()`), which calls
`OnExit` to cancel the pipe listener, dispose the tray icon, and release the mutex.

## Conventions & gotchas

- Because both WPF and WinForms are enabled, `Window`, `Application`, and
  `MessageBox` names are ambiguous. The code fully-qualifies them
  (`System.Windows.Window`, etc.) — follow that pattern when adding UI code.
- The `Resources/app-icon.ico` is both an embedded `<Resource>` and copied to the
  output directory; `TrayIcon` loads it by relative path (`Resources/app-icon.ico`),
  so it must exist next to the executable at runtime.
- `WEBVIEW2_USER_DATA_FOLDER` is set as an env var in `App.OnStartup` *and* the folder
  is passed explicitly to `CoreWebView2Environment.CreateAsync` in `MainWindow` — keep
  both pointing at the same `%LocalAppData%\VoiceRoom\WebView2` path if you change it.
