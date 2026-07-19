# Build and Installer

This covers building the Voice Room desktop shell and packaging it with Inno
Setup. For how the resulting installer is shipped and updated, see
[distribution.md](distribution.md). The web tier is out of scope here — see
[../../gvoice-server/docs/deployment.md](../../gvoice-server/docs/deployment.md).

## Requirements

- **Windows** — the project targets `net10.0-windows` with WPF and WinForms and
  cannot build or run on non-Windows platforms.
- **.NET 10 SDK** (Windows).
- **Inno Setup 6+** (the `iscc` compiler) — only for building the installer.
- **WebView2 bootstrapper** — `MicrosoftEdgeWebView2RuntimeInstallerX64.exe` in
  the repo root, only for building the installer (see
  [distribution.md](distribution.md) for which bootstrapper flavor to use).

The build machine does **not** need the .NET Desktop Runtime installed for end
users, because the publish is self-contained (below).

## Build and run (development)

```powershell
dotnet build                     # debug build
dotnet run --project VoiceRoom    # build and launch
```

There are no automated tests in the repository today; see the test strategy in
[code-review.md](code-review.md#test-strategy).

## Publish (self-contained)

The end-user payload is a **self-contained** `win-x64` publish. This is enforced
in [`VoiceRoom.csproj`](../VoiceRoom/VoiceRoom.csproj):

```xml
<RuntimeIdentifier>win-x64</RuntimeIdentifier>
<SelfContained>true</SelfContained>
<Version>1.1.0</Version>
```

Because the RID and `SelfContained` are pinned in the project file, the payload
carries its own copy of the .NET 10 runtime and **starts on a clean machine with
no .NET Desktop Runtime installed**. (A framework-dependent publish — the .NET 8+
default for `-r win-x64` — would fail to start there with no clear error. This
was the root cause of a HIGH bug; see [code-review.md](code-review.md).)

Produce the payload:

```powershell
dotnet publish -c Release -o publish\win-x64
```

The output folder `publish\win-x64\` is exactly what the installer's `[Files]`
section expects. Because the RID is already in the csproj, `-r win-x64` is
optional on the command line; passing it does no harm.

### Versioning

`<Version>` in the csproj and `AppVersion` in [`installer.iss`](../installer.iss)
must stay in sync (both `1.1.0` today). Bump **both** together when releasing —
the installer file name (`VoiceRoom-Setup-{AppVersion}.exe`) and the assembly
file version derive from these.

## Building the installer (Inno Setup)

The installer is defined by [`installer.iss`](../installer.iss). Its `[Files]`
section pulls from `publish\win-x64\` and bundles the WebView2 bootstrapper from
the repo root.

Steps:

1. Run the self-contained publish above so `publish\win-x64\` is current.
2. Place `MicrosoftEdgeWebView2RuntimeInstallerX64.exe` in the repo root (see
   [distribution.md](distribution.md) for offline vs online bootstrapper).
3. Bump `AppVersion` in `installer.iss` (and `<Version>` in the csproj) if
   releasing a new version.
4. Compile:

   ```powershell
   iscc installer.iss
   ```

   Output lands in `installer-output\` as
   `VoiceRoom-Setup-<AppVersion>.exe`.

### What the installer does

- Installs to `{autopf}\VoiceRoom` (Program Files), 64-bit mode
  (`x64compatible`).
- Creates Start-menu and desktop shortcuts to `VoiceRoom.exe`.
- Offers an **optional** "Start Voice Room with Windows" task, which writes an
  `HKCU\...\Run` value (removed on uninstall via `uninsdeletevalue`).
- Installs the WebView2 runtime silently **only when it is not already present**,
  gated by the `WebView2Installed` registry check.

### WebView2 detection (recently fixed)

The `WebView2Installed` function queries the WebView2 **Evergreen Runtime** client
GUID under both `HKLM\...\WOW6432Node\Microsoft\EdgeUpdate\Clients\{...}` and
`HKCU\...\Microsoft\EdgeUpdate\Clients\{...}`, treating an empty or `0.0.0.0`
`pv` value as "not installed."

**Recently applied fix:** the script previously contained GUID *placeholders*, so
the check never matched a real installation — meaning WebView2 was either
reinstalled every time or the gate misbehaved. The real Evergreen Runtime GUID is
now used for both the per-machine and per-user paths. See
[code-review.md](code-review.md) (HIGH, FIXED).

## Remaining recommendations

These are not yet implemented and are safe to defer, but matter for a public
release of an app that requests camera/microphone:

- **Code signing** — sign `VoiceRoom.exe` and the installer with an
  Authenticode certificate. Unsigned binaries trigger SmartScreen and UAC
  warnings, which are especially discouraging for a cam/mic app. See
  [code-review.md](code-review.md).
- **Silent autostart (`--minimized`)** — the autostart `Run` entry launches the
  app normally, so it pops the window on login instead of starting quietly to the
  tray. Add a `--minimized` argument (both to the installer's `Run` value and to
  the app's startup handling).
- **Auto-update** — updates are currently manual (reinstall). Consider a startup
  version check against the server. See [distribution.md](distribution.md).
</content>
