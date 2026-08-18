# Syncthing Service Wrapper

A small Windows service wrapper that runs [Syncthing](https://syncthing.net/) as a native Windows service.
It lets you start, stop, and auto-start Syncthing like any other Windows service, with proper logging
and a graceful shutdown.

## Features

- Runs `syncthing.exe` as a Windows service (`Syncthing`)
- Service auto-start on boot + automatic restart if Syncthing crashes (`sc failure`)
- Captures Syncthing console output into `syncthing.log` (next to the wrapper), plus service lifecycle events
- Graceful shutdown: on stop, the wrapper asks Syncthing to shut down via its REST API
  (`POST /rest/system/shutdown`), waits up to 20 s, and only falls back to `taskkill /F`
- Extra command-line parameters can be passed to `syncthing.exe` (one-off or permanent)
- Watchdog: if Syncthing dies, the service stops itself
- Self-upgrade is disabled (`STNOUPGRADE=1`) — update Syncthing manually (see below)
- Pure .NET Framework 4.x — no external dependencies, no installers, compiles with the `csc.exe`
  that ships with Windows

## Requirements

- Windows 10/11 (or any Windows with .NET Framework 4.x, which is built in)
- `syncthing.exe` — place it in the same folder as `SyncthingService.exe`

## Installation

1. Download (or build, see below) `SyncthingService.exe`.
2. Put it in the same folder as `syncthing.exe`. Layout:

   ```
   C:\Syncthing\
   ├── syncthing.exe
   ├── SyncthingService.exe
   ├── syncthing.log      (created automatically)
   └── syncthing-args.txt (created only if you use `params`)
   ```

3. Open a terminal **as Administrator** and install the service:

   ```
   C:\Syncthing\SyncthingService.exe install
   ```

4. Start it:

   ```
   C:\Syncthing\SyncthingService.exe start
   ```

That's it. The service is configured to start automatically with Windows and to restart
if it crashes.

## Building from source

Compile with the .NET Framework C# compiler (already on Windows):

```
C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /target:exe ^
  /out:SyncthingService.exe /r:System.ServiceProcess.dll /r:System.Xml.dll ^
  SyncthingService.cs
```

## Usage

```
SyncthingService.exe install           Install the service (admin required)
SyncthingService.exe uninstall         Stop and remove the service
SyncthingService.exe start [params...] Start the service; extra params are passed to syncthing.exe
SyncthingService.exe stop              Stop the service
SyncthingService.exe status            Show service state
SyncthingService.exe params [args...]  Save permanent extra args for syncthing.exe
                                       (no args = clear); stored in syncthing-args.txt
SyncthingService.exe run               Run in console for testing (no service)
```

### Examples

Pass parameters for a single run (forwarded to `syncthing.exe`):

```
SyncthingService.exe start --gui-address=127.0.0.1:8080 --no-browser
```

Set permanent parameters applied on every start:

```
SyncthingService.exe params --no-browser --gui-address=127.0.0.1:8080
SyncthingService.exe params          # clear them
```

Notes:

- `--no-restart` is always added automatically (unless you already passed it), so the service
  has full control over the process lifecycle.
- The Windows SCM remembers the arguments of the last `sc start` and replays them on later
  starts even if you don't pass any. Use `params` for settings you want to keep.
- Effective arguments are logged on every start (`Syncthing args: ...`).

## Logging

All output goes to `syncthing.log` next to the wrapper:

```
=== Syncthing service starting ===
Syncthing args: --no-restart
2026-08-18 12:39:20 INF syncthing v2.1.3 "Hafnium Hornet" (go1.26.5 windows-amd64) ...
Syncthing started, PID 65236
=== Syncthing service stopping ===
Graceful shutdown requested via REST API
Syncthing exited gracefully
=== Syncthing service stopped ===
```

## Updating Syncthing

The wrapper disables Syncthing's self-upgrade (`STNOUPGRADE=1`). Reason: after a self-upgrade
Syncthing spawns the new process itself (via its "upgrade" helper), outside of the service —
the wrapper would lose control over it and the service would stop.

Update manually:

1. `SyncthingService.exe stop`
2. Replace `syncthing.exe` with the new version (download from
   [syncthing.net](https://syncthing.net/) / GitHub releases)
3. `SyncthingService.exe start`

## How it works

- The wrapper is a real Windows service (written in C#, `ServiceBase`). The service manager
  (`sc.exe`) starts/stops it like any native service.
- On start it spawns `syncthing.exe` as a child process with redirected output, which is
  written to `syncthing.log`.
- On stop it reads the GUI address and API key from Syncthing's `config.xml`
  (`%LOCALAPPDATA%\Syncthing\config.xml`) and posts `/rest/system/shutdown` — the same
  graceful shutdown you get with Ctrl+C. If Syncthing doesn't exit within 20 seconds,
  the process is killed with `taskkill /PID <pid> /T /F`.
- A watchdog timer stops the service if the Syncthing process exits unexpectedly.

## Troubleshooting

- **`Failed to acquire lock: is another Syncthing instance already running?`**
  Stop the manually running instance first — only one Syncthing can use the same config.
- **Access denied when running `install` / `uninstall`** — run the command from an elevated
  (Administrator) terminal.
- **Syncthing can't reach network shares** — the service runs as `LocalSystem` by default.
  If you need a user account, configure it via `services.msc` → `Syncthing` → Log On.
- **GUI port conflict** — if Syncthing already listens on port 8384, pass another address,
  e.g. `params --gui-address=127.0.0.1:8385`.

## License

MIT
