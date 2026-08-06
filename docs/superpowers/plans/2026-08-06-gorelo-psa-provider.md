# Gorelo PSA Provider Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `GoreloPsaDataProvider : IPsaDataProvider`, selectable via `Psa:Provider=Gorelo`, so the agent can poll a Gorelo PSA tenant on the same `PsaSnapshot` contract Halo already satisfies.

**Architecture:** New files under `src/PsaToolAgent/Psa/Gorelo/` (`GoreloOptions`, `GoreloApiModels`, `GoreloPsaDataProvider`) mirroring the existing `Psa/Halo/` layout. `GoreloPsaDataProvider` splits into a pure, unit-testable `MapSnapshot(...)` (raw tickets → `PsaSnapshot`) and an instance `GetSnapshotAsync` that pages through `GET /v1/tickets` via cursor and calls it — same split `HaloPsaDataProvider` uses. Auth is a static `X-API-Key` header set once at DI registration (no token client needed, unlike Halo).

**Tech Stack:** .NET 10, `System.Text.Json` (`ReadFromJsonAsync`), xUnit + the existing `FakeHttpMessageHandler` test double. No new package dependencies.

## Global Constraints

- Target framework: `net10.0` (matches existing `.csproj` files) — do not change `TargetFramework`.
- No new NuGet packages — everything needed (`System.Net.Http.Json`, `Microsoft.Extensions.Options.DataAnnotations`, `xunit`) is already referenced by the existing projects.
- Follow the spec's data-mapping decisions exactly (see [`2026-08-06-gorelo-psa-provider-design.md`](../specs/2026-08-06-gorelo-psa-provider-design.md)): `closedOn == null` for open, priority mapped by **name** against the fixed `Urgent=1/High=2/Normal=3/Low=4/None=5` table (not by `priority.id`), unassigned = `leadAssigneeId == null`, `sla.firstResponse.minutes` fed straight into `SlaRiskTicket.MinutesRemaining`, `VipTickets` always empty, `OrganizationName` always null.
- Never commit a real Gorelo API key. Test code uses literal placeholder values only (e.g. `"test-key"`).

---

### Task 1: Gorelo API models and pure `MapSnapshot` mapping

**Files:**
- Create: `src/PsaToolAgent/Psa/Gorelo/GoreloApiModels.cs`
- Create: `src/PsaToolAgent/Psa/Gorelo/GoreloPsaDataProvider.cs`
- Test: `test/PsaToolAgent.Tests/Psa/Gorelo/GoreloPsaDataProviderTests.cs`

