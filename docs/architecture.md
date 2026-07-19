# Architecture

Voice Room is a single WPF project ([`VoiceRoom/`](../VoiceRoom/)) with a small,
deliberate file set. All visible UI is the remote website rendered in WebView2;
the native code exists only for the desktop-only concerns described below.

See also: [README.md](README.md) for scope, and
[code-review.md](code-review.md) for known issues in the code paths referenced
here.

## File map

| File | Responsibility |
|---|---|
| [`App.xaml.cs`](../VoiceRoom/App.xaml.cs) | Entry point, lifecycle owner, single-instance, crash logging. |
| [`App.xaml`](../VoiceRoom/App.xaml) | No `StartupUri` — everything is wired manually in `OnStartup`. |
| [`MainWindow.xaml`](../VoiceRoom/MainWindow.xaml) | WebView2 control + loading overlay (title, status, Retry). |
| [`MainWindow.xaml.cs`](../VoiceRoom/MainWindow.xaml.cs) | WebView2 init, trusted-origin permission gating, audio-session naming. |
| [`TrayIcon.cs`](../VoiceRoom/TrayIcon.cs) | WinForms `NotifyIcon`, Open/Exit menu, close-to-tray target. |
| [`VoiceRoom.csproj`](../VoiceRoom/VoiceRoom.csproj) | Target framework, self-contained publish, version, packages. |

## App.xaml.cs — application entry point and lifecycle

`App.xaml` declares no `StartupUri`, so `OnStartup` performs all wiring by hand:
it creates the `MainWindow`, attaches the `TrayIcon`, installs the crash-log
handlers, and starts the single-instance pipe listener.

### Single instance (Mutex + Named Pipe)

Two mechanisms cooperate:

1. **Detection — named `Mutex`.** On startup the app opens
   `Mutex(true, "VoiceRoom_SingleInstance", out bool isNewInstance)`. The first
   process owns the mutex; any later process sees `isNewInstance == false`.
2. **Hand-off — named pipe `"show"` message.** The second (non-owning) process
   calls `AllowSetForegroundWindow(ASFW_ANY)` (so the existing process is allowed
   to steal focus), connects to the pipe `VoiceRoom_Pipe` as a client, writes the
   line `show`, disposes its mutex handle **without releasing** (it never owned
   it), and calls `Shutdown()` — it never creates a window.

The first process runs `ListenForShowSignalAsync` on a background task: it opens
a `NamedPipeServerStream("VoiceRoom_Pipe", PipeDirection.In)`, awaits a
connection, reads a line, and on `"show"` marshals to the UI thread to
`Show()` + restore + `Activate()` + `SetForegroundWindow(...)` the main window.

**Recently applied fix:** the listener's `catch` for transient pipe errors now
performs `await Task.Delay(500, ct)` before retrying. Previously a persistent
pipe fault (name briefly held, ACL hiccup) spun the loop at 100% CPU. See
[code-review.md](code-review.md) (HIGH, FIXED).

Related open items (not yet fixed): the server stream is created without
`PipeOptions.Asynchronous`, and there is a narrow cold-start race where the
client's `Connect(1000)` may time out before the first instance's listener is
ready. Both are tracked in [code-review.md](code-review.md).

### Crash logging

Two handlers are registered in `OnStartup`:

- `DispatcherUnhandledException` → logs via `LogError`, sets `e.Handled = true`,
  and shows a `MessageBox` pointing at the log file.
- `AppDomain.CurrentDomain.UnhandledException` → logs when
  `ExceptionObject is Exception`.

`LogError` appends `"[timestamp]\n{exception}\n\n"` to:

```
%LocalAppData%\VoiceRoom\VoiceRoom-crash.log
```

The logger swallows its own I/O errors ("logging must never throw"). Note: the
log has **no rotation**, `Handled` is currently always `true` on the dispatcher
path (which can produce a flood of message boxes), and non-`Exception`
`ExceptionObject`s are not logged. See [code-review.md](code-review.md).

