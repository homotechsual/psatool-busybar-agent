# Gorelo PSA Provider Design

## Context

`psatool-busybar-agent` was built around `IPsaDataProvider` as a pluggable seam from the start
(see [`2026-07-31-psatool-busybar-agent-design.md`](2026-07-31-psatool-busybar-agent-design.md)),
with Halo as the only v1 implementation. This is the second provider: [Gorelo](https://gorelo.io),
a newer PSA whose public API ([swagger](https://api.aue.gorelo.io/swagger/index.html)) is
considerably thinner than Halo's — no server-side ticket filtering, no VIP concept, and only a
single, narrowly-scoped SLA timer. This document records the mapping decisions made to fit that
thinner API into the existing provider-agnostic `PsaSnapshot` contract, and where those decisions
are best-effort assumptions rather than documented API behavior.

## Goal

A `GoreloPsaDataProvider : IPsaDataProvider`, selectable via `Psa:Provider=Gorelo`, that polls
Gorelo's REST API and produces a `PsaSnapshot` on the same contract Halo already satisfies — no
changes to `PsaSnapshot`, `PriorityEngine`, or the display layer.

## API Surface Used

- **Auth**: static `X-API-Key` header (global security scheme) — no token exchange, no refresh.
- **`GET /v1/tickets`**: cursor-paginated (`cursor`, `pageSize`, `sortBy`, `sortOrder`). No
  status/priority/client filter parameters exist in the spec, so every poll pages through the
  *entire* ticket set (open and closed) and filters client-side. Confirmed acceptable for the
  target tenant size (under ~2,000 total tickets); a larger tenant would need a different
  strategy (e.g. `sortBy=updatedOn` + early-stop heuristics), out of scope here.
- Ticket fields used: `id`, `displayNumber`, `priority` (`{id, name}`), `leadAssigneeId`,
  `closedOn`, `sla.firstResponse.minutes`.

## Components

- **`GoreloOptions`** (`Psa:Gorelo` section) — `BaseUrl` (required, e.g.
  `https://api.aue.gorelo.io/`), `ApiKey` (required, secret), `PageSize` (default 200 — the spec
  documents no upper bound, but an unbounded default would be reckless).

- **`GoreloApiModels`** — `GoreloTicketsResponse` (`data`, `nextCursor`, `hasMore`),
  `GoreloTicket`, `GoreloCodeModel` (`{id, name}`, used for `priority`), `GoreloSlaModel` /
  `GoreloSlaFirstResponseModel`.

- **`GoreloPsaDataProvider`** — no separate auth-client class (unlike Halo's `HaloAuthClient`):
  the API key is a static value set once as a `DefaultRequestHeaders` entry on the typed
  `HttpClient` at DI registration, so there's no token to cache or refresh. `GetSnapshotAsync`
  pages through `GET /v1/tickets` (`cursor` → `nextCursor`) until `hasMore` is false, then calls
  an internal static `MapSnapshot(...)` — mirroring `HaloPsaDataProvider`'s split so the mapping
  logic is unit-testable without HTTP.

## Data Mapping Decisions

- **Active/open ticket** = `closedOn == null`. Gorelo has no server-side open filter and
  `PublicStatusListItemModel.baseStatusId`'s meaning isn't documented, so `closedOn` is the one
  reliable, tenant-customization-proof signal.

- **Priority rank** — Gorelo priority is a tenant-configurable `{id, name}` pair (confirmed:
  tenants run either a 2-level `Urgent/Normal` or 5-level `Urgent/High/Normal/Low/None` scheme),
  and `priority.id` alone carries no guarantee of matching `PsaSnapshot`'s "1 = most urgent"
  contract. `PriorityEngine` hardcodes rank 1 as the CRITICAL trigger, so getting this wrong
  would misfire the display. `GoreloPsaDataProvider` instead maps by name, case-insensitively,
  against a fixed table covering both known schemes:

  | Name | Rank |
  | --- | --- |
  | Urgent | 1 |
  | High | 2 |
  | Normal | 3 |
  | Low | 4 |
  | None | 5 |

  A ticket whose priority name isn't in this table is excluded from `PriorityCounts`/
  `PriorityNames` (still counted in `OpenTicketCount`) and logged once per unrecognized name
  (not per ticket) so a future/renamed tier doesn't spam logs. A 2-level tenant simply never
  populates ranks 2/4/5 — harmless, since `PriorityEngine` only reads ranks 1–3 and treats a
  missing key as zero.

- **Unassigned** = `leadAssigneeId == null` — cleaner than Halo's magic `agent_id == 0`
  convention, since Gorelo's field is genuinely nullable.

- **SLA risk** — `sla.firstResponse.minutes` is fed directly into `SlaRiskTicket.MinutesRemaining`
  (already minutes; no hours conversion like Halo's `slatimeleft`) for tickets where it has a
  value. **Unverified assumption**: the spec has no description text for this field, so "minutes
  remaining until first-response SLA breach" (negative once breached) is inferred from the field
  name and Halo's analogous field, not confirmed API behavior. It also only ever reflects the
  first-response timer — Gorelo's API exposes no overall resolution-SLA timer the way Halo does.
  Flagged in code comments as needing verification against a live tenant, same as Halo's own
  tenant-specific assumptions (e.g. its unassigned-agent convention).

- **VIP** — `PsaSnapshot.VipTickets` is always empty for Gorelo. Neither the ticket nor client
  schema exposes any VIP/tier concept in this API version. Documented as a known gap rather than
  approximated (e.g. via a configured tag ID) — revisit if Gorelo's API grows this concept, or if
  a tag-based approximation becomes worth the config surface it'd add.

- **Organization name** — always null. Gorelo's API has no organization-profile/portal-name
  endpoint (only `/v1/organization/users` and `/v1/organization/groups`, neither relevant). Falls
  back to `Dashboard:HeaderText`, a path `HaloPsaDataProvider` already exercises on lookup failure.

## Configuration

New keys under `Psa:Gorelo`:

- `Psa:Gorelo:BaseUrl` — required, e.g. `https://api.aue.gorelo.io/` (Gorelo publishes
  region-specific base URLs; AU is the one referenced during this design).
- `Psa:Gorelo:ApiKey` — required, secret (env-var/user-secrets only, never committed).
- `Psa:Gorelo:PageSize` — default 200.

`Program.cs` gains a `Provider=Gorelo` branch registering `IPsaDataProvider` → `GoreloPsaDataProvider`
with `BaseAddress` and the `X-API-Key` default header, alongside the existing Halo branch — the
`if/else` becomes a proper branch-per-provider dispatch rather than a Halo-only `if`/throw.

## Testing

- `GoreloPsaDataProviderTests` mirroring `HaloPsaDataProviderTests`: pure `MapSnapshot(...)` unit
  tests covering priority-name mapping (including an unrecognized name), unassigned detection,
  closed-ticket exclusion, and SLA breach (negative `MinutesRemaining`) cases; plus an HTTP-level
  test using the existing `FakeHttpMessageHandler` covering multi-page cursor stitching
  (`hasMore`/`nextCursor` loop termination).
- No live-Gorelo integration test in the automated suite, matching the existing Halo policy.

## Out of Scope

- Approximating VIP via a configured tag ID (documented gap instead, see above).
- Large-tenant pagination strategies (early-stop heuristics, incremental sync) — current design
  assumes the full ticket set is small enough to page through every poll.
- Verifying the `sla.firstResponse.minutes` semantics or the `baseStatusId` taxonomy against a
  live Gorelo tenant — both are called out as assumptions to confirm once real credentials/data
  are available, not blocking this design.