**Interfaces:**
- Consumes: `PsaToolAgent.Psa.PsaSnapshot`, `PsaToolAgent.Psa.SlaRiskTicket`, `PsaToolAgent.Psa.VipTicket` (all in the parent `PsaToolAgent.Psa` namespace — no `using` needed, same as `HaloPsaDataProvider`, because C#'s dotted namespace declarations nest for lookup purposes).
- Produces: `GoreloTicket(string Id, string? DisplayNumber, long? LeadAssigneeId, DateTimeOffset? ClosedOn, GoreloCodeModel? Priority, GoreloSlaModel? Sla)`, `GoreloCodeModel(long? Id, string? Name)`, `GoreloSlaModel(GoreloSlaFirstResponseModel? FirstResponse)`, `GoreloSlaFirstResponseModel(double? Minutes)`, `GoreloTicketsResponse(IReadOnlyList<GoreloTicket> Data, string? NextCursor, bool HasMore)` — all `internal sealed record`, used by Task 2. `internal static PsaSnapshot GoreloPsaDataProvider.MapSnapshot(IReadOnlyList<GoreloTicket> tickets)` — used by Task 2's `GetSnapshotAsync`.

- [ ] **Step 1: Write the failing tests**

Create `test/PsaToolAgent.Tests/Psa/Gorelo/GoreloPsaDataProviderTests.cs`:

```csharp
using PsaToolAgent.Psa.Gorelo;
using Xunit;

namespace PsaToolAgent.Tests.Psa.Gorelo;

public class GoreloPsaDataProviderTests
{
    [Fact]
    public void MapSnapshot_CountsPriorityByName_UsingTheFixedRankTable()
    {
        var tickets = new[]
        {
            new GoreloTicket("1", "T-1", 3, null, new GoreloCodeModel(10, "Urgent"), null),
            new GoreloTicket("2", "T-2", 3, null, new GoreloCodeModel(10, "Urgent"), null),
            new GoreloTicket("3", "T-3", 3, null, new GoreloCodeModel(20, "Normal"), null)
        };

        var snapshot = GoreloPsaDataProvider.MapSnapshot(tickets);

        Assert.Equal(3, snapshot.OpenTicketCount);
        Assert.Equal(2, snapshot.PriorityCounts[1]);
        Assert.Equal(1, snapshot.PriorityCounts[3]);
        Assert.Equal("Urgent", snapshot.PriorityNames![1]);
        Assert.Equal("Normal", snapshot.PriorityNames![3]);
    }

    [Fact]
    public void MapSnapshot_ExcludesUnrecognizedPriorityName_FromPriorityCountsButNotOpenTicketCount()
    {
        var tickets = new[]
        {
            new GoreloTicket("1", "T-1", 3, null, new GoreloCodeModel(99, "Emergency"), null)
        };

        var snapshot = GoreloPsaDataProvider.MapSnapshot(tickets);

        Assert.Equal(1, snapshot.OpenTicketCount);
        Assert.Empty(snapshot.PriorityCounts);
    }

    [Fact]
    public void MapSnapshot_ExcludesClosedTickets_FromAllCounts()
    {
        var tickets = new[]
        {
            new GoreloTicket("1", "T-1", null, DateTimeOffset.UtcNow, new GoreloCodeModel(10, "Urgent"),
                new GoreloSlaModel(new GoreloSlaFirstResponseModel(5))),
            new GoreloTicket("2", "T-2", 3, null, new GoreloCodeModel(20, "Normal"), null)
        };

        var snapshot = GoreloPsaDataProvider.MapSnapshot(tickets);

        Assert.Equal(1, snapshot.OpenTicketCount);
        Assert.False(snapshot.PriorityCounts.ContainsKey(1));
        Assert.Equal(1, snapshot.PriorityCounts[3]);
        Assert.Empty(snapshot.SlaRiskTickets);
        Assert.Equal(0, snapshot.UnassignedTicketCount);
    }

    [Fact]
    public void MapSnapshot_IdentifiesUnassignedTickets_ByNullLeadAssigneeId()
    {
        var tickets = new[]
        {
            new GoreloTicket("1", "T-1", null, null, new GoreloCodeModel(20, "Normal"), null),
            new GoreloTicket("2", "T-2", 3, null, new GoreloCodeModel(20, "Normal"), null)
        };

        var snapshot = GoreloPsaDataProvider.MapSnapshot(tickets);

        Assert.Equal(1, snapshot.UnassignedTicketCount);
    }

    [Fact]
    public void MapSnapshot_MapsSlaFirstResponseMinutes_IncludingNegativeForBreachedTickets()
    {
        var tickets = new[]
        {
            new GoreloTicket("1", "T-1", 3, null, new GoreloCodeModel(20, "Normal"),
                new GoreloSlaModel(new GoreloSlaFirstResponseModel(30))),
            new GoreloTicket("2", "T-2", 3, null, new GoreloCodeModel(20, "Normal"),
                new GoreloSlaModel(new GoreloSlaFirstResponseModel(-15))),
            new GoreloTicket("3", "T-3", 3, null, new GoreloCodeModel(20, "Normal"), null)
        };

        var snapshot = GoreloPsaDataProvider.MapSnapshot(tickets);

        Assert.Equal(2, snapshot.SlaRiskTickets.Count);
        Assert.Contains(snapshot.SlaRiskTickets, t => t.TicketId == "T-1" && t.MinutesRemaining == 30);
        Assert.Contains(snapshot.SlaRiskTickets, t => t.TicketId == "T-2" && t.MinutesRemaining == -15);
    }

    [Fact]
    public void MapSnapshot_PrefersDisplayNumberOverId_ForTicketIdentifiers()
    {
        var tickets = new[]
        {
            new GoreloTicket("guid-1", "T-42", 3, null, new GoreloCodeModel(20, "Normal"),
                new GoreloSlaModel(new GoreloSlaFirstResponseModel(10)))
        };

        var snapshot = GoreloPsaDataProvider.MapSnapshot(tickets);

        Assert.Equal("T-42", Assert.Single(snapshot.SlaRiskTickets).TicketId);
    }

    [Fact]
    public void MapSnapshot_FallsBackToId_WhenDisplayNumberIsNull()
    {
        var tickets = new[]
        {
            new GoreloTicket("guid-1", null, 3, null, new GoreloCodeModel(20, "Normal"),
                new GoreloSlaModel(new GoreloSlaFirstResponseModel(10)))
        };

        var snapshot = GoreloPsaDataProvider.MapSnapshot(tickets);

        Assert.Equal("guid-1", Assert.Single(snapshot.SlaRiskTickets).TicketId);
    }

    [Fact]
    public void MapSnapshot_AlwaysReturnsEmptyVipTicketsAndNullOrganizationName()
    {
        var tickets = new[]
        {
            new GoreloTicket("1", "T-1", 3, null, new GoreloCodeModel(10, "Urgent"), null)
        };

        var snapshot = GoreloPsaDataProvider.MapSnapshot(tickets);

        Assert.Empty(snapshot.VipTickets);
        Assert.Null(snapshot.OrganizationName);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PsaToolAgent.sln --filter FullyQualifiedName~GoreloPsaDataProviderTests`
Expected: build FAILS — `GoreloTicket`, `GoreloCodeModel`, `GoreloSlaModel`, `GoreloSlaFirstResponseModel`, and `GoreloPsaDataProvider` don't exist yet.

- [ ] **Step 3: Create the API models**

Create `src/PsaToolAgent/Psa/Gorelo/GoreloApiModels.cs`:

```csharp
using System.Text.Json.Serialization;

namespace PsaToolAgent.Psa.Gorelo;

internal sealed record GoreloTicketsResponse(
    [property: JsonPropertyName("data")] IReadOnlyList<GoreloTicket> Data,
    [property: JsonPropertyName("nextCursor")] string? NextCursor,
    [property: JsonPropertyName("hasMore")] bool HasMore);

internal sealed record GoreloTicket(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("displayNumber")] string? DisplayNumber,
    [property: JsonPropertyName("leadAssigneeId")] long? LeadAssigneeId,
    [property: JsonPropertyName("closedOn")] DateTimeOffset? ClosedOn,
    [property: JsonPropertyName("priority")] GoreloCodeModel? Priority,
    [property: JsonPropertyName("sla")] GoreloSlaModel? Sla);

/// <summary>Gorelo's shared "id + name" shape, used here for <see cref="GoreloTicket.Priority"/>.
/// The id is tenant-assigned and carries no guaranteed urgency ordering — see
/// <see cref="GoreloPsaDataProvider.MapSnapshot"/>, which resolves rank from the name instead.</summary>
internal sealed record GoreloCodeModel(
    [property: JsonPropertyName("id")] long? Id,
    [property: JsonPropertyName("name")] string? Name);

internal sealed record GoreloSlaModel(
    [property: JsonPropertyName("firstResponse")] GoreloSlaFirstResponseModel? FirstResponse);

internal sealed record GoreloSlaFirstResponseModel(
    [property: JsonPropertyName("minutes")] double? Minutes);
```

- [ ] **Step 4: Create the provider class with the pure mapping method**

Create `src/PsaToolAgent/Psa/Gorelo/GoreloPsaDataProvider.cs`:

```csharp
namespace PsaToolAgent.Psa.Gorelo;

public sealed class GoreloPsaDataProvider
{
    /// <summary>
    /// Pure mapping from raw Gorelo ticket data (the full paginated set, open and closed) to the
    /// provider-agnostic <see cref="PsaSnapshot"/>. Internal (not private) so tests can exercise
    /// the mapping logic directly, without HTTP.
    ///
    /// A ticket counts as active/open when <see cref="GoreloTicket.ClosedOn"/> is null — Gorelo's
    /// API has no server-side open filter and no documented status taxonomy, so this is the one
    /// tenant-customization-proof signal.
    ///
    /// Priority rank is resolved by name against <see cref="PriorityRankByName"/>, not by
    /// <see cref="GoreloCodeModel.Id"/>: <see cref="PsaSnapshot.PriorityCounts"/>'s "1 = most
    /// urgent" contract has no guaranteed relationship to a tenant-assigned numeric id, and
    /// PriorityEngine hardcodes rank 1 as its CRITICAL trigger, so getting this wrong would
    /// misfire the display. A ticket whose priority name isn't in the table is excluded from
    /// PriorityCounts/PriorityNames (still counted in OpenTicketCount).
    ///
    /// VipTickets is always empty and OrganizationName is always null: neither concept exists in
    /// Gorelo's public API as of this implementation.
    /// </summary>
    internal static PsaSnapshot MapSnapshot(IReadOnlyList<GoreloTicket> tickets)
    {
        var activeTickets = tickets.Where(t => t.ClosedOn is null).ToList();

        var priorityCounts = new Dictionary<int, int>();
        var priorityNames = new Dictionary<int, string>();

        foreach (var ticket in activeTickets)
        {
            var name = ticket.Priority?.Name;
            if (name is null || !PriorityRankByName.TryGetValue(name, out var rank))
            {
                continue;
            }

            priorityCounts[rank] = priorityCounts.GetValueOrDefault(rank) + 1;
            priorityNames[rank] = name;
        }

        var slaRiskTickets = activeTickets
            .Where(t => t.Sla?.FirstResponse?.Minutes is not null)
            .Select(t => new SlaRiskTicket(t.DisplayNumber ?? t.Id, (int)t.Sla!.FirstResponse!.Minutes!.Value))
            .ToList();

        var unassignedCount = activeTickets.Count(t => t.LeadAssigneeId is null);

        return new PsaSnapshot
        {
            OpenTicketCount = activeTickets.Count,
            PriorityCounts = priorityCounts,
            PriorityNames = priorityNames,
            SlaRiskTickets = slaRiskTickets,
            UnassignedTicketCount = unassignedCount,
            VipTickets = Array.Empty<VipTicket>(),
            OrganizationName = null
        };
    }

    /// <summary>Gorelo tenants run one of two fixed priority schemes (2-level Urgent/Normal, or
    /// 5-level Urgent/High/Normal/Low/None) — unlike Halo, priority names aren't arbitrary, so a
    /// fixed table (rather than per-tenant discovery) is sufficient.</summary>
    private static readonly IReadOnlyDictionary<string, int> PriorityRankByName =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Urgent"] = 1,
            ["High"] = 2,
            ["Normal"] = 3,
            ["Low"] = 4,
            ["None"] = 5
        };
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test PsaToolAgent.sln --filter FullyQualifiedName~GoreloPsaDataProviderTests`
Expected: PASS (8 tests).

- [ ] **Step 6: Commit**

```bash
git add src/PsaToolAgent/Psa/Gorelo/GoreloApiModels.cs src/PsaToolAgent/Psa/Gorelo/GoreloPsaDataProvider.cs test/PsaToolAgent.Tests/Psa/Gorelo/GoreloPsaDataProviderTests.cs
git commit -m "Add Gorelo API models and pure ticket-to-snapshot mapping"
```

---

### Task 2: HTTP fetching, pagination, and `IPsaDataProvider` implementation

**Files:**
- Create: `src/PsaToolAgent/Psa/Gorelo/GoreloOptions.cs`
- Modify: `src/PsaToolAgent/Psa/Gorelo/GoreloPsaDataProvider.cs` (replace whole file — adds the class header, fields, constructor, and `GetSnapshotAsync`; `MapSnapshot` and `PriorityRankByName` from Task 1 are unchanged)
- Test: `test/PsaToolAgent.Tests/Psa/Gorelo/GoreloPsaDataProviderTests.cs` (append HTTP-level tests)

**Interfaces:**
- Consumes: `GoreloTicketsResponse`, `GoreloTicket`, `PriorityRankByName` (all from Task 1, same file/class). `PsaToolAgent.Tests.Internal.FakeHttpMessageHandler` (existing test double, `Respond` delegate + `Requests` list — see `test/PsaToolAgent.Tests/Internal/FakeHttpMessageHandler.cs`).
- Produces: `public sealed class GoreloOptions { SectionName = "Psa:Gorelo"; BaseUrl; ApiKey; PageSize }`. `public sealed class GoreloPsaDataProvider : IPsaDataProvider` with constructor `(HttpClient http, IOptions<GoreloOptions> options, ILogger<GoreloPsaDataProvider> logger)` — used by Task 3's DI registration.

- [ ] **Step 1: Write the failing tests**

Append to `test/PsaToolAgent.Tests/Psa/Gorelo/GoreloPsaDataProviderTests.cs` (add these `using`s at the top: `System.Net`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options`, `PsaToolAgent.Tests.Internal`; then add these facts inside the existing class):

```csharp
    [Fact]
    public async Task GetSnapshotAsync_SendsApiKeyHeader_AndMapsASinglePageOfTickets()
    {
        var handler = new FakeHttpMessageHandler
        {
            ResponseBody = "{\"data\":[{\"id\":\"t1\",\"displayNumber\":\"T-1\",\"leadAssigneeId\":3,\"closedOn\":null,\"priority\":{\"id\":10,\"name\":\"Urgent\"},\"sla\":null}],\"nextCursor\":null,\"hasMore\":false}"
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.gorelo.io/") };
        http.DefaultRequestHeaders.Add("X-API-Key", "test-key");
        var options = Options.Create(new GoreloOptions { BaseUrl = "https://example.gorelo.io/", ApiKey = "test-key" });
        var provider = new GoreloPsaDataProvider(http, options, NullLogger<GoreloPsaDataProvider>.Instance);

        var snapshot = await provider.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal(1, snapshot.OpenTicketCount);
        Assert.Equal(1, snapshot.PriorityCounts[1]);
        Assert.Contains(handler.Requests, r => r.Headers.Contains("X-API-Key") && r.Headers.GetValues("X-API-Key").Single() == "test-key");
        Assert.Contains(handler.Requests, r => r.RequestUri!.AbsolutePath.Contains("v1/tickets"));
    }

    [Fact]
    public async Task GetSnapshotAsync_FollowsCursorPagination_UntilHasMoreIsFalse()
    {
        var requestCount = 0;
        var handler = new FakeHttpMessageHandler
        {
            Respond = request =>
            {
                requestCount++;
                if (requestCount == 1)
                {
                    Assert.DoesNotContain("cursor=", request.RequestUri!.Query);
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(
                            "{\"data\":[{\"id\":\"t1\",\"displayNumber\":\"T-1\",\"leadAssigneeId\":3,\"closedOn\":null,\"priority\":{\"id\":10,\"name\":\"Urgent\"},\"sla\":null}],\"nextCursor\":\"abc\",\"hasMore\":true}",
                            Encoding.UTF8, "application/json")
                    };
                }

                Assert.Contains("cursor=abc", request.RequestUri!.Query);
                return new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"data\":[{\"id\":\"t2\",\"displayNumber\":\"T-2\",\"leadAssigneeId\":3,\"closedOn\":null,\"priority\":{\"id\":20,\"name\":\"Normal\"},\"sla\":null}],\"nextCursor\":null,\"hasMore\":false}",
                        Encoding.UTF8, "application/json")
                };
            }
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.gorelo.io/") };
        var options = Options.Create(new GoreloOptions { BaseUrl = "https://example.gorelo.io/", ApiKey = "test-key" });
        var provider = new GoreloPsaDataProvider(http, options, NullLogger<GoreloPsaDataProvider>.Instance);

        var snapshot = await provider.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal(2, snapshot.OpenTicketCount);
        Assert.Equal(2, requestCount);
    }

    [Fact]
    public async Task GetSnapshotAsync_ThrowsRatherThanLoopingForever_WhenHasMoreNeverBecomesFalse()
    {
        var handler = new FakeHttpMessageHandler
        {
            Respond = request => new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"data\":[],\"nextCursor\":\"loop\",\"hasMore\":true}", Encoding.UTF8, "application/json")
            }
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.gorelo.io/") };
        var options = Options.Create(new GoreloOptions { BaseUrl = "https://example.gorelo.io/", ApiKey = "test-key" });
        var provider = new GoreloPsaDataProvider(http, options, NullLogger<GoreloPsaDataProvider>.Instance);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provider.GetSnapshotAsync(CancellationToken.None));
    }