> Documentation note: an older `CLAUDE.md` passage said the crash log lands on
> the Desktop. The actual location is `%LocalAppData%\VoiceRoom\` as shown above.

### Shutdown path

`OnExit` cancels the pipe `CancellationTokenSource`, disposes the tray icon, and
(if this instance owns it) releases and disposes the mutex. `ShutdownMode` is not
set explicitly — the app stays alive because `MainWindow.Closing` is cancelled
(see below), and terminates only through tray **Exit** →
`Application.Current.Shutdown()`.

## MainWindow — WebView2 host

`MainWindow` hosts a single `wv2:WebView2` control overlaid by a loading `Grid`
(`loadingOverlay`) containing the "Voice Room" title, a `statusText` line, and a
`retryButton` (collapsed by default). The constructor wires
`Loaded += async (_, _) => await InitWebViewAsync()`.

### WebView2 environment

`InitWebViewAsync` creates the environment against an explicit user-data folder:

```
%LocalAppData%\VoiceRoom\WebView2
```

The same path is also exported as the `WEBVIEW2_USER_DATA_FOLDER` environment
variable in `App.OnStartup`. Both must stay in sync if the location changes. It
then calls `webView.EnsureCoreWebView2Async(env)` and navigates to
`AppUrl` (`https://voice-room.ru`).

> Open item: `InitWebViewAsync` runs from an `async void` `Loaded` handler with
> no `try/catch`. If environment creation or `EnsureCoreWebView2Async` throws
> (missing runtime, corrupt user-data), the overlay is stuck on "Connecting..."
> with no Retry. Tracked in [code-review.md](code-review.md).

### Auto camera/microphone — trusted origin only

`CoreWebView2.PermissionRequested` auto-grants **only** `Camera` and
`Microphone`, and **only** when `IsTrustedOrigin(e.Uri)` returns true:

```
IsTrustedOrigin: scheme must be https AND
  host == "voice-room.ru"  OR  host endsWith ".voice-room.ru"
```

**Recently applied fix:** previously cam/mic was granted to any origin loaded in
the WebView2 (redirects, iframes, external links). The origin check restricts it
to the app's own site. See [code-review.md](code-review.md) (MED, FIXED).

### Loading overlay

- `NavigationStarting` → show overlay, hide Retry, `statusText = "Connecting..."`.
- `NavigationCompleted` with `IsSuccess` → collapse overlay.
- `NavigationCompleted` failure → keep overlay, show
  `"Failed to load (error: {WebErrorStatus})"` and the Retry button.
- `RetryButton_Click` → re-`Navigate(AppUrl)` if `CoreWebView2` exists. (Note:
  this recovers from navigation failures but not from a failed environment init.)

### Audio-session naming (NAudio) — RefreshOurPids + SetupAudioSessionNaming

The goal is to label this app's entry in the Windows volume mixer as
"Voice Room" with the app icon, instead of the raw `msedgewebview2` child. The
challenge: the session is owned by a WebView2 child process, and other apps may
also run WebView2 — so renaming must target **only our own** process tree.

- **`RefreshOurPids`** (UI thread) rebuilds the `_ourPids` set from
  `CoreWebView2.Environment.GetProcessInfos()` plus the current process id, under
  `_pidLock`. It is called once after init and re-run on every
  `Environment.ProcessInfosChanged` event, then applies naming.
- **`SetupAudioSessionNaming`** creates an `MMDeviceEnumerator`, resolves the
  default render endpoint, and subscribes to
  `AudioSessionManager.OnSessionCreated`. The session only exists once audio
  actually starts (user joins a call), so it reacts to session creation rather
  than a one-off scan.
- **`OnAudioSessionCreated`** marshals from the COM callback thread to the UI
  thread and calls `TryRenameSession`.
- **`ApplyAudioSessionName`** walks the current `Sessions` collection and renames
  matches.
- **`TryRenameSession`** skips the system-sounds session, reads the session's
  process id, and — only if that pid is in `_ourPids` — sets
  `DisplayName = "Voice Room"` and `IconPath` to the app executable.

