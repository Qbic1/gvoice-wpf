# Code Review

Structured review of the Voice Room desktop shell. Findings are grouped by
status: **FIXED** (already applied in the current code) and **OPEN** (described,
not yet fixed — final verification is on Windows and belongs to the maintainer).

Severity: **HIGH** (breaks core function or resource-abusive), **MED** (real
defect / robustness or security gap), **LOW** (minor / hygiene), **OPT**
(optional improvement).

Cross-references: [architecture.md](architecture.md),
[build-and-installer.md](build-and-installer.md),
[distribution.md](distribution.md).

## Fixed

| # | Sev | File | Issue | Resolution |
|---|-----|------|-------|-----------|
| F1 | HIGH | [`App.xaml.cs`](../VoiceRoom/App.xaml.cs) | `ListenForShowSignalAsync` had a `catch` with no delay: a persistent pipe error spun the loop at 100% CPU (busy-loop). | `catch` now does `await Task.Delay(500, ct)` (with `OperationCanceledException` handling) to back off before retrying. |
| F2 | HIGH | [`installer.iss`](../installer.iss) | The `WebView2Installed` check used GUID **placeholders**, so it never matched a real install — WebView2 was reinstalled every time / the gate misbehaved. | Real WebView2 Evergreen Runtime GUID substituted for both `HKLM\...\WOW6432Node` and `HKCU` paths. |
| F3 | HIGH | [`VoiceRoom.csproj`](../VoiceRoom/VoiceRoom.csproj) | Framework-dependent publish meant the .NET 10 Desktop Runtime was never installed — the app failed to start on a clean machine with no clear error. | `RuntimeIdentifier=win-x64` + `SelfContained=true` (self-contained publish); `Version=1.1.0` added to stay in sync with the installer. |
| F4 | MED | [`MainWindow.xaml.cs`](../VoiceRoom/MainWindow.xaml.cs) | `PermissionRequested` auto-granted camera/microphone to **any** origin (redirects, iframes, external links). | Added `IsTrustedOrigin` — grants only over `https` to `voice-room.ru` and its subdomains. |

## Open

### MED — WebView2 init has no error handling

**File:** [`MainWindow.xaml.cs`](../VoiceRoom/MainWindow.xaml.cs)
(`Loaded += async (_, _) => await InitWebViewAsync()`).