```

Note: this file already has a `System.Text` import need for `Encoding.UTF8` — add `using System.Text;` alongside the other new usings.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test PsaToolAgent.sln --filter FullyQualifiedName~GoreloPsaDataProviderTests`
Expected: build FAILS — `GoreloOptions` doesn't exist, and `GoreloPsaDataProvider` has no constructor taking `(HttpClient, IOptions<GoreloOptions>, ILogger<GoreloPsaDataProvider>)` or `GetSnapshotAsync` method.

- [ ] **Step 3: Create `GoreloOptions`**

Create `src/PsaToolAgent/Psa/Gorelo/GoreloOptions.cs`:

```csharp
using System.ComponentModel.DataAnnotations;

namespace PsaToolAgent.Psa.Gorelo;

public sealed class GoreloOptions
{
    public const string SectionName = "Psa:Gorelo";

    [Required, Url]
    public required string BaseUrl { get; init; }

    /// <summary>Sent as the <c>X-API-Key</c> header on every request — a secret, set via
    /// env/user-secrets, never committed.</summary>
    [Required]
    public required string ApiKey { get; init; }

    /// <summary>Tickets requested per page from <c>GET /v1/tickets</c>. Gorelo's API documents no
    /// upper bound, but an unbounded default would be reckless — 200 keeps each poll to a handful
    /// of requests for a typical (sub-2,000-ticket) tenant.</summary>
    [Range(1, int.MaxValue)]
    public int PageSize { get; init; } = 200;
}
```