`_ourPids` is guarded by `_pidLock` because it is written on the UI thread and
read from the audio callback thread. Audio naming is best-effort: all NAudio
paths swallow exceptions.

> Open item: the NAudio COM objects (`_deviceEnumerator`, `_renderDevice`,
> `AudioSessionManager`), the `OnSessionCreated` subscription, and the `webView`
> itself are never released on exit. Tracked in
> [code-review.md](code-review.md).

## TrayIcon — system-tray presence

`TrayIcon` wraps a WinForms `NotifyIcon`:

- **Icon** is loaded from `AppContext.BaseDirectory\Resources\app-icon.ico`
  (resolved relative to the executable, because at Windows startup the working
  directory is `system32`). If the file is missing it falls back to the shared
  `SystemIcons.Application` (which must not be disposed — tracked by
  `_ownsIconImage`).
- **Context menu**: **Open** and **Exit**; **double-click** also shows the
  window.
- **Show** restores + activates the window. **Exit** hides the icon and calls
  `Application.Current.Shutdown()` — the only true termination path.

### Close-to-tray

In `App.OnStartup`, `MainWindow.Closing` is intercepted with
`args.Cancel = true; _mainWindow.Hide()`. Clicking the window's **X** therefore
hides the app to the tray instead of exiting. The process ends only through tray
**Exit**.

## WPF + WinForms coexistence

Both `UseWPF` and `UseWindowsForms` are enabled (WinForms for `NotifyIcon`).
This makes `Window`, `Application`, and `MessageBox` ambiguous, so the code
fully-qualifies them (`System.Windows.Window`, `System.Windows.Application`,
`System.Windows.MessageBox`). Follow that pattern when adding UI code.

## Lifecycle diagram

```
                         user launches VoiceRoom.exe
                                      |
                                      v
                    +-------------------------------------+
                    |  App.OnStartup                      |
                    |  Mutex("VoiceRoom_SingleInstance")  |
                    +-------------------------------------+
                          |                         |
             isNewInstance = false        isNewInstance = true
             (another copy runs)          (this is the first)
                    |                              |
                    v                              v
   +-----------------------------+   +-----------------------------------+
   | AllowSetForegroundWindow    |   | set WEBVIEW2_USER_DATA_FOLDER     |
   | pipe client -> "show"       |   | register crash handlers           |
   | Shutdown() (no window)      |   | new MainWindow / new TrayIcon      |
   +-----------------------------+   | Closing -> Cancel + Hide           |
                    |                | MainWindow.Show()                  |
                    |                | start ListenForShowSignalAsync ----+
                    |                +-----------------------------------+
                    |                              |
                    |                              v
                    |                +-----------------------------------+
                    |                | MainWindow.Loaded                 |
                    |                | InitWebViewAsync:                 |
                    |                |  - CreateAsync(userDataFolder)     |
                    |                |  - EnsureCoreWebView2Async         |
                    |                |  - PermissionRequested (cam/mic    |
                    |                |    only if IsTrustedOrigin)         |
                    |                |  - RefreshOurPids                  |
                    |                |  - SetupAudioSessionNaming (NAudio) |
                    |                |  - Navigate("https://voice-room.ru")|
                    |                +-----------------------------------+
                    |                              |
                    |          overlay: Connecting -> collapsed on success
                    |                    (Retry shown on failure)
                    |                              |
                    |                              v
                    |                 +--------------------------+
                    +---- "show" ---> |  RUNNING                 |
                        (pipe msg)    |  window <-> tray         |
                                      +--------------------------+
                                        |          |         |
                                   click X    tray Open   tray Exit
                                        |          |         |
                                   Hide()->tray  Show()   Shutdown()
                                                              |
                                                              v
                                            +-----------------------------+
                                            | App.OnExit                  |
                                            | pipeCts.Cancel()            |
                                            | tray.Dispose()              |
                                            | mutex release + dispose     |
                                            +-----------------------------+
```
</content>
