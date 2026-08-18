# Syncthing Service Wrapper

A small Windows service wrapper that runs [Syncthing](https://syncthing.net/) as a native Windows service.
It lets you start, stop, and auto-start Syncthing like any other Windows service, with proper logging
and a graceful shutdown.

## Features

- Runs `syncthing.exe` as a Windows service (`Syncthing`)
- Service auto-start on boot + automatic restart if Syncthing crashes (`sc failure`)
- Service lifecycle events go to the Windows Event Log (source `SyncthingService`); Syncthing keeps its own log in its config directory
- Graceful shutdown: on stop, the wrapper asks Syncthing to shut down via its REST API
  (`POST /rest/system/shutdown`), waits up to 20 s, and only falls back to `taskkill /F`
- Extra command-line parameters can be passed to `syncthing.exe` (one-off or permanent)
- Watchdog: if Syncthing dies, the service stops itself
- Syncthing can self-upgrade: the wrapper detects the clean exit, kills the orphan process left by Syncthing's upgrade helper and starts the updated binary under service control
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
   └── SyncthingService.exe
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
SyncthingService.exe install [--home path] Install the service (admin required; optional config dir)
SyncthingService.exe uninstall         Stop and remove the service
SyncthingService.exe start [params...] Start the service; extra params are passed to syncthing.exe
SyncthingService.exe stop              Stop the service
SyncthingService.exe status            Show service state
SyncthingService.exe params [args...]  Save permanent extra args for syncthing.exe
                                       (no args = clear); stored in the service registry
                                       (HKLM\SYSTEM\CurrentControlSet\Services\Syncthing\Parameters)
SyncthingService.exe home [path]       Show/set the Syncthing config dir (--home); stored in the
                                       service registry. Set automatically during install — the
                                       service runs as LocalSystem, which would otherwise use a
                                       fresh empty config in the system profile
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

- --home given to install is written into the service binPath (visible in services.msc
  → Properties → "Path to executable") and mirrored to the service registry on each start.
- --no-restart is always added automatically (unless you already passed it), so the service
  has full control over the process lifecycle.
- The Windows SCM remembers the arguments of the last `sc start` and replays them on later
  starts even if you don't pass any. Use `params` for settings you want to keep.
- Effective arguments are logged on every start (`Syncthing args: ...`).

## Logging

Syncthing writes its own log into its config directory (`syncthing.log` there) — check it for
transfer and connection details. The wrapper only logs service lifecycle events
(start/stop/args/errors) to the **Windows Event Log** under source `SyncthingService`
(Event Viewer → Windows Logs → Application).

Example events:

- `Syncthing service starting`
- `Syncthing args: --no-restart --home="C:\Users\...\Syncthing"`
- `Syncthing started, PID 1234`
- `Graceful shutdown requested via REST API`
- `Syncthing exited gracefully`

## Updating Syncthing

Syncthing can self-upgrade (default interval: 12 h, or via the GUI button). The upgrade flow:

1. Syncthing downloads the new version and starts its "upgrade" helper, then exits cleanly (code 0).
2. The helper replaces `syncthing.exe` and starts the new process on its own (as an orphan).
3. The wrapper sees the clean exit, waits 5 s, kills the orphan process, and starts the updated
   `syncthing.exe` under service control again.

Manual update still works:

1. `SyncthingService.exe stop`
2. Replace `syncthing.exe` with the new version
3. `SyncthingService.exe start`

## How it works

- The wrapper is a real Windows service (written in C#, `ServiceBase`). The service manager
  (`sc.exe`) starts/stops it like any native service.
- On start it spawns `syncthing.exe` as a child process and logs service lifecycle events
  to the Windows Event Log.
- On stop it reads the GUI address and API key from Syncthing's `config.xml`
  (`%LOCALAPPDATA%\Syncthing\config.xml`) and posts `/rest/system/shutdown` — the same
  graceful shutdown you get with Ctrl+C. If Syncthing doesn't exit within 20 seconds,
  the process is killed with `taskkill /PID <pid> /T /F`.
- A watchdog timer stops the service if the Syncthing process exits unexpectedly.

## Troubleshooting

- **`Failed to acquire lock: is another Syncthing instance already running?`**
  Stop the manually running instance first — only one Syncthing can use the same config.
- **"Folder Unshared" / empty config in the GUI** — the service runs as `LocalSystem`, which
  looks for the config in the *system* profile and starts with a fresh empty config. `install`
  saves your real config dir into the service registry automatically. If it's missing or wrong,
  fix it with `SyncthingService.exe home C:\path\to\Syncthing`, then restart the service.
- **Access denied when running `install` / `uninstall`** — run the command from an elevated
  (Administrator) terminal.
- **Syncthing can't reach network shares** — the service runs as `LocalSystem` by default.
  If you need a user account, configure it via `services.msc` → `Syncthing` → Log On.
- **GUI port conflict** — if Syncthing already listens on port 8384, pass another address,
  e.g. `params --gui-address=127.0.0.1:8385`.

## License

MIT