- [ ] **Step 4: Replace `GoreloPsaDataProvider.cs` with the full implementation**

Replace the entire contents of `src/PsaToolAgent/Psa/Gorelo/GoreloPsaDataProvider.cs`:

```csharp
using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace PsaToolAgent.Psa.Gorelo;

public sealed class GoreloPsaDataProvider : IPsaDataProvider
{
    /// <summary>Hard ceiling on pages fetched per poll (at the default 200-per-page size, 100
    /// pages is 20,000 tickets — far beyond the sub-2,000-ticket tenant size this provider targets).
    /// Guards against an unbounded fetch loop (e.g. a server bug where <c>hasMore</c> never turns
    /// false) hanging the poll cycle forever; <see cref="PollingBackgroundService"/> catches and
    /// logs the resulting exception, then retries next interval, same as any other poll failure.</summary>
    private const int MaxPages = 100;

    private readonly HttpClient _http;
    private readonly GoreloOptions _options;
    private readonly ILogger<GoreloPsaDataProvider> _logger;
    private readonly HashSet<string> _loggedUnknownPriorityNames = new(StringComparer.OrdinalIgnoreCase);

    public GoreloPsaDataProvider(HttpClient http, IOptions<GoreloOptions> options, ILogger<GoreloPsaDataProvider> logger)
    {
        _http = http;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<PsaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var tickets = new List<GoreloTicket>();
        string? cursor = null;
        var pageCount = 0;
        bool hasMore;

        do
        {
            pageCount++;
            if (pageCount > MaxPages)
            {
                throw new InvalidOperationException(
                    $"Gorelo tickets pagination exceeded {MaxPages} pages without hasMore becoming false — aborting to avoid an unbounded fetch loop.");
            }

            var uri = cursor is null
                ? $"v1/tickets?pageSize={_options.PageSize}"
                : $"v1/tickets?pageSize={_options.PageSize}&cursor={Uri.EscapeDataString(cursor)}";

            using var response = await _http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var body = await response.Content.ReadFromJsonAsync<GoreloTicketsResponse>(cancellationToken: cancellationToken).ConfigureAwait(false)
                ?? throw new InvalidOperationException("Gorelo tickets response was empty.");

            tickets.AddRange(body.Data);
            cursor = body.NextCursor;
            hasMore = body.HasMore;
        } while (hasMore);

        LogUnrecognizedPriorityNames(tickets);

        return MapSnapshot(tickets);
    }

    /// <summary>Logs a warning once per distinct unrecognized priority name seen (not once per
    /// ticket), so a renamed or custom priority tier doesn't spam logs every poll.</summary>
    private void LogUnrecognizedPriorityNames(IReadOnlyList<GoreloTicket> tickets)
    {
        var unknownNames = tickets
            .Where(t => t.ClosedOn is null)
            .Select(t => t.Priority?.Name)
            .Where(name => name is not null && !PriorityRankByName.ContainsKey(name));

        foreach (var name in unknownNames)
        {
            if (_loggedUnknownPriorityNames.Add(name!))
            {
                _logger.LogWarning(
                    "Gorelo ticket priority {PriorityName} is not one of the known Urgent/High/Normal/Low/None labels — tickets with this priority are counted in OpenTicketCount but excluded from PriorityCounts.",
                    name);
            }
        }
    }

    /// <summary>
    /// Pure mapping from raw Gorelo ticket data (the full paginated set, open and closed) to the
    /// provider-agnostic <see cref="PsaSnapshot"/>. Internal (not private) so tests can exercise
    /// the mapping logic directly, without HTTP.
    ///
    /// A ticket counts as active/open when <see cref="GoreloTicket.ClosedOn"/> is null — Gorelo's
    /// API has no server-side open filter and no documented status taxonomy, so this is the one
    /// tenant-customization-proof signal.
    ///
    /// Priority rank is resolved by name against <see cref="PriorityRankByName"/>, not by
    /// <see cref="GoreloCodeModel.Id"/>: <see cref="PsaSnapshot.PriorityCounts"/>'s "1 = most
    /// urgent" contract has no guaranteed relationship to a tenant-assigned numeric id, and
    /// PriorityEngine hardcodes rank 1 as its CRITICAL trigger, so getting this wrong would
    /// misfire the display. A ticket whose priority name isn't in the table is excluded from
    /// PriorityCounts/PriorityNames (still counted in OpenTicketCount).
    ///
    /// VipTickets is always empty and OrganizationName is always null: neither concept exists in
    /// Gorelo's public API as of this implementation.
    /// </summary>
    internal static PsaSnapshot MapSnapshot(IReadOnlyList<GoreloTicket> tickets)
    {
        var activeTickets = tickets.Where(t => t.ClosedOn is null).ToList();

        var priorityCounts = new Dictionary<int, int>();
        var priorityNames = new Dictionary<int, string>();

        foreach (var ticket in activeTickets)
        {
            var name = ticket.Priority?.Name;
            if (name is null || !PriorityRankByName.TryGetValue(name, out var rank))
            {
                continue;
            }

            priorityCounts[rank] = priorityCounts.GetValueOrDefault(rank) + 1;
            priorityNames[rank] = name;
        }

        var slaRiskTickets = activeTickets
            .Where(t => t.Sla?.FirstResponse?.Minutes is not null)
            .Select(t => new SlaRiskTicket(t.DisplayNumber ?? t.Id, (int)t.Sla!.FirstResponse!.Minutes!.Value))
            .ToList();

        var unassignedCount = activeTickets.Count(t => t.LeadAssigneeId is null);

        return new PsaSnapshot
        {
            OpenTicketCount = activeTickets.Count,
            PriorityCounts = priorityCounts,
            PriorityNames = priorityNames,
            SlaRiskTickets = slaRiskTickets,
            UnassignedTicketCount = unassignedCount,
            VipTickets = Array.Empty<VipTicket>(),
            OrganizationName = null
        };
    }

    /// <summary>Gorelo tenants run one of two fixed priority schemes (2-level Urgent/Normal, or
    /// 5-level Urgent/High/Normal/Low/None) — unlike Halo, priority names aren't arbitrary, so a
    /// fixed table (rather than per-tenant discovery) is sufficient.</summary>
    private static readonly IReadOnlyDictionary<string, int> PriorityRankByName =
        new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
        {
            ["Urgent"] = 1,
            ["High"] = 2,
            ["Normal"] = 3,
            ["Low"] = 4,
            ["None"] = 5
        };
}
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test PsaToolAgent.sln --filter FullyQualifiedName~GoreloPsaDataProviderTests`
Expected: PASS (11 tests: the 8 from Task 1 plus the 3 added here).

