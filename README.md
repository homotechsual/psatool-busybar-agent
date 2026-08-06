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
| `Psa:Halo:Scope` | `read:tickets` | OAuth2 scope requested. **Set this to the minimum your Halo API client is actually granted** — see Least Privilege below. |
| `Psa:Halo:OrganisationId` | `1` | Halo Organisation whose `portal_title` is used as the dashboard header (fetched once at startup, cached for the process lifetime). Falls back to `Dashboard:HeaderText` if the fetch fails or the org has no portal title set. |
| `Psa:Gorelo:BaseUrl` | — | Your Gorelo tenant's regional API base URL (e.g. `https://api.aue.gorelo.io/` for Australia — see [Gorelo's API docs](https://help.gorelo.io/api-overview) for other regions). |
| `Psa:Gorelo:ApiKey` | — | Gorelo API key, sent as the `X-API-Key` header — a secret, set via env/user-secrets. |
| `Psa:Gorelo:PageSize` | `200` | Tickets requested per page when paginating `GET /v1/tickets`. Gorelo's API has no ticket-status filter, so every poll pages through the *entire* ticket set (open and closed) client-side; as implemented this is only suitable for smaller tenants (roughly under 2,000 total tickets). |
| `BusyBar:Address` | `10.0.4.20` | Network address of the BUSY Bar device. |
| `Dashboard:HeaderText` | `WRC SERVICE DESK` | First line of the NORMAL-mode display, used only when the Halo organisation lookup (`Psa:Halo:OrganisationId`) doesn't produce a header. |
| `Dashboard:DisplayCycleSeconds` | `5` | How often the CRITICAL display cycles between its P1/P2/P3 pages. Has no effect on NORMAL or SLA WARNING, which have only one page each. |

## Least privilege

- **Halo API client**: register a dedicated Halo API client (Configuration → Integrations → Halo
  API in Halo's admin UI) scoped to read-only ticket/SLA access only. Do not reuse an
  admin-scoped client, and confirm `Psa:Halo:Scope` matches the specific read-only scope your
  Halo tenant exposes for ticket data — the shipped default (`read:tickets`) is a reasonable
  starting point, but you must verify a scope of that name actually exists and is granted to your
  API client in your tenant.
- **Container**: the image runs as a non-root user (`app`, the built-in .NET container user)
  with no extra capabilities granted. `docker-compose.yml` additionally drops all Linux
  capabilities (`cap_drop: ALL`), sets `no-new-privileges`, and mounts the root filesystem
  read-only with a `tmpfs` `/tmp`. Don't add `privileged: true` or extra `cap_add` entries to
  `docker-compose.yml` — nothing here needs them.
- **Gorelo API key**: Gorelo's public API uses a single static key with no documented scoping —
  unlike Halo's OAuth client, there's no token expiry to limit an exposure window if it leaks.
  Use a dedicated key if Gorelo's admin UI supports issuing one, and rotate it immediately if ever
  exposed.

## Gorelo provider notes

Gorelo's public API is considerably thinner than Halo's, which shapes a few `PsaSnapshot` fields
when `Psa:Provider=Gorelo`:

- **`VipTickets` is always empty** — Gorelo's ticket/client schema has no VIP or customer-tier
  concept.
- **`OrganizationName` is always null** — Gorelo has no organization-profile/portal-name endpoint,
  so the dashboard always falls back to `Dashboard:HeaderText`.
- **SLA risk reflects first-response only** — Gorelo exposes a single `sla.firstResponse.minutes`
  timer (no overall resolution-SLA timer like Halo's). Its exact semantics aren't documented in
  Gorelo's API spec; treated as "minutes remaining until first-response breach" by inference, not
  confirmed behavior.
- **On-hold/paused tickets are not excluded** — unlike Halo (which excludes on-hold tickets from
  every count so a paused ticket can't drive a P1/SLA/VIP/unassigned signal), Gorelo has no
  reliable on-hold/paused signal in its ticket schema: its `baseStatusId` taxonomy is undocumented
  and tenant-customizable, so it isn't safe to key filtering off. A paused Gorelo ticket therefore
  still counts toward `OpenTicketCount`, `PriorityCounts`, SLA risk, and unassigned counts.

See [`docs/superpowers/specs/2026-08-06-gorelo-psa-provider-design.md`](docs/superpowers/specs/2026-08-06-gorelo-psa-provider-design.md)
for the full rationale behind these decisions.

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

  Verify actual reachability once deployed with: `curl -sf http://<BusyBar address>/api/version`.
  Run this from the Docker host itself (or any machine on the same network as the BUSY Bar), not
  from inside the container — the runtime image ships no `curl`.

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
