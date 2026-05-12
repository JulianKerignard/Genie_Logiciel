# LogCentralizer — Docker admin manual

EasySave v3 centralizes daily logs into one file regardless of how many
workstations run the backup engine. The cahier des charges requires:

> Un seul et unique fichier journalier quel que soit le nombre
> d'utilisateurs.

`LogCentralizer` is the ASP.NET Core 8 minimal API service that fulfills
that requirement. EasySave instances (console + GUI) ship a `LogShipper`
that POSTs each `LogEntry` to this service over HTTP; the service drains
an in-memory `Channel<LogEntry>` from a single background writer task
and appends one line per entry to `{LogsDir}/yyyy-MM-dd.jsonl`.

This document covers what an operator needs to deploy, monitor and
troubleshoot the service in production.

## Topology

```
┌──────────────────┐   HTTP POST /logs   ┌──────────────────────┐
│  EasySave (WS-1) ├────────────────────►│                      │
└──────────────────┘                     │                      │
┌──────────────────┐                     │   LogCentralizer     │
│  EasySave (WS-2) ├────────────────────►│      (container)     │
└──────────────────┘                     │                      │     ┌────────┐
┌──────────────────┐                     │   /logs  /health     ├────►│ logs/  │
│  EasySave (WS-N) ├────────────────────►│                      │     │ *.jsonl│
└──────────────────┘                     └──────────────────────┘     └────────┘
                                                                       (host volume)
```

- One container per site / business day. **Do not scale replicas** —
  concurrent appends from two writer processes would interleave at the
  byte level. If you need redundancy, run an active-passive pair behind
  a TCP load balancer that fails over at the LB layer.
- The daily file is on a host bind-mount (`./logs/`) so it survives
  container restarts and can be archived with the operator's existing
  tooling.

## Quick start

From the repo root:

```bash
docker compose up -d                        # build + start
curl http://localhost:8080/health           # smoke test
docker compose logs -f log-centralizer      # tail server logs
ls logs/                                    # see today's *.jsonl
docker compose down                         # stop
```

The compose stack does the build on first run; subsequent `up`s reuse
the cached image.

## Configuration

### Ports

| Port | Direction | Purpose |
| --- | --- | --- |
| `8080/tcp` | host ← LB / EasySave clients | `POST /logs` and `GET /health` |

The container always listens on `8080` inside the network namespace
(`ASPNETCORE_URLS=http://+:8080` in the Dockerfile). Change the **host**
port in `docker-compose.yml` if `8080` is already taken on your host:

```yaml
ports:
  - "9100:8080"        # host 9100 → container 8080
```

### Volumes

| Host path | Container path | Owner inside container | Purpose |
| --- | --- | --- | --- |
| `./logs/` | `/var/log/easysave/` | `app` (UID 1654) | Daily JSON Lines file |

**Linux only.** The container runs as the non-root `app` user
(UID 1654 in the .NET 8 base image). On Linux hosts the bind-mounted
host directory must be writable by that UID, otherwise the background
writer faults and `POST /logs` returns `503`. Either:

```bash
sudo chown 1654:1654 ./logs            # recommended — exact UID match
# or
chmod 0777 ./logs                      # looser — works without knowing the UID
```

Docker Desktop on macOS and Windows translates UIDs transparently — no
chmod / chown needed there.

### Centralized logging mode on the client side

Each EasySave workstation must opt in via `appsettings.json`:

```json
{
  "log_mode": "Centralized",
  "log_centralized_endpoint": "http://logs.internal:8080/logs"
}
```

- `Local` (default) — daily file on the workstation only. Unchanged
  from v1 / v2.
- `Centralized` — POST every entry to the endpoint, skip the local
  file. The collector is the single source of truth.
- `Both` — POST every entry AND keep writing the local file. Useful
  during the cut-over so operators can fall back to the workstation
  file if the collector misbehaves.

The `LogShipper` buffers in memory and retries with exponential backoff
(`1s, 2s, 5s, 10s`, capped at `30s`) — a brief LogCentralizer outage
does not lose entries as long as the EasySave process keeps running.
A host crash with entries still buffered is a known loss window;
operators run `Both` during cut-over for that exact reason.

## Healthcheck

The container declares a `HEALTHCHECK` that hits `/health` every 30 s
using `curl`. `docker ps` reports `(healthy)` once three probes have
succeeded inside the `start_period` window (10 s).