- [ ] **Step 6: Commit**

```bash
git add src/PsaToolAgent/Psa/Gorelo/GoreloOptions.cs src/PsaToolAgent/Psa/Gorelo/GoreloPsaDataProvider.cs test/PsaToolAgent.Tests/Psa/Gorelo/GoreloPsaDataProviderTests.cs
git commit -m "Implement Gorelo ticket fetching, cursor pagination, and IPsaDataProvider"
```

---

### Task 3: Wire Gorelo into DI, config, and docs

**Files:**
- Modify: `src/PsaToolAgent/Program.cs`
- Modify: `src/PsaToolAgent/appsettings.json`
- Modify: `README.md`

**Interfaces:**
- Consumes: `GoreloOptions` and `GoreloPsaDataProvider` from Task 2 (`PsaToolAgent.Psa.Gorelo` namespace).
- Produces: nothing consumed by later tasks — this is the last task.

- [ ] **Step 1: Register `GoreloOptions` and add the Gorelo DI branch in `Program.cs`**

Add the `using` alongside the existing Halo one:

```csharp
using PsaToolAgent.Psa.Gorelo;
```

Add options binding immediately after the existing `HaloOptions` block:

```csharp
builder.Services.AddOptions<GoreloOptions>()
    .Bind(builder.Configuration.GetSection(GoreloOptions.SectionName))
    .ValidateDataAnnotations()
    .ValidateOnStart();
```

