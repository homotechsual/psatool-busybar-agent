# psatool-busybar-agent

A .NET Worker Service that polls a pluggable PSA provider (Halo first) and drives a physical
BUSY Bar with a priority-ranked helpdesk dashboard. Consumes the
[`BusyBar`](https://www.nuget.org/packages/BusyBar) NuGet package.

## Configuration

Set via `appsettings.json`, environment variables (double-underscore nesting, e.g.
`Psa__Halo__ClientId`), or `dotnet user-secrets` for local development. Never commit real
`ClientId`/`ClientSecret` values.

| Key | Default | Description |
| --- | --- | --- |
| `Psa:Provider` | `Halo` | Which `IPsaDataProvider` implementation to use. |
| `Psa:PollIntervalSeconds` | `60` | How often to poll the PSA and refresh the display. |
| `Psa:SlaRiskThresholdMinutes` | `60` | A ticket is "SLA-risk" at or below this many minutes to breach. |
| `Psa:Halo:BaseUrl` | — | Your Halo tenant's API base URL. |
| `Psa:Halo:ClientId` / `ClientSecret` | — | OAuth2 client-credentials — secrets, set via env/user-secrets. |
| `Psa:Halo:Scope` | `all` | OAuth2 scope requested. **Set this to the minimum your Halo API client is actually granted** — see Least Privilege below. |
| `BusyBar:Address` | `10.0.4.20` | Network address of the BUSY Bar device. |
| `Dashboard:HeaderText` | `WRC SERVICE DESK` | First line of the NORMAL-mode display. |

## Least privilege

- **Halo API client**: register a dedicated Halo API client (Configuration → Integrations → Halo
  API in Halo's admin UI) scoped to read-only ticket/SLA access only. Do not reuse an
  admin-scoped client, and do not leave `Psa:Halo:Scope` at its `all` default in production —
  set it to the specific read scope your Halo tenant exposes for ticket data.
- **Container**: the image runs as a non-root user (`app`, the built-in .NET container user)
  with no extra capabilities granted. Don't add `privileged: true` or extra `cap_add` entries to
  `docker-compose.yml` — nothing here needs them.

## Docker networking

The BUSY Bar presents as a USB-Ethernet adapter with its own IP (e.g. `10.0.4.20`) — reaching it
from a container is a network-routing question, not a USB-passthrough one.

- **Linux Docker host**: `network_mode: host` (already set in `docker-compose.yml`) is simplest —
  the container shares the host's network stack and reaches the BUSY Bar exactly as the host does.
- **Windows (Docker Desktop / WSL2)**: `network_mode: host` does **not** expose host-only adapters
  (like the BUSY Bar's USB-Ethernet interface) into the WSL2 VM automatically — WSL2 has its own
  network namespace. Two working options:
  1. **Bridge the adapter into WSL2**: share the BUSY Bar's USB-Ethernet adapter with the WSL2
     network via `wsl --shutdown` + a `.wslconfig` `[wsl2] networkingMode=mirrored` setting
     (Windows 11 22H2+), which mirrors host network interfaces (including the USB-Ethernet one)
     into WSL2 directly — then `network_mode: host` works as on Linux.
  2. **Run the worker directly on Windows instead of in Docker** for the BUSY Bar network hop —
     not using Docker at all is simpler than fighting WSL2 networking if mirrored mode isn't
     available on your Windows build.

  Verify actual reachability once deployed with: `docker exec psatool-busybar-agent curl -sf http://<BusyBar address>/api/version`.

## Development

```bash
dotnet build PsaToolAgent.sln
dotnet test PsaToolAgent.sln
```

## Deployment

```bash
cp .env.example .env   # fill in HALO_CLIENT_ID, HALO_CLIENT_SECRET, HALO_BASE_URL
docker compose up -d --build
```
