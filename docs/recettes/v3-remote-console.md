# Recette V3 — Console déportée (connexion + commandes)

ClickUp task: `[Recette V3] Console déportée (connexion + commandes)` — P4 Finition V3
Tags: `grille-tuteur`, `recette-v3`, `dev3-state` / `dev4-cli`

## Goal

Verify that the v3 remote operator console connects to the EasySave engine over
TCP, observes job events live, sends Pause/Play/Stop commands, recovers from
network loss, and supports multiple concurrent operators — including from a
second machine on the LAN.

## How the remote console works

- The engine hosts `TcpRemoteConsoleServer` on the port configured in
  `appsettings.json` (`remote_console_enabled: true`, `remote_console_port: 9000`
  by default). On startup, `App.axaml.cs` calls `server.StartAsync(port, ct)`
  on the application background.
- The server accepts N concurrent TCP clients. Each connection logs a
  `RemoteConsoleConnected` event in the daily log; disconnect logs
  `RemoteConsoleDisconnected`.
- Events flow engine → bus → server → all clients via
  `RemoteConsoleBroadcastBridge` (subscribes to `EventDto` on `IEventBus` and
  calls `BroadcastAsync`).
- The client (`EasySave.RemoteConsole`) is a separate Avalonia app. It
  connects to a configured `host:port`, deserializes incoming JSON lines into
  `EventDto`, and sends `CommandDto` (Pause / Play / Stop) when the operator
  clicks the matching button on a job.
- On connection loss the client transitions
  `Connected → Disconnected → Connecting → ...` and retries automatically
  (`TcpRemoteConsoleClient` reconnect loop).

## Pre-requisites

- Build everything once:
  ```bash
  dotnet build EasySave.sln
  dotnet build EasySave.RemoteConsole/EasySave.RemoteConsole.csproj
  ```
- Enable the server in `appsettings.json` next to the EasySave executable
  (`src/EasySave.UI/bin/Debug/net8.0/appsettings.json` for `dotnet run`):
  ```json
  "remote_console_enabled": true,
  "remote_console_port": 9000
  ```
- For LAN scenarios, allow inbound TCP on port 9000 in the host firewall
  (Windows Defender Firewall: New Inbound Rule → Port 9000 TCP → Allow).
- Configure at least one backup job that runs long enough to interact with —
  e.g. ≥ 200 small files or ~50 MB to copy, so Pause / Play windows are
  observable. Use the v2 GUI Jobs view to create it.
- Wiring is live since the V3 wire-remote-console PR: `App.axaml.cs`
  resolves `IEventBus` / `IRemoteConsoleServer` / `StateTrackerEventBridge`
  / `RemoteConsoleBroadcastBridge` from the DI container and starts the
  TCP listener + bridges when `RemoteConsoleEnabled` is true. Disabling
  the flag in `appsettings.json` keeps the GUI's startup unchanged.

### Known limitation — scenario 4

The adapter has no dedicated `Stop` API yet. The `Stop` button in the
client routes through `PauseJob` with reason `"Stopped"`, so the job
halts at the next file boundary but `state.json` reports `"Paused"`
(not `"Inactive"`) until a follow-up exposes a true Stop transition.
Expect step 4.2 to be a partial pass; track via the issue raised in
the next iteration.

## Scenarios

Each scenario uses an empty result table at the bottom — fill it in during the
manual run and paste the screenshot link if relevant.

### 1. Connexion locale + événements live

| Step | Action | Expected |
|---|---|---|
| 1.1 | Launch the engine: `dotnet run --project src/EasySave.UI` | Window appears, `appsettings.json` shows `remote_console_enabled: true` |
| 1.2 | Launch the client: `dotnet run --project EasySave.RemoteConsole` | Window appears, `Host = 127.0.0.1`, `Port = 9000`, `Disconnected` |
| 1.3 | Click **Connect** in the client | Status flips to `Connected` within 1–2 s |
| 1.4 | In the engine GUI, click **Run** on a configured job | The client's job grid shows the job with progress updating live (FilesLeft decreasing, CurrentFile updating) |
| 1.5 | Open today's log file (`%AppData%\ProSoft\EasySave\Logs\<yyyy-MM-dd>.json` on Windows; `~/.config/ProSoft/EasySave/Logs/` on Linux/macOS) | Contains a `RemoteConsoleConnected` entry with the client's IP |

**Pass / Fail:** ____  **Tester:** ____  **Date:** ____

### 2. Pause depuis la console

| Step | Action | Expected |
|---|---|---|
| 2.1 | Start a long-running job from the engine | Job state in client grid = `Running`, progress increasing |
| 2.2 | In the client, click **Pause** on that job | Within ~1 file boundary, state in client AND in engine GUI flips to `Paused` |
| 2.3 | Inspect `state.json` (`%AppData%\ProSoft\EasySave\state.json`) | Entry for the job: `"State": "Paused"`, `PauseReason` set, `FilesRemaining > 0` |
| 2.4 | Inspect today's daily log | Contains `CommandReceived` (or equivalent v3 marker) entry for the Pause |