Replace the `if (string.Equals(psaProviderName, "Halo", ...)) { ... } else { throw ... }` block with:

```csharp
var psaProviderName = builder.Configuration.GetSection(PsaOptions.SectionName)["Provider"];
if (string.Equals(psaProviderName, "Halo", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IPsaDataProvider, HaloPsaDataProvider>((provider, client) =>
    {
        var options = provider.GetRequiredService<IOptions<HaloOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
    });
}
else if (string.Equals(psaProviderName, "Gorelo", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IPsaDataProvider, GoreloPsaDataProvider>((provider, client) =>
    {
        var options = provider.GetRequiredService<IOptions<GoreloOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
        client.Timeout = TimeSpan.FromSeconds(30);
        client.DefaultRequestHeaders.Add("X-API-Key", options.ApiKey);
    });
}
else
{
    throw new InvalidOperationException($"Unknown Psa:Provider '{psaProviderName}'. Supported: Halo, Gorelo.");
}
```

- [ ] **Step 2: Add a Gorelo section to `appsettings.json`**

In `src/PsaToolAgent/appsettings.json`, add a `Gorelo` object alongside the existing `Halo` one under `Psa` (leave `Provider` set to `"Halo"` — this only adds the template for anyone switching to Gorelo, it doesn't change default behavior):

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  },
  "Psa": {
    "Provider": "Halo",
    "PollIntervalSeconds": 60,
    "SlaRiskThresholdMinutes": 60,
    "Halo": {
      "BaseUrl": "https://your-tenant.halopsa.com/",
      "ClientId": "",
      "ClientSecret": "",
      "Scope": "read:tickets",
      "OrganisationId": 1
    },
    "Gorelo": {
      "BaseUrl": "https://api.aue.gorelo.io/",
      "ApiKey": "",
      "PageSize": 200
    }
  },
  "BusyBar": {
    "Address": "10.0.4.20"
  },
  "Dashboard": {
    "HeaderText": "WRC SERVICE DESK",
    "DisplayCycleSeconds": 5
  }
}
```

- [ ] **Step 3: Document the new config keys and provider caveats in `README.md`**

In the `## Configuration` table, add rows after the existing `Psa:Halo:*` rows (before the `BusyBar:Address` row):