`InitWebViewAsync` runs from an `async void` handler with no `try/catch`. If
`CoreWebView2Environment.CreateAsync` or `EnsureCoreWebView2Async` throws (runtime
missing, corrupt user-data folder), the overlay is stuck on "Connecting..."
forever and no Retry appears — the exception surfaces only through the global
crash handler. **Recommendation:** wrap init in `try/catch`; on failure set
`statusText` to an actionable message and show `retryButton`, and make Retry able
to re-initialize the environment (not just re-navigate). See
[architecture.md](architecture.md#webview2-environment).

### MED — COM / resource leaks on exit

**File:** [`MainWindow.xaml.cs`](../VoiceRoom/MainWindow.xaml.cs).

`_deviceEnumerator`, `_renderDevice`, and the `AudioSessionManager` (NAudio COM
objects) are never released; the `OnSessionCreated` handler is never unsubscribed;
and the `webView` is never disposed. **Recommendation:** implement deterministic
cleanup on `Exit` (unsubscribe `OnSessionCreated`, dispose the NAudio objects and
the WebView2 control).

### MED — Named pipe server not asynchronous

**File:** [`App.xaml.cs`](../VoiceRoom/App.xaml.cs)
(`new NamedPipeServerStream("VoiceRoom_Pipe", PipeDirection.In)`).

Created without `PipeOptions.Asynchronous`. Async pipe operations
(`WaitForConnectionAsync`) are most reliable when the handle is opened for
overlapped I/O. **Recommendation:** pass `PipeOptions.Asynchronous`.

### MED — Dispatcher exception handling is blunt

**File:** [`App.xaml.cs`](../VoiceRoom/App.xaml.cs) (`OnDispatcherException`).

`e.Handled` is always set to `true`, and each exception shows a `MessageBox` — a
repeating fault can produce a flood of dialogs and mask fatal states.
**Recommendation:** always log, set `Handled` selectively (only for genuinely
recoverable cases), and deduplicate / rate-limit the user-facing message.

### MED — Autostart is not silent

**File:** [`installer.iss`](../installer.iss) (`HKCU\...\Run` entry).

The autostart value launches the app without `--minimized`, so the window opens
on login instead of starting quietly to the tray. **Recommendation:** add
`--minimized` to the `Run` value and handle it in `App.OnStartup` (start hidden to
tray). See
[build-and-installer.md](build-and-installer.md#remaining-recommendations).

### MED — No code signing

**Files:** `VoiceRoom.exe`, the installer.

Unsigned binaries trigger SmartScreen and UAC warnings — a significant trust and
adoption problem for an app that requests camera and microphone access.
**Recommendation:** Authenticode-sign the executable and the installer. See
[distribution.md](distribution.md).

### LOW — Single-instance cold-start race

**File:** [`App.xaml.cs`](../VoiceRoom/App.xaml.cs).

A second instance calls `client.Connect(1000)`, but if it launches while the first
instance has not yet started its pipe listener, the connect can time out and the
"show" signal is lost (the second instance still exits). Narrow window; low impact.
**Recommendation:** brief retry, or accept as-is given the small audience.

### LOW — `Process.GetCurrentProcess()` not disposed

**File:** [`MainWindow.xaml.cs`](../VoiceRoom/MainWindow.xaml.cs) (`RefreshOurPids`,
`TryRenameSession`).

`Process.GetCurrentProcess()` returns a disposable object created on frequently
hit paths without disposal. **Recommendation:** cache the PID once, or wrap in
`using`.

### LOW — `ShutdownMode` not set explicitly

**File:** [`App.xaml.cs`](../VoiceRoom/App.xaml.cs).

App lifetime relies on the cancelled `MainWindow.Closing` rather than an explicit
`ShutdownMode`. Works, but is implicit. **Recommendation:** set
`ShutdownMode = OnExplicitShutdown` to make intent clear and decouple lifetime
from window state.

### LOW — Crash-log location and rotation

**Files:** [`App.xaml.cs`](../VoiceRoom/App.xaml.cs), `CLAUDE.md`.

`CLAUDE.md` states the crash log is written to the Desktop; the code actually
writes `%LocalAppData%\VoiceRoom\VoiceRoom-crash.log`. The log also grows
unbounded (no rotation/size cap). **Recommendation:** correct the docs (done in
this set — see [architecture.md](architecture.md#crash-logging)) and cap/rotate
the log.

### LOW — `AppDomain.UnhandledException` drops non-`Exception` payloads

**File:** [`App.xaml.cs`](../VoiceRoom/App.xaml.cs) (`OnDomainException`).

Only logs when `ExceptionObject is Exception`; a non-`Exception` payload is
silently ignored. **Recommendation:** log `ExceptionObject?.ToString()` as a
fallback.

### OPT — Miscellaneous improvements

- **Centralize configuration.** `AppUrl`, the trusted origin, and the WebView2
  user-data folder are defined in more than one place (the folder appears in both
  `App.OnStartup` and `MainWindow`). Consolidate into one config source.
- **Redundant icon copy.** `installer.iss` copies `app-icon.ico` both via the
  recursive `publish\win-x64\*` glob and an explicit line — the explicit copy is
  redundant.
- **External links.** Handle `NewWindowRequested` to open external links in the
  system browser rather than inside the app's WebView2.
- **Release hardening.** Disable DevTools and the default context menu in Release
  builds.

## Test strategy

Voice Room is a thin shell: full UI automation is expensive relative to its value.
A layered approach gives most of the coverage for a fraction of the cost.

1. **Unit-test the pure logic.** Extract the framework-free logic into small,
   testable classes and cover them with xUnit:
   - `IsTrustedOrigin` (scheme + host / subdomain matching, including rejection of
     `http`, look-alike hosts, and malformed URIs);
   - the "is this PID ours?" membership check;
   - path helpers (user-data folder, crash-log path);
   - single-instance message parsing (the `"show"` protocol).

   These have real bug surface (F4 was an origin-check gap) and no UI dependency.

2. **One smoke test via UI automation.** A single end-to-end check with WinAppDriver
   or FlaUI: launch → window appears → tray icon present → launching a second
   instance re-focuses the first (no duplicate window).

3. **Manual release checklist on a clean machine** (no WebView2, no .NET runtime):
   - installer runs; WebView2 bootstrapper installs only when absent;
   - app starts and loads `https://voice-room.ru`;
   - autostart works (and — once implemented — starts minimized to tray);
   - camera/microphone are granted without an in-app prompt;
   - single-instance re-focus works;
   - close-to-tray and tray **Exit** behave correctly;
   - crash log appears at `%LocalAppData%\VoiceRoom\VoiceRoom-crash.log` on a
     forced error.
</content>
