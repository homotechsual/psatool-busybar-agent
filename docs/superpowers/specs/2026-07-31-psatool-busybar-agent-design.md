# psatool-busybar-agent Design

## Context

This is the second of two sub-projects (the first, [`busybar-dotnet`](https://github.com/homotechsual/busybar-dotnet),
is a released, NuGet-published typed .NET client for the BUSY Bar HTTP API). This project is a
.NET Worker Service that polls a PSA (initially Halo, others pluggable later) and drives a
physical BUSY Bar display with a priority-ranked helpdesk dashboard.

The original request specified a Python/asyncio/pydantic service. Partway through the prior
session, the decision to release the BusyBar client as a standalone, independently-usable .NET
library implied this worker would consume it directly via .NET project/package referencing —
so this project's stack pivoted to .NET as well, to consume `BusyBar` as a normal NuGet
dependency rather than needing a separate bridge between two languages.

The project is named `psatool-busybar-agent`, not `halo-busybar-agent`, because Halo is the
first of what should be a pluggable set of PSA data sources, not a permanent hard dependency.

## Goal

A Docker-deployed .NET Worker Service that, every 60 seconds, polls the configured PSA provider
for ticket state, determines the current dashboard priority, and renders it to a BUSY Bar over
the network via the `BusyBar` NuGet package.

## Architecture

```
[Timer: 60s] -> IPsaDataProvider.GetSnapshotAsync()
             -> PsaSnapshot (open/priority counts, SLA-risk tickets, unassigned, VIP)
             -> PriorityEngine (pure function: PsaSnapshot -> DashboardState)
             -> BusyBarRenderer (DashboardState -> BusyBar.DisplayDrawAsync)
```

### Components

- **`IPsaDataProvider`** — the pluggable-provider seam: `Task<PsaSnapshot> GetSnapshotAsync(CancellationToken)`.
  Implementations are registered in DI; `appsettings.json`'s `Provider` setting (e.g. `"Halo"`)
  selects which one is active at startup. No runtime plugin loading (no external DLL discovery) —
  a new provider ships as a new class in this codebase and a DI registration. This keeps the
  pluggability real (swapping providers is "implement one interface," not "rewrite the worker")
  without building infrastructure (assembly isolation, plugin manifests) nothing currently needs.

- **`HaloPsaDataProvider`** — the only built-in provider for v1. Authenticates to Halo's REST API
  via OAuth2 client credentials (client_id/client_secret from config/environment, exchanged for a
  bearer token and refreshed as needed). Queries whatever Halo endpoints/fields are needed to
  populate a `PsaSnapshot` — the exact API calls are determined at implementation time against a
  real Halo instance, not specified here.

- **`PsaSnapshot`** — the provider-agnostic data contract: open ticket count, priority counts,
  SLA-risk tickets (tickets breaching SLA within a configurable threshold, default 60 minutes),
  unassigned ticket count, and VIP customer tickets (open tickets belonging to a customer flagged
  VIP in Halo) — this same VIP ticket list is what the priority engine calls "VIP escalations":
  there's no separate escalation concept, an open VIP customer ticket *is* the escalation.

- **`PriorityEngine`** — pure function, `PsaSnapshot -> DashboardState`. Implements the 5-level
  priority order from the original spec, evaluated top to bottom (first match wins):
  1. P1 tickets present → CRITICAL mode
  2. SLA risk tickets (breaching within 60 min) present → SLA WARNING mode
  3. VIP customer tickets present → CRITICAL mode (VIP variant)
  4. Unassigned tickets present → NORMAL mode with an unassigned-count callout
  5. Otherwise → NORMAL mode, standard dashboard

  This is the unit-testable core the original spec called out explicitly — no I/O, no BUSY Bar
  or Halo dependency, just data in, `DashboardState` out.

- **`DashboardState`** — which display mode is active, plus the specific values to render
  (ticket IDs, counts, remaining SLA minutes).

- **`BusyBarRenderer`** — `DashboardState -> DisplayDrawAsync` calls, one method per display mode,
  using the exact text templates from the original spec:

  ```
  NORMAL:
  WRC SERVICE DESK
  P1:x P2:y
  SLA: OK

  SLA WARNING:
  SLA RISK
  Ticket #id
  Xm REMAIN

  CRITICAL:
  CRITICAL
  P1 OPEN
  Count:x
  ```

- **`PollingBackgroundService`** — a .NET `BackgroundService` driving the 60-second loop. A
  failed poll (Halo unreachable, auth failure, BUSY Bar unreachable) is logged and the service
  waits for the next cycle rather than crashing — a background dashboard tool should degrade
  gracefully, not require manual restarts for a transient network blip.

## Tech Stack

- .NET 10, `Microsoft.Extensions.Hosting` Worker Service template
- `BusyBar` consumed as the **published NuGet package** (not a local project reference) —
  dogfooding the same dependency path any other consumer would use
- `appsettings.json` / `IConfiguration` with strongly-typed options classes; environment variable
  overrides for secrets (Halo client_id/secret) and per-environment values (BUSY Bar address)
- `Microsoft.Extensions.Logging` for structured logs (no extra logging framework dependency)
- xUnit for tests, mirroring `busybar-dotnet`'s conventions

## Deployment

Dockerfile + docker-compose.yml, matching the original spec's requirement.

### Least privilege

- **Halo OAuth client**: scoped read-only — ticket and SLA data access only. No write, admin, or
  configuration scopes. Document the exact minimum scope set once confirmed against a real Halo
  API client registration.
- **Container**: runs as a non-root user, no unnecessary Linux capabilities, read-only root
  filesystem where practical (the service has no need to write to disk beyond logs, which go to
  stdout/stderr per container logging convention).

### Docker networking to the BUSY Bar

The BUSY Bar presents as a USB-Ethernet adapter with its own IP (e.g. `10.0.4.20`) — this is a
network-reachability concern, not a USB-device-passthrough one. Documented guidance will cover:

- **Linux Docker host**: `network_mode: host` in docker-compose is simplest — the container
  shares the host's network stack and reaches the BUSY Bar's IP exactly as the host does.
- **Windows (Docker Desktop / WSL2)**: host networking does not expose host-only adapters into
  the WSL2 VM automatically. Guidance will cover the working options for this case (e.g. bridging
  the USB-Ethernet adapter's subnet to WSL2's network, or running the container with an explicit
  route/port-forward) once verified against a real Windows deployment.

## Configuration

`appsettings.json` (with environment-specific overrides and env-var secret overrides):

- `Psa:Provider` — which `IPsaDataProvider` implementation to use (`"Halo"` for v1)
- `Psa:Halo:ClientId` / `Psa:Halo:ClientSecret` — OAuth2 client credentials (secrets, env-var only)
- `Psa:Halo:BaseUrl` — the Halo instance's API base URL
- `Psa:PollIntervalSeconds` — default 60
- `Psa:SlaRiskThresholdMinutes` — default 60
- `BusyBar:Address` — the device's network address, default `10.0.4.20`

## Testing

- xUnit tests for `PriorityEngine` covering all 5 priority levels and their precedence ordering
  (the specific scenario the original spec called for)
- `HaloPsaDataProvider` tests against mocked HTTP responses (same `FakeHttpMessageHandler`-style
  pattern used in `busybar-dotnet`)
- No live-device or live-Halo integration tests in the automated suite — those require real
  credentials/hardware and are exercised manually, mirroring `busybar-dotnet`'s
  `samples/LiveDeviceTest` approach

## Docs Site

Public, same pattern as `busybar-dotnet`: Docusaurus, deployed to Cloudflare Pages at
`psatool-busybar-agent.homotechsual.dev`. Contents:

- **API reference**: auto-generated from XML doc comments via the same `xmldoc2md` pipeline
  used in `busybar-dotnet` (`scripts/generate-api-docs.ps1`, adapted).
- **Developer docs**: architecture overview (this document's structure, prose-ified), a
  provider plugin guide (how to implement a new `IPsaDataProvider`), deployment guide (Docker,
  networking, least-privilege), and a configuration reference.

## Out of Scope for v1

- Multiple BUSY Bar devices (single device, config-driven address)
- Runtime/external plugin loading (config-driven provider selection only)
- A second PSA provider implementation (the interface exists to make this possible later; no
  second implementation ships now)