```markdown
| `Psa:Gorelo:BaseUrl` | — | Your Gorelo tenant's regional API base URL (e.g. `https://api.aue.gorelo.io/` for Australia — see [Gorelo's API docs](https://help.gorelo.io/api-overview) for other regions). |
| `Psa:Gorelo:ApiKey` | — | Gorelo API key, sent as the `X-API-Key` header — a secret, set via env/user-secrets. |
| `Psa:Gorelo:PageSize` | `200` | Tickets requested per page when paginating `GET /v1/tickets`. Gorelo's API has no ticket-status filter, so every poll pages through the *entire* ticket set (open and closed) client-side; as implemented this is only suitable for smaller tenants (roughly under 2,000 total tickets). |
```

After the `## Least privilege` section's existing bullets (Halo API client, Container), add:

```markdown
- **Gorelo API key**: Gorelo's public API uses a single static key with no documented scoping —
  unlike Halo's OAuth client, there's no token expiry to limit an exposure window if it leaks.
  Use a dedicated key if Gorelo's admin UI supports issuing one, and rotate it immediately if ever
  exposed.
```

Add a new subsection after `## Least privilege` (before `## Docker networking`):

```markdown
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

See [`docs/superpowers/specs/2026-08-06-gorelo-psa-provider-design.md`](docs/superpowers/specs/2026-08-06-gorelo-psa-provider-design.md)
for the full rationale behind these decisions.
```

- [ ] **Step 4: Build and run the full test suite**

Run: `dotnet build PsaToolAgent.sln`
Expected: build succeeds with no errors.

Run: `dotnet test PsaToolAgent.sln`
Expected: all tests pass, including the 11 Gorelo tests from Tasks 1–2 and every pre-existing Halo/PriorityEngine/Renderer test (no regressions).

- [ ] **Step 5: Commit**

```bash
git add src/PsaToolAgent/Program.cs src/PsaToolAgent/appsettings.json README.md
git commit -m "Wire Gorelo provider into DI, config, and docs"
```