| Probe | Means |
| --- | --- |
| `GET /health` → `200 OK` `{"status":"ok"}` | Web host is alive. |
| `GET /health` → no response / connection refused | Container has crashed or has not yet finished booting. |

**`/health` does NOT prove the writer task is alive.** That is by
design — `/health` is cheap (no disk I/O) so a slow filesystem cannot
flap the probe. The writer death signal is separate (see next section).

## Detecting writer faults

A faulted writer (disk full, bind-mount permission error, ...) does NOT
crash the host. Instead:

| Signal | Means |
| --- | --- |
| `GET /health` → `200` | Host is alive (probe says nothing about disk). |
| `POST /logs` → `503 Service Unavailable` | Writer is dead; no entry can be persisted. |
| No new lines in `./logs/yyyy-MM-dd.jsonl` | Writer is dead OR no client is shipping. |

Operators should alert on `503` responses or on a flat-line in the
file's `mtime`. Both signals appear together.

## Retention

The service does not rotate or prune log files. Each day produces
one `yyyy-MM-dd.jsonl` and that file grows for the duration of that
business day. Choose one of:

### A — keep everything (default)

No action needed. The host filesystem holds the entire history. Plan
disk capacity around the average daily volume × retention requirement.

### B — host-side cron

Add a daily cron on the docker host:

```bash
# /etc/cron.daily/easysave-logs-cleanup
find /opt/easysave/logs -name "*.jsonl" -mtime +90 -delete
```

90 days is a good default — adjust to match your audit policy.

### C — log shipping pipeline

If the workstation cluster already feeds a SIEM / ELK / Loki, point a
log shipper at `./logs/` and treat the bind-mount as the buffer. The
shipper handles retention; the bind-mount only needs to hold the
tail.

## Troubleshooting

### Container reports `(unhealthy)`

1. `docker compose logs log-centralizer --tail 100` — look for an
   exception during host startup (port binding, missing
   `appsettings.json`, etc.).
2. `docker compose exec log-centralizer curl -fsS http://127.0.0.1:8080/health`
   — does the container itself see `/health`? If yes, the host-side
   port mapping is wrong (`docker compose ps` will show the wrong
   port).
3. If `/health` is reachable but the container still goes unhealthy
   intermittently, raise `interval` and `timeout` in
   `docker-compose.yml`.

### Clients get `503` on every POST but `/health` is `200`

The writer background task has faulted permanently. Recover with:

1. `docker compose logs log-centralizer | grep -i exception` — find
   the root cause. Most common on Linux: `UnauthorizedAccessException`
   from the bind-mount permission issue described in **Volumes**.
2. Fix the underlying cause (chown the host dir, free disk space, …).
3. `docker compose restart log-centralizer` — the writer starts
   afresh, the channel reopens, subsequent POSTs return `204` again.

Clients that hold their entries in the LogShipper buffer recover
without further action — the next successful POST drains the queue
in FIFO order.

### Clients get `connection refused`

The container is not running or the host port is wrong.

```bash
docker compose ps                    # state column should be `running`
docker compose port log-centralizer 8080
```

### Daily file has duplicate / interleaved bytes

You scaled the service to more than one replica. **Stop one of them.**
The cahier-mandated "single daily file" maps to a single writer
process; concurrent appends from two processes are byte-interleaved
at the OS level — JSON Lines parsers will see a stream of
half-rows.

## Validating the image before a release tag

The Testcontainers-backed e2e suite under `tests/LogCentralizer.Tests/`
builds the actual image from `LogCentralizer/Dockerfile`, starts a
container with a host bind-mount, fires three simulated workstations
in parallel against the live HTTP surface and asserts the single-file
contract on the volume.

CI does **not** run this suite (the in-process suite already covers
the functional contract, and Testcontainers on the GitHub-hosted Linux
runner has consistently hung on the bind-mount UID issue). Run it on
a dev workstation with Docker Desktop or a Linux daemon **before
tagging a release**:

```bash
# from the repo root
docker info >/dev/null 2>&1 || (echo "Start Docker first" && exit 1)
dotnet test tests/LogCentralizer.Tests/LogCentralizer.Tests.csproj \
    --filter "FullyQualifiedName~LogCentralizerE2ETests" \
    -c Release
```

Expected: 2 tests pass, ~30 s with the image cached, ~90 s on a cold
build. Tests marked `Skipped` mean the daemon was unreachable — start
it and retry.
