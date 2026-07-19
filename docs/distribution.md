# Distribution

How the Voice Room desktop shell reaches end users. The desktop app is
distributed **independently** of the web stack; the server side
(`voice-room.ru`) is deployed separately per
[../../gvoice-server/docs/deployment.md](../../gvoice-server/docs/deployment.md).

For how to produce the installer, see
[build-and-installer.md](build-and-installer.md).

## Shipping the installer

The deliverable is a single Inno Setup executable:

```
installer-output\VoiceRoom-Setup-<AppVersion>.exe
```

It is self-contained (bundles the .NET 10 runtime) and carries the WebView2
bootstrapper, so it installs and runs on a clean Windows machine with no .NET
Desktop Runtime.

Given the small audience (peak ≤ 10 concurrent users), a simple distribution
channel is sufficient:

- Host the `.exe` on the server or a shared location and send users a download
  link; or
- Hand off the file directly to each user.

There is no update feed or store listing today (see
[Updates](#updates) below).

Because the binaries are **not code-signed**, users will see a SmartScreen
"unrecognized app" prompt and a UAC elevation dialog (the installer writes to
Program Files). Code signing is the recommended fix and is tracked in
[code-review.md](code-review.md).

## WebView2 runtime bootstrapper

Voice Room requires the Microsoft Edge WebView2 runtime. The installer only runs
the bundled bootstrapper when WebView2 is not already present (the
`WebView2Installed` registry check). Choose the bootstrapper flavor by naming the
file placed in the repo root before compiling the installer:

| Flavor | File | Behavior | Use when |
|---|---|---|---|
| **Evergreen online (bootstrapper)** | small `MicrosoftEdgeWebView2RuntimeInstallerX64.exe` bootstrapper | Downloads the runtime from Microsoft during install; requires internet at install time. | Users have reliable internet during install; keeps the installer small. |
| **Evergreen offline (standalone)** | the full standalone `MicrosoftEdgeWebView2RuntimeInstallerX64.exe` | Installs the runtime fully offline; larger installer. | Installs may run without internet, or you want a fully self-contained package. |

Both are named identically in the script (`installer.iss` `[Files]`), so
swapping flavor is just a matter of which file you drop in the repo root. Given
that most modern Windows installs already carry the Evergreen runtime, the check
usually skips this step entirely.

## Updates

Updates are **manual** today: build a new installer with a bumped version and
have users reinstall over the top. There is no in-app update check.

Recommendation (not yet implemented): add a lightweight startup version check —
the app fetches a published version string from the server and, if newer than its
own `1.1.0`, notifies the user (or links to the new installer). This fits the
small-audience model without the complexity of a full update framework. Tracked
in [code-review.md](code-review.md) and
[build-and-installer.md](build-and-installer.md#remaining-recommendations).

## Autostart

The installer offers an optional "Start Voice Room with Windows" task that writes
an `HKCU\...\Run` value. Note the current limitation: it launches the app
normally, so the window appears on login instead of starting quietly to the tray.
The recommended fix is a `--minimized` argument; see
[build-and-installer.md](build-and-installer.md#remaining-recommendations).

## Uninstallation and residual data

Uninstalling via **Programs and Features** (or the Start-menu uninstaller)
removes:

- the installed files under `{autopf}\VoiceRoom`,
- the Start-menu and desktop shortcuts,
- the autostart `Run` registry value (`uninsdeletevalue`).

The uninstaller does **not** remove the per-user application data, which persists
under:

```
%LocalAppData%\VoiceRoom\
├── WebView2\                  (WebView2 profile: cache, cookies, local storage)
└── VoiceRoom-crash.log        (appended crash log, no rotation)
```

To fully reset a user's state — clear cached credentials/site data or a corrupt
WebView2 profile — delete `%LocalAppData%\VoiceRoom\` after uninstalling (or while
the app is closed). This is also the manual remedy if WebView2 initialization
fails due to a corrupt user-data folder.
</content>
