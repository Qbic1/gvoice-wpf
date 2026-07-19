# Voice Room — Desktop Shell Documentation

Voice Room (WPF) is a thin native Windows wrapper around the web application
hosted at <https://voice-room.ru>. It embeds the live website in an
[WebView2](https://learn.microsoft.com/microsoft-edge/webview2/) control and
adds a small set of desktop-only capabilities that a browser tab cannot provide
on its own.

The shell owns no application UI. Every page the user sees is the remote site;
this repository ships no HTML, CSS, or in-app views. If the website changes, the
desktop app follows automatically on the next navigation — no rebuild required.

## Why a native shell exists

A plain browser bookmark would already open the site. The wrapper is justified
only by behaviors the web platform cannot deliver:

| Capability | What it gives the user |
|---|---|
| **Single instance** | A second launch (double-click, autostart, shortcut) re-focuses the existing window instead of opening a duplicate. |
| **System-tray presence** | Closing the window hides it to the tray; the app keeps running so calls/notifications survive. Explicit **Exit** is the only real shutdown. |
| **Auto camera/microphone grant** | The trusted origin (`voice-room.ru` and its subdomains) receives cam/mic permission automatically — no repeated in-page prompts on every join. |
| **Named audio session** | The Windows volume mixer shows a labeled "Voice Room" entry with the app icon instead of an anonymous `msedgewebview2` process. |
| **Crash logging** | Unhandled exceptions are appended to a local log for support. |

## Scope and boundaries

- **This app** is the Windows client only. It is distributed separately via an
  installer (see [distribution.md](distribution.md)).
- **The web stack** (server, containers, TLS, the `voice-room.ru` deployment)
  is documented in the server repository:
  [../../gvoice-server/docs/deployment.md](../../gvoice-server/docs/deployment.md).
  This documentation set does not duplicate it.
- **Deployment context**: end users run Windows; the web tier runs on a
  dedicated server (`voice-room.ru`) sized for a small audience (peak ≤ 10
  concurrent users). The desktop shell is intentionally minimal to match.

## Document index

| Document | Contents |
|---|---|
| [architecture.md](architecture.md) | Process model, single-instance IPC, WebView2 hosting, trusted-origin permission gating, audio-session naming, tray lifecycle, and an ASCII lifecycle diagram. |
| [build-and-installer.md](build-and-installer.md) | Toolchain requirements, build/run, **self-contained** publish, and building the Inno Setup installer. |
| [distribution.md](distribution.md) | Shipping the installer, WebView2 bootstrapper options (offline vs Evergreen online), update handling, and uninstall/residual data. |
| [code-review.md](code-review.md) | Structured findings (severity, file, FIXED/OPEN status, recommendation) and a test strategy for a thin-shell app. |

## Quick reference

- **Target framework**: `net10.0-windows` (WPF + WinForms).
- **Runtime**: self-contained `win-x64` — no .NET Desktop Runtime required on the
  target machine.
- **Version**: `1.1.0` (kept in sync between
  [`VoiceRoom/VoiceRoom.csproj`](../VoiceRoom/VoiceRoom.csproj) `<Version>` and
  [`installer.iss`](../installer.iss) `AppVersion`).
- **App URL**: `https://voice-room.ru` (single source: `MainWindow.AppUrl`).
- **Per-user data**: `%LocalAppData%\VoiceRoom\` (WebView2 profile + crash log).
</content>
</invoke>