**Pass / Fail:** ____  **Tester:** ____  **Date:** ____

### 3. Play / reprise depuis la console

| Step | Action | Expected |
|---|---|---|
| 3.1 | After scenario 2, click **Play** on the paused job | State flips back to `Running` in both UIs within 1–2 s |
| 3.2 | Watch FilesLeft | Decreases from where it stopped, **not** restarted from 0 |
| 3.3 | At completion, target folder | Contains the full backup (no missing or duplicated files) |

**Pass / Fail:** ____  **Tester:** ____  **Date:** ____

### 4. Stop depuis la console

| Step | Action | Expected |
|---|---|---|
| 4.1 | Start another long-running job | Job state = `Running` in client |
| 4.2 | Click **Stop** on the job | Job stops at the next file boundary; client view shows `Done`, `state.json` shows `"State": "Inactive"`. No exception in the engine console output |
| 4.3 | Engine GUI is still responsive (other jobs runnable) | No frozen state, no stack trace in stderr |
| 4.4 | Daily log | Last entry for the job is a clean partial run, `FileTransferTimeMs ≥ 0` for copied files |

**Pass / Fail:** ____  **Tester:** ____  **Date:** ____

### 5. Coupure réseau + auto-reconnect

| Step | Action | Expected |
|---|---|---|
| 5.1 | With client `Connected` and an idle engine, disable the active network adapter (or disconnect Wi-Fi) | Client connection state transitions `Connected → Disconnected` within ~5 s |
| 5.2 | Wait ~5 s while disconnected | Client UI remains responsive, retries are visible (state oscillates `Disconnected ↔ Connecting`) |
| 5.3 | Re-enable the network adapter | State automatically transitions `Connecting → Connected`, **without** clicking Connect |
| 5.4 | Run a job from the engine | Live events flow again into the client |

**Pass / Fail:** ____  **Tester:** ____  **Date:** ____

### 6. Deux consoles simultanées

| Step | Action | Expected |
|---|---|---|
| 6.1 | Launch two `EasySave.RemoteConsole` instances on the same machine (e.g. two terminals: `dotnet run --project EasySave.RemoteConsole`) | Both windows appear |
| 6.2 | Connect both to `127.0.0.1:9000` | Both show `Connected` |
| 6.3 | Run a job from the engine | Both client grids update in lock-step (same FilesLeft, same CurrentFile) |
| 6.4 | Send `Pause` from console A | Both A and B reflect `Paused` for the targeted job |
| 6.5 | Send `Play` from console B | Both A and B reflect `Running` again — no command duplication, no client-affinity bug |
| 6.6 | Daily log | Two `RemoteConsoleConnected` entries (different IPs/ports), `CommandReceived` entries tagged with the originating SourceIp |

**Pass / Fail:** ____  **Tester:** ____  **Date:** ____

### 7. Replay sur 2 machines distinctes (LAN)

Run scenarios 1, 2, 3, 4 again, but with the client on a **different physical
machine** on the same LAN. Use the engine machine's LAN IP (`ipconfig` /
`ifconfig`) instead of `127.0.0.1`.

Pre-flight checklist:
- [ ] Inbound TCP 9000 allowed in the engine host's firewall.
- [ ] Both machines on the same subnet (`ping <engine-ip>` from the client machine returns).
- [ ] `appsettings.json` on the engine has `remote_console_enabled: true`.

| Step | Action | Expected |
|---|---|---|
| 7.1 | Client (machine B) → connect to `<engine-ip>:9000` | `Connected` |
| 7.2 | Replay scenarios 1–4 above with steps reading "client" interpreted on machine B | All scenarios pass identically |
| 7.3 | Engine daily log | `RemoteConsoleConnected` entry shows the LAN IP of machine B (not `127.0.0.1`) |

**Pass / Fail:** ____  **Tester:** ____  **Date:** ____  **OS engine / OS client:** ____

## Out of scope

- TLS / authentication on the v3 socket — deferred to v3.x hardening.
- Restore commands from the remote console — only Pause / Play / Stop in
  scope for this recette.
- Performance under > 10 concurrent clients — the cahier expects a
  reasonable operator count, not a load test.

## Sign-off

| Tester | Engine OS | Client OS | All 7 scenarios pass? | Date |
|---|---|---|---|---|
| ____ | ____ | ____ | ☐ Yes ☐ No | ____ |

If any scenario fails, file an issue with the scenario number, the screenshot,
and the relevant excerpt from the daily log + `state.json`. Tag the issue with
`recette-v3` and the suspected zone (`dev3-state`, `dev4-cli`).
