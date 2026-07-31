# psatool-busybar-agent Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Docker-deployed .NET Worker Service that polls a pluggable PSA provider (Halo first)
every 60 seconds, determines dashboard priority via a pure priority-ranking function, and renders
the result to a physical BUSY Bar over the network using the released `BusyBar` NuGet package.

**Architecture:** `PollingBackgroundService` drives a timed loop: `IPsaDataProvider.GetSnapshotAsync()`
produces a provider-agnostic `PsaSnapshot`, `PriorityEngine.Evaluate()` (a pure function) turns
that into a `DashboardState`, and `BusyBarRenderer` turns the `DashboardState` into
`BusyBar.DisplayDrawAsync` calls. `HaloPsaDataProvider` is the only `IPsaDataProvider`
implementation for v1, authenticating via OAuth2 client credentials.

**Tech Stack:** .NET 10, `Microsoft.Extensions.Hosting` Worker Service, `BusyBar` NuGet package
(published, not a project reference), `Microsoft.Extensions.Options`, `Microsoft.Extensions.Logging`,
xUnit, Docker.

## Global Constraints

- Target framework: `net10.0`, matching the `BusyBar` package.
- `BusyBar` is consumed as the **published NuGet package** (`BusyBar`, version `1.0.0`), never a
  local project reference.
- Configuration is `appsettings.json` / `IConfiguration` with strongly-typed options classes — no
  YAML.
- PSA provider selection is config-driven (`Psa:Provider`), resolved once at startup via DI. No
  runtime/external plugin loading.
- Single BUSY Bar device per instance; address from config (`BusyBar:Address`, default `10.0.4.20`).
- Halo authentication is OAuth2 client credentials; the OAuth scope is configurable
  (`Psa:Halo:Scope`) so it can be set to the minimum the API client actually needs — never
  hardcode a broad/admin scope.
- A failed poll cycle (Halo unreachable, auth failure, BUSY Bar unreachable) is logged and the
  service waits for the next interval — it must never crash the process.
- Priority precedence, evaluated top to bottom, first match wins: rank-1 tickets → SLA-risk
  tickets (within threshold) → VIP tickets → unassigned tickets (still NORMAL mode, with a
  callout) → standard NORMAL mode.
- Test project mirrors `busybar-dotnet`'s conventions: xUnit, a `FakeHttpMessageHandler` in
  `Internal/`, `Microsoft.NET.Test.Sdk` 18.8.1, `xunit` 2.9.3, `xunit.runner.visualstudio` 3.1.5.
- Never commit real Halo ticket/customer data (names, emails, ticket contents) anywhere in this
  repo, including tests and docs — use synthetic examples only.

## Real Halo API Shape (confirmed live against a real tenant)

This was checked directly against a real Halo instance before writing this plan, so the DTOs
below use real field names rather than guesses:

- `GET /api/Tickets?open_only=true&includeslatimer=true` returns
  `{"record_count": N, "tickets": [...]}`.
- Each ticket has (relevant fields only): `id` (int), `summary` (string), `priority_id` (int),
  `agent_id` (int — `0` means unassigned for this tenant's configuration; verify against yours),
  `client_name` (string), `is_vip` (bool, **directly on the ticket**, not a separate client
  lookup), `slatimeleft` (nullable double, **hours** remaining until SLA breach — not minutes),
  `onhold` (bool).
- Priority IDs are **not** literally "P1"/"P2" — a real tenant's priority names were `Critical`/
  `Urgent`/`High`/`Medium`/`Low` with `priorityid` 1 (most urgent) through 4 (least urgent).
  `PsaSnapshot.PriorityCounts` is therefore keyed by **numeric rank** (1 = most urgent tier a PSA
  defines), not a provider-specific label — this is what keeps the contract usable by a future
  non-Halo provider. "P1"/"P2" text only appears at the *display* layer (`BusyBarRenderer`), as
  an abbreviation of rank 1/rank 2, decoupled from whatever the PSA itself calls them.
- OAuth2 token endpoint: `POST /auth/token`, `application/x-www-form-urlencoded` body with
  `grant_type=client_credentials`, `client_id`, `client_secret`, `scope`. Response:
  `{"access_token": "...", "expires_in": 3600}`.

---

### Task 1: Solution & project scaffolding

**Files:**
- Create: `PsaToolAgent.sln`
- Create: `src/PsaToolAgent/PsaToolAgent.csproj`
- Create: `src/PsaToolAgent/Program.cs`
- Create: `src/PsaToolAgent/appsettings.json`
- Create: `test/PsaToolAgent.Tests/PsaToolAgent.Tests.csproj`
- Create: `.gitignore`
- Create: `README.md`
- Create: `LICENSE`

**Interfaces:**
- Produces: a buildable, empty Worker Service host (`Program.cs` boots via
  `Host.CreateApplicationBuilder(args).Build().Run()`, no services registered yet) and an empty
  but buildable xUnit test project. Later tasks add real code to both.

- [ ] **Step 1: Create the solution and directories**

```bash
mkdir -p src/PsaToolAgent test/PsaToolAgent.Tests
dotnet new sln -n PsaToolAgent
```

- [ ] **Step 2: Create the main project**

`src/PsaToolAgent/PsaToolAgent.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk.Worker">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <RootNamespace>PsaToolAgent</RootNamespace>
    <UserSecretsId>psatool-busybar-agent</UserSecretsId>
    <InvariantGlobalization>true</InvariantGlobalization>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="BusyBar" Version="1.0.0" />
  </ItemGroup>

</Project>
```

`src/PsaToolAgent/Program.cs`:

```csharp
using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

var host = builder.Build();
host.Run();
```

`src/PsaToolAgent/appsettings.json`:

```json
{
  "Logging": {
    "LogLevel": {
      "Default": "Information"
    }
  }
}
```

- [ ] **Step 3: Create the test project**

`test/PsaToolAgent.Tests/PsaToolAgent.Tests.csproj`:

```xml
<Project Sdk="Microsoft.NET.Sdk">

  <PropertyGroup>
    <TargetFramework>net10.0</TargetFramework>
    <ImplicitUsings>enable</ImplicitUsings>
    <Nullable>enable</Nullable>
    <IsPackable>false</IsPackable>
    <IsTestProject>true</IsTestProject>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk" Version="18.8.1" />
    <PackageReference Include="xunit" Version="2.9.3" />
    <PackageReference Include="xunit.runner.visualstudio" Version="3.1.5" />
  </ItemGroup>

  <ItemGroup>
    <ProjectReference Include="..\..\src\PsaToolAgent\PsaToolAgent.csproj" />
  </ItemGroup>

</Project>
```

- [ ] **Step 4: Add both projects to the solution**

```bash
dotnet sln add src/PsaToolAgent/PsaToolAgent.csproj
dotnet sln add test/PsaToolAgent.Tests/PsaToolAgent.Tests.csproj
```

- [ ] **Step 5: Add `.gitignore`**

```
.superpowers/
bin/
obj/
artifacts/
*.user
.vs/
```

- [ ] **Step 6: Add a README stub**

`README.md`:

```markdown
# psatool-busybar-agent

A .NET Worker Service that polls a pluggable PSA provider (Halo first) and drives a physical
BUSY Bar with a priority-ranked helpdesk dashboard.

Full documentation, configuration reference, and deployment guide are expanded in a later task.
```

- [ ] **Step 7: Add MIT LICENSE**

`LICENSE` — same MIT text as `busybar-dotnet`'s `LICENSE` file, with `Copyright (c) 2026 MJCO`.

- [ ] **Step 8: Verify the solution builds and tests run**

Run: `dotnet build PsaToolAgent.sln`
Expected: Build succeeds, 0 errors, 0 warnings.

Run: `dotnet test PsaToolAgent.sln`
Expected: exits with a "No test is available" warning, not a failure — there are no `[Fact]`
methods yet (this task is pure scaffolding; Task 3 adds the first real tests). The build itself
succeeding is what this step actually verifies.

- [ ] **Step 9: Commit**

```bash
git add -A
git commit -m "Scaffold solution, Worker Service project, and test project"
```

---

### Task 2: PSA data contracts

**Files:**
- Create: `src/PsaToolAgent/Psa/PsaSnapshot.cs`
- Create: `src/PsaToolAgent/Psa/IPsaDataProvider.cs`
- Create: `src/PsaToolAgent/Psa/PsaOptions.cs`

**Interfaces:**
- Produces: `PsaSnapshot`, `SlaRiskTicket`, `VipTicket` (records), `IPsaDataProvider` interface,
  `PsaOptions` (options class) — the seam every later task builds against.

- [ ] **Step 1: Write `PsaSnapshot.cs`**

```csharp
namespace PsaToolAgent.Psa;

/// <summary>
/// Provider-agnostic snapshot of PSA ticket state, produced by any <see cref="IPsaDataProvider"/>.
/// </summary>
public sealed record PsaSnapshot
{
    public required int OpenTicketCount { get; init; }

    /// <summary>
    /// Ticket counts keyed by priority rank, where 1 is the most urgent tier a PSA defines and
    /// higher numbers are progressively less urgent. Using a numeric rank rather than a
    /// provider-specific label (e.g. Halo's "Critical"/"Urgent"/"High") keeps this contract usable
    /// by any future PSA provider, whatever it calls its own priority tiers. "P1"/"P2" text is a
    /// display-layer abbreviation of rank 1/rank 2, not part of this contract.
    /// </summary>
    public required IReadOnlyDictionary<int, int> PriorityCounts { get; init; }

    public required IReadOnlyList<SlaRiskTicket> SlaRiskTickets { get; init; }
    public required int UnassignedTicketCount { get; init; }
    public required IReadOnlyList<VipTicket> VipTickets { get; init; }
}

/// <param name="TicketId">Provider-specific ticket identifier, as a display-ready string.</param>
/// <param name="MinutesRemaining">Minutes remaining until this ticket's SLA breaches.</param>
public sealed record SlaRiskTicket(string TicketId, int MinutesRemaining);

/// <param name="TicketId">Provider-specific ticket identifier, as a display-ready string.</param>
/// <param name="CustomerName">Name of the VIP customer the ticket belongs to.</param>
public sealed record VipTicket(string TicketId, string CustomerName);
```

- [ ] **Step 2: Write `IPsaDataProvider.cs`**

```csharp
namespace PsaToolAgent.Psa;

/// <summary>
/// The pluggable-provider seam: implement this for a new PSA. Register the implementation in DI
/// and select it via the <c>Psa:Provider</c> config setting.
/// </summary>
public interface IPsaDataProvider
{
    Task<PsaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken);
}
```

- [ ] **Step 3: Write `PsaOptions.cs`**

```csharp
namespace PsaToolAgent.Psa;

public sealed class PsaOptions
{
    public const string SectionName = "Psa";

    /// <summary>Which <see cref="IPsaDataProvider"/> implementation to use. Only "Halo" is
    /// supported for v1.</summary>
    public required string Provider { get; init; }

    public int PollIntervalSeconds { get; init; } = 60;

    /// <summary>A ticket is "SLA-risk" when its remaining time to breach is at or below this
    /// threshold.</summary>
    public int SlaRiskThresholdMinutes { get; init; } = 60;
}
```

- [ ] **Step 4: Verify the solution still builds**

Run: `dotnet build PsaToolAgent.sln`
Expected: Build succeeds, 0 errors, 0 warnings.

- [ ] **Step 5: Commit**

```bash
git add src/PsaToolAgent/Psa/
git commit -m "Add PSA data contracts (PsaSnapshot, IPsaDataProvider, PsaOptions)"
```

---

### Task 3: PriorityEngine and DashboardState

**Files:**
- Create: `src/PsaToolAgent/Dashboard/DashboardState.cs`
- Create: `src/PsaToolAgent/Dashboard/PriorityEngine.cs`
- Test: `test/PsaToolAgent.Tests/Dashboard/PriorityEngineTests.cs`

**Interfaces:**
- Consumes: `PsaToolAgent.Psa.PsaSnapshot`, `SlaRiskTicket`, `VipTicket` (Task 2).
- Produces: `DashboardState` (abstract record) with `NormalDashboardState`,
  `SlaWarningDashboardState`, `CriticalDashboardState` derived records; `PriorityEngine.Evaluate(PsaSnapshot, int) -> DashboardState`
  (static method) — consumed by Task 6 (`BusyBarRenderer`) and Task 7 (`PollingBackgroundService`).

- [ ] **Step 1: Write `DashboardState.cs`**

```csharp
namespace PsaToolAgent.Dashboard;

public abstract record DashboardState;

public sealed record NormalDashboardState : DashboardState
{
    public required int Rank1Count { get; init; }
    public required int Rank2Count { get; init; }

    /// <summary>Non-zero when unassigned tickets exist but nothing more urgent does — the
    /// display shows an unassigned-count callout instead of "SLA: OK" in this case.</summary>
    public int UnassignedCount { get; init; }
}

public sealed record SlaWarningDashboardState : DashboardState
{
    public required string TicketId { get; init; }
    public required int MinutesRemaining { get; init; }
}

/// <summary>Covers both the "rank-1 tickets open" and "VIP tickets open" cases — <see cref="Reason"/>
/// distinguishes which triggered it (e.g. "P1 OPEN" or "VIP OPEN").</summary>
public sealed record CriticalDashboardState : DashboardState
{
    public required string Reason { get; init; }
    public required int Count { get; init; }
}
```

- [ ] **Step 2: Write the failing tests**

`test/PsaToolAgent.Tests/Dashboard/PriorityEngineTests.cs`:

```csharp
using PsaToolAgent.Dashboard;
using PsaToolAgent.Psa;
using Xunit;

namespace PsaToolAgent.Tests.Dashboard;

public class PriorityEngineTests
{
    private static PsaSnapshot EmptySnapshot() => new()
    {
        OpenTicketCount = 0,
        PriorityCounts = new Dictionary<int, int>(),
        SlaRiskTickets = Array.Empty<SlaRiskTicket>(),
        UnassignedTicketCount = 0,
        VipTickets = Array.Empty<VipTicket>()
    };

    [Fact]
    public void Evaluate_ReturnsNormal_WhenSnapshotIsEmpty()
    {
        var state = PriorityEngine.Evaluate(EmptySnapshot(), slaRiskThresholdMinutes: 60);

        var normal = Assert.IsType<NormalDashboardState>(state);
        Assert.Equal(0, normal.Rank1Count);
        Assert.Equal(0, normal.Rank2Count);
        Assert.Equal(0, normal.UnassignedCount);
    }

    [Fact]
    public void Evaluate_ReturnsCritical_WhenRank1TicketsPresent()
    {
        var snapshot = EmptySnapshot() with { PriorityCounts = new Dictionary<int, int> { [1] = 3 } };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        var critical = Assert.IsType<CriticalDashboardState>(state);
        Assert.Equal("P1 OPEN", critical.Reason);
        Assert.Equal(3, critical.Count);
    }

    [Fact]
    public void Evaluate_ReturnsSlaWarning_WhenTicketBreachesWithinThreshold()
    {
        var snapshot = EmptySnapshot() with { SlaRiskTickets = new[] { new SlaRiskTicket("101", 45) } };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        var slaWarning = Assert.IsType<SlaWarningDashboardState>(state);
        Assert.Equal("101", slaWarning.TicketId);
        Assert.Equal(45, slaWarning.MinutesRemaining);
    }

    [Fact]
    public void Evaluate_IgnoresSlaTicket_WhenOutsideThreshold()
    {
        var snapshot = EmptySnapshot() with { SlaRiskTickets = new[] { new SlaRiskTicket("101", 90) } };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        Assert.IsType<NormalDashboardState>(state);
    }

    [Fact]
    public void Evaluate_PicksMostUrgentSlaTicket_WhenMultiplePresent()
    {
        var snapshot = EmptySnapshot() with
        {
            SlaRiskTickets = new[] { new SlaRiskTicket("101", 45), new SlaRiskTicket("102", 10) }
        };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        var slaWarning = Assert.IsType<SlaWarningDashboardState>(state);
        Assert.Equal("102", slaWarning.TicketId);
    }

    [Fact]
    public void Evaluate_ReturnsCritical_WhenVipTicketsPresent()
    {
        var snapshot = EmptySnapshot() with { VipTickets = new[] { new VipTicket("201", "Acme Corp") } };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        var critical = Assert.IsType<CriticalDashboardState>(state);
        Assert.Equal("VIP OPEN", critical.Reason);
        Assert.Equal(1, critical.Count);
    }

    [Fact]
    public void Evaluate_ReturnsNormalWithUnassignedCount_WhenOnlyUnassignedTicketsPresent()
    {
        var snapshot = EmptySnapshot() with { UnassignedTicketCount = 5 };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        var normal = Assert.IsType<NormalDashboardState>(state);
        Assert.Equal(5, normal.UnassignedCount);
    }

    [Fact]
    public void Evaluate_Rank1TakesPrecedenceOverEverythingElse()
    {
        var snapshot = new PsaSnapshot
        {
            OpenTicketCount = 10,
            PriorityCounts = new Dictionary<int, int> { [1] = 1 },
            SlaRiskTickets = new[] { new SlaRiskTicket("101", 5) },
            UnassignedTicketCount = 3,
            VipTickets = new[] { new VipTicket("201", "Acme Corp") }
        };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        var critical = Assert.IsType<CriticalDashboardState>(state);
        Assert.Equal("P1 OPEN", critical.Reason);
    }

    [Fact]
    public void Evaluate_SlaWarningTakesPrecedenceOverVipAndUnassigned()
    {
        var snapshot = new PsaSnapshot
        {
            OpenTicketCount = 10,
            PriorityCounts = new Dictionary<int, int>(),
            SlaRiskTickets = new[] { new SlaRiskTicket("101", 5) },
            UnassignedTicketCount = 3,
            VipTickets = new[] { new VipTicket("201", "Acme Corp") }
        };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        Assert.IsType<SlaWarningDashboardState>(state);
    }

    [Fact]
    public void Evaluate_VipTakesPrecedenceOverUnassigned()
    {
        var snapshot = new PsaSnapshot
        {
            OpenTicketCount = 10,
            PriorityCounts = new Dictionary<int, int>(),
            SlaRiskTickets = Array.Empty<SlaRiskTicket>(),
            UnassignedTicketCount = 3,
            VipTickets = new[] { new VipTicket("201", "Acme Corp") }
        };

        var state = PriorityEngine.Evaluate(snapshot, slaRiskThresholdMinutes: 60);

        Assert.IsType<CriticalDashboardState>(state);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test test/PsaToolAgent.Tests/PsaToolAgent.Tests.csproj --filter PriorityEngineTests`
Expected: FAIL with "PriorityEngine does not exist" (or similar compile error) — `PriorityEngine.cs`
does not exist yet.

- [ ] **Step 4: Write `PriorityEngine.cs`**

```csharp
using PsaToolAgent.Psa;

namespace PsaToolAgent.Dashboard;

/// <summary>
/// Pure function turning a <see cref="PsaSnapshot"/> into a <see cref="DashboardState"/>. No I/O,
/// no PSA or BUSY Bar dependency — evaluates the 5-level priority order, first match wins.
/// </summary>
public static class PriorityEngine
{
    public static DashboardState Evaluate(PsaSnapshot snapshot, int slaRiskThresholdMinutes)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var rank1Count = snapshot.PriorityCounts.GetValueOrDefault(1);
        if (rank1Count > 0)
        {
            return new CriticalDashboardState { Reason = "P1 OPEN", Count = rank1Count };
        }

        var worstSlaRisk = snapshot.SlaRiskTickets
            .Where(t => t.MinutesRemaining <= slaRiskThresholdMinutes)
            .OrderBy(t => t.MinutesRemaining)
            .FirstOrDefault();
        if (worstSlaRisk is not null)
        {
            return new SlaWarningDashboardState
            {
                TicketId = worstSlaRisk.TicketId,
                MinutesRemaining = worstSlaRisk.MinutesRemaining
            };
        }

        if (snapshot.VipTickets.Count > 0)
        {
            return new CriticalDashboardState { Reason = "VIP OPEN", Count = snapshot.VipTickets.Count };
        }

        return new NormalDashboardState
        {
            Rank1Count = rank1Count,
            Rank2Count = snapshot.PriorityCounts.GetValueOrDefault(2),
            UnassignedCount = snapshot.UnassignedTicketCount
        };
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test test/PsaToolAgent.Tests/PsaToolAgent.Tests.csproj --filter PriorityEngineTests`
Expected: `Passed! - Failed: 0, Passed: 10, Skipped: 0, Total: 10`

- [ ] **Step 6: Commit**

```bash
git add src/PsaToolAgent/Dashboard/ test/PsaToolAgent.Tests/Dashboard/
git commit -m "Add PriorityEngine and DashboardState with full precedence test coverage"
```

---

### Task 4: Halo OAuth2 client credentials auth

**Files:**
- Create: `src/PsaToolAgent/Psa/Halo/HaloOptions.cs`
- Create: `src/PsaToolAgent/Psa/Halo/HaloAuthClient.cs`
- Create: `test/PsaToolAgent.Tests/Internal/FakeHttpMessageHandler.cs`
- Test: `test/PsaToolAgent.Tests/Psa/Halo/HaloAuthClientTests.cs`

**Interfaces:**
- Produces: `HaloOptions` (options class), `HaloAuthClient` with
  `GetAccessTokenAsync(CancellationToken) -> Task<string>` — consumed by Task 5
  (`HaloPsaDataProvider`). `FakeHttpMessageHandler` — reused by Tasks 5, 6, 7's tests.

- [ ] **Step 1: Write `HaloOptions.cs`**

```csharp
namespace PsaToolAgent.Psa.Halo;

public sealed class HaloOptions
{
    public const string SectionName = "Psa:Halo";

    public required string BaseUrl { get; init; }
    public required string ClientId { get; init; }
    public required string ClientSecret { get; init; }

    /// <summary>OAuth2 scope requested for the client-credentials token. Set this to the minimum
    /// your Halo API client is actually granted (e.g. a read-only tickets scope) — do not leave
    /// this at a broad/admin default.</summary>
    public string Scope { get; init; } = "all";
}
```

- [ ] **Step 2: Write `FakeHttpMessageHandler.cs`**

```csharp
using System.Net;
using System.Text;

namespace PsaToolAgent.Tests.Internal;

internal sealed class FakeHttpMessageHandler : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = new();
    public HttpRequestMessage? LastRequest => Requests.Count > 0 ? Requests[^1] : null;
    public string? LastRequestBody { get; private set; }

    /// <summary>Status code and body returned for every request, unless <see cref="Respond"/> is set.</summary>
    public HttpStatusCode ResponseStatusCode { get; set; } = HttpStatusCode.OK;
    public string ResponseBody { get; set; } = "{}";

    /// <summary>When set, overrides <see cref="ResponseStatusCode"/>/<see cref="ResponseBody"/> so a
    /// test can return different responses for different requests (e.g. an auth call vs. a data
    /// call).</summary>
    public Func<HttpRequestMessage, HttpResponseMessage>? Respond { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        LastRequestBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);

        if (Respond is not null)
        {
            return Respond(request);
        }

        return new HttpResponseMessage(ResponseStatusCode)
        {
            Content = new StringContent(ResponseBody, Encoding.UTF8, "application/json")
        };
    }
}
```

- [ ] **Step 3: Write the failing test**

`test/PsaToolAgent.Tests/Psa/Halo/HaloAuthClientTests.cs`:

```csharp
using Microsoft.Extensions.Options;
using PsaToolAgent.Psa.Halo;
using PsaToolAgent.Tests.Internal;
using Xunit;

namespace PsaToolAgent.Tests.Psa.Halo;

public class HaloAuthClientTests
{
    private static (HaloAuthClient client, FakeHttpMessageHandler handler) CreateClient()
    {
        var handler = new FakeHttpMessageHandler();
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.halopsa.com/") };
        var options = Options.Create(new HaloOptions
        {
            BaseUrl = "https://example.halopsa.com/",
            ClientId = "test-client",
            ClientSecret = "test-secret",
            Scope = "read:tickets"
        });
        return (new HaloAuthClient(http, options), handler);
    }

    [Fact]
    public async Task GetAccessTokenAsync_SendsClientCredentialsGrant()
    {
        var (client, handler) = CreateClient();
        handler.ResponseBody = "{\"access_token\":\"abc123\",\"expires_in\":3600}";

        var token = await client.GetAccessTokenAsync(CancellationToken.None);

        Assert.Equal("abc123", token);
        Assert.Contains("grant_type=client_credentials", handler.LastRequestBody);
        Assert.Contains("scope=read%3Atickets", handler.LastRequestBody);
    }

    [Fact]
    public async Task GetAccessTokenAsync_CachesTokenUntilNearExpiry()
    {
        var (client, handler) = CreateClient();
        handler.ResponseBody = "{\"access_token\":\"abc123\",\"expires_in\":3600}";

        await client.GetAccessTokenAsync(CancellationToken.None);
        await client.GetAccessTokenAsync(CancellationToken.None);

        Assert.Single(handler.Requests);
    }
}
```

- [ ] **Step 4: Run the tests to verify they fail**

Run: `dotnet test test/PsaToolAgent.Tests/PsaToolAgent.Tests.csproj --filter HaloAuthClientTests`
Expected: FAIL with a compile error — `HaloAuthClient` does not exist yet.

- [ ] **Step 5: Write `HaloAuthClient.cs`**

```csharp
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;

namespace PsaToolAgent.Psa.Halo;

public sealed class HaloAuthClient
{
    private readonly HttpClient _http;
    private readonly HaloOptions _options;
    private string? _cachedToken;
    private DateTimeOffset _tokenExpiresAt = DateTimeOffset.MinValue;

    public HaloAuthClient(HttpClient http, IOptions<HaloOptions> options)
    {
        _http = http;
        _options = options.Value;
    }

    public async Task<string> GetAccessTokenAsync(CancellationToken cancellationToken)
    {
        if (_cachedToken is not null && DateTimeOffset.UtcNow < _tokenExpiresAt)
        {
            return _cachedToken;
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, "auth/token")
        {
            Content = new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = _options.ClientId,
                ["client_secret"] = _options.ClientSecret,
                ["scope"] = _options.Scope
            })
        };

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<TokenResponse>(cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Halo token response was empty.");

        _cachedToken = body.AccessToken;
        // Refresh 60s before actual expiry so a request never races a just-expired token.
        _tokenExpiresAt = DateTimeOffset.UtcNow.AddSeconds(body.ExpiresIn - 60);
        return _cachedToken;
    }

    private sealed record TokenResponse(
        [property: JsonPropertyName("access_token")] string AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}
```

- [ ] **Step 6: Run the tests to verify they pass**

Run: `dotnet test test/PsaToolAgent.Tests/PsaToolAgent.Tests.csproj --filter HaloAuthClientTests`
Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2`

- [ ] **Step 7: Commit**

```bash
git add src/PsaToolAgent/Psa/Halo/HaloOptions.cs src/PsaToolAgent/Psa/Halo/HaloAuthClient.cs test/PsaToolAgent.Tests/Internal/ test/PsaToolAgent.Tests/Psa/Halo/HaloAuthClientTests.cs
git commit -m "Add Halo OAuth2 client-credentials auth (HaloAuthClient)"
```

---

### Task 5: HaloPsaDataProvider

**Files:**
- Create: `src/PsaToolAgent/Psa/Halo/HaloApiModels.cs`
- Create: `src/PsaToolAgent/Psa/Halo/HaloPsaDataProvider.cs`
- Test: `test/PsaToolAgent.Tests/Psa/Halo/HaloPsaDataProviderTests.cs`

**Interfaces:**
- Consumes: `IPsaDataProvider`, `PsaSnapshot`, `SlaRiskTicket`, `VipTicket`, `PsaOptions` (Task 2);
  `HaloAuthClient`, `HaloOptions` (Task 4).
- Produces: `HaloPsaDataProvider : IPsaDataProvider` — consumed by Task 7's DI wiring.

- [ ] **Step 1: Write `HaloApiModels.cs`**

```csharp
using System.Text.Json.Serialization;

namespace PsaToolAgent.Psa.Halo;

internal sealed record HaloTicketsResponse(
    [property: JsonPropertyName("record_count")] int RecordCount,
    [property: JsonPropertyName("tickets")] IReadOnlyList<HaloTicket> Tickets);

internal sealed record HaloTicket(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("summary")] string Summary,
    [property: JsonPropertyName("priority_id")] int PriorityId,
    [property: JsonPropertyName("agent_id")] int AgentId,
    [property: JsonPropertyName("client_name")] string ClientName,
    [property: JsonPropertyName("is_vip")] bool IsVip,
    [property: JsonPropertyName("slatimeleft")] double? SlaTimeLeftHours,
    [property: JsonPropertyName("onhold")] bool OnHold);
```

- [ ] **Step 2: Write the failing tests**

`test/PsaToolAgent.Tests/Psa/Halo/HaloPsaDataProviderTests.cs`:

```csharp
using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using PsaToolAgent.Psa;
using PsaToolAgent.Psa.Halo;
using PsaToolAgent.Tests.Internal;
using Xunit;

namespace PsaToolAgent.Tests.Psa.Halo;

public class HaloPsaDataProviderTests
{
    [Fact]
    public void MapSnapshot_CountsPriorityCorrectly()
    {
        var tickets = new[]
        {
            new HaloTicket(1, "Ticket A", PriorityId: 1, AgentId: 3, ClientName: "Acme", IsVip: false, SlaTimeLeftHours: null, OnHold: false),
            new HaloTicket(2, "Ticket B", PriorityId: 1, AgentId: 3, ClientName: "Acme", IsVip: false, SlaTimeLeftHours: null, OnHold: false),
            new HaloTicket(3, "Ticket C", PriorityId: 2, AgentId: 3, ClientName: "Acme", IsVip: false, SlaTimeLeftHours: null, OnHold: false)
        };

        var snapshot = HaloPsaDataProvider.MapSnapshot(tickets, slaRiskThresholdMinutes: 60);

        Assert.Equal(3, snapshot.OpenTicketCount);
        Assert.Equal(2, snapshot.PriorityCounts[1]);
        Assert.Equal(1, snapshot.PriorityCounts[2]);
    }

    [Fact]
    public void MapSnapshot_IdentifiesUnassignedTickets_ByZeroAgentId()
    {
        var tickets = new[]
        {
            new HaloTicket(1, "Ticket A", PriorityId: 3, AgentId: 0, ClientName: "Acme", IsVip: false, SlaTimeLeftHours: null, OnHold: false),
            new HaloTicket(2, "Ticket B", PriorityId: 3, AgentId: 3, ClientName: "Acme", IsVip: false, SlaTimeLeftHours: null, OnHold: false)
        };

        var snapshot = HaloPsaDataProvider.MapSnapshot(tickets, slaRiskThresholdMinutes: 60);

        Assert.Equal(1, snapshot.UnassignedTicketCount);
    }

    [Fact]
    public void MapSnapshot_IdentifiesVipTickets()
    {
        var tickets = new[]
        {
            new HaloTicket(1, "Ticket A", PriorityId: 3, AgentId: 3, ClientName: "Acme", IsVip: true, SlaTimeLeftHours: null, OnHold: false)
        };

        var snapshot = HaloPsaDataProvider.MapSnapshot(tickets, slaRiskThresholdMinutes: 60);

        var vip = Assert.Single(snapshot.VipTickets);
        Assert.Equal("1", vip.TicketId);
        Assert.Equal("Acme", vip.CustomerName);
    }

    [Fact]
    public void MapSnapshot_ConvertsSlaTimeLeftHoursToMinutes_AndFiltersByThreshold()
    {
        var tickets = new[]
        {
            new HaloTicket(1, "Within threshold", PriorityId: 3, AgentId: 3, ClientName: "Acme", IsVip: false, SlaTimeLeftHours: 0.5, OnHold: false),
            new HaloTicket(2, "Outside threshold", PriorityId: 3, AgentId: 3, ClientName: "Acme", IsVip: false, SlaTimeLeftHours: 5.0, OnHold: false)
        };

        var snapshot = HaloPsaDataProvider.MapSnapshot(tickets, slaRiskThresholdMinutes: 60);

        var slaTicket = Assert.Single(snapshot.SlaRiskTickets);
        Assert.Equal("1", slaTicket.TicketId);
        Assert.Equal(30, slaTicket.MinutesRemaining);
    }

    [Fact]
    public async Task GetSnapshotAsync_AuthenticatesThenFetchesAndMapsTickets()
    {
        var handler = new FakeHttpMessageHandler
        {
            Respond = request => request.RequestUri!.AbsolutePath.Contains("auth/token")
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent("{\"access_token\":\"abc123\",\"expires_in\":3600}", Encoding.UTF8, "application/json")
                }
                : new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(
                        "{\"record_count\":1,\"tickets\":[{\"id\":1,\"summary\":\"Test\",\"priority_id\":1,\"agent_id\":3,\"client_name\":\"Acme\",\"is_vip\":false,\"slatimeleft\":null,\"onhold\":false}]}",
                        Encoding.UTF8, "application/json")
                }
        };
        var http = new HttpClient(handler) { BaseAddress = new Uri("https://example.halopsa.com/") };
        var haloOptions = Options.Create(new HaloOptions
        {
            BaseUrl = "https://example.halopsa.com/",
            ClientId = "test-client",
            ClientSecret = "test-secret"
        });
        var authClient = new HaloAuthClient(http, haloOptions);
        var psaOptions = Options.Create(new PsaOptions { Provider = "Halo", SlaRiskThresholdMinutes = 60 });
        var provider = new HaloPsaDataProvider(http, authClient, psaOptions);

        var snapshot = await provider.GetSnapshotAsync(CancellationToken.None);

        Assert.Equal(1, snapshot.OpenTicketCount);
        Assert.Equal(1, snapshot.PriorityCounts[1]);
        Assert.Contains(handler.Requests, r => r.Headers.Authorization?.Parameter == "abc123");
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test test/PsaToolAgent.Tests/PsaToolAgent.Tests.csproj --filter HaloPsaDataProviderTests`
Expected: FAIL with a compile error — `HaloPsaDataProvider` does not exist yet.

- [ ] **Step 4: Write `HaloPsaDataProvider.cs`**

```csharp
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.Extensions.Options;

namespace PsaToolAgent.Psa.Halo;

public sealed class HaloPsaDataProvider : IPsaDataProvider
{
    private readonly HttpClient _http;
    private readonly HaloAuthClient _auth;
    private readonly PsaOptions _psaOptions;

    public HaloPsaDataProvider(HttpClient http, HaloAuthClient auth, IOptions<PsaOptions> psaOptions)
    {
        _http = http;
        _auth = auth;
        _psaOptions = psaOptions.Value;
    }

    public async Task<PsaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var token = await _auth.GetAccessTokenAsync(cancellationToken).ConfigureAwait(false);

        using var request = new HttpRequestMessage(HttpMethod.Get,
            "api/Tickets?open_only=true&includeslatimer=true&pageinate=false&count=200");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();

        var body = await response.Content.ReadFromJsonAsync<HaloTicketsResponse>(cancellationToken: cancellationToken).ConfigureAwait(false)
            ?? throw new InvalidOperationException("Halo tickets response was empty.");

        return MapSnapshot(body.Tickets, _psaOptions.SlaRiskThresholdMinutes);
    }

    /// <summary>
    /// Pure mapping from raw Halo ticket data to the provider-agnostic <see cref="PsaSnapshot"/>.
    /// Internal (not private) so tests can exercise the mapping logic directly, without HTTP.
    /// </summary>
    internal static PsaSnapshot MapSnapshot(IReadOnlyList<HaloTicket> tickets, int slaRiskThresholdMinutes)
    {
        var priorityCounts = tickets
            .GroupBy(t => t.PriorityId)
            .ToDictionary(g => g.Key, g => g.Count());

        var slaRiskTickets = tickets
            .Where(t => t.SlaTimeLeftHours is not null)
            .Select(t => new SlaRiskTicket(t.Id.ToString(), (int)(t.SlaTimeLeftHours!.Value * 60)))
            .Where(t => t.MinutesRemaining <= slaRiskThresholdMinutes)
            .ToList();

        // This tenant represents "no agent assigned" as agent_id 0 — verify this convention
        // against your own Halo tenant if ticket routing differs.
        var unassignedCount = tickets.Count(t => t.AgentId == 0);

        var vipTickets = tickets
            .Where(t => t.IsVip)
            .Select(t => new VipTicket(t.Id.ToString(), t.ClientName))
            .ToList();

        return new PsaSnapshot
        {
            OpenTicketCount = tickets.Count,
            PriorityCounts = priorityCounts,
            SlaRiskTickets = slaRiskTickets,
            UnassignedTicketCount = unassignedCount,
            VipTickets = vipTickets
        };
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test test/PsaToolAgent.Tests/PsaToolAgent.Tests.csproj --filter HaloPsaDataProviderTests`
Expected: `Passed! - Failed: 0, Passed: 5, Skipped: 0, Total: 5`

- [ ] **Step 6: Commit**

```bash
git add src/PsaToolAgent/Psa/Halo/HaloApiModels.cs src/PsaToolAgent/Psa/Halo/HaloPsaDataProvider.cs test/PsaToolAgent.Tests/Psa/Halo/HaloPsaDataProviderTests.cs
git commit -m "Add HaloPsaDataProvider, mapping real Halo ticket fields to PsaSnapshot"
```

---

### Task 6: BusyBarRenderer

**Files:**
- Create: `src/PsaToolAgent/Display/DashboardOptions.cs`
- Create: `src/PsaToolAgent/Display/BusyBarRenderer.cs`
- Test: `test/PsaToolAgent.Tests/Display/BusyBarRendererTests.cs`

**Interfaces:**
- Consumes: `DashboardState`, `NormalDashboardState`, `SlaWarningDashboardState`,
  `CriticalDashboardState` (Task 3); `Busy.Bar.BusyBar`, `Busy.Bar.DisplayDrawParams`,
  `Busy.Bar.TextElement`, `Busy.Bar.TextFont` (the `BusyBar` NuGet package).
- Produces: `BusyBarRenderer` with `RenderAsync(DashboardState, CancellationToken) -> Task` —
  consumed by Task 7 (`PollingBackgroundService`).

- [ ] **Step 1: Write `DashboardOptions.cs`**

```csharp
namespace PsaToolAgent.Display;

public sealed class DashboardOptions
{
    public const string SectionName = "Dashboard";

    /// <summary>First line of the NORMAL-mode display.</summary>
    public string HeaderText { get; init; } = "WRC SERVICE DESK";
}
```

- [ ] **Step 2: Write the failing tests**

`test/PsaToolAgent.Tests/Display/BusyBarRendererTests.cs`:

```csharp
using Microsoft.Extensions.Options;
using PsaToolAgent.Dashboard;
using PsaToolAgent.Display;
using PsaToolAgent.Tests.Internal;
using Xunit;

namespace PsaToolAgent.Tests.Display;

public class BusyBarRendererTests
{
    private static (BusyBarRenderer renderer, FakeHttpMessageHandler handler) CreateRenderer()
    {
        var handler = new FakeHttpMessageHandler { ResponseBody = "{\"result\":\"OK\"}" };
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://10.0.4.20/") };
        var bar = new Busy.Bar.BusyBar(http, new Busy.Bar.BusyBarOptions());
        var options = Options.Create(new DashboardOptions());
        return (new BusyBarRenderer(bar, options), handler);
    }

    [Fact]
    public async Task RenderAsync_Normal_ShowsPriorityCountsAndSlaOk()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new NormalDashboardState { Rank1Count = 2, Rank2Count = 5, UnassignedCount = 0 };

        await renderer.RenderAsync(state, CancellationToken.None);

        Assert.Contains("\"text\":\"WRC SERVICE DESK\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"P1:2 P2:5\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"SLA: OK\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Normal_ShowsUnassignedCallout_WhenUnassignedTicketsPresent()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new NormalDashboardState { Rank1Count = 0, Rank2Count = 0, UnassignedCount = 4 };

        await renderer.RenderAsync(state, CancellationToken.None);

        Assert.Contains("\"text\":\"UNASSIGN:4\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_SlaWarning_ShowsTicketIdAndMinutesRemaining()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new SlaWarningDashboardState { TicketId = "101", MinutesRemaining = 12 };

        await renderer.RenderAsync(state, CancellationToken.None);

        Assert.Contains("\"text\":\"SLA RISK\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"Ticket #101\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"12m REMAIN\"", handler.LastRequestBody);
    }

    [Fact]
    public async Task RenderAsync_Critical_ShowsReasonAndCount()
    {
        var (renderer, handler) = CreateRenderer();
        var state = new CriticalDashboardState { Reason = "P1 OPEN", Count = 3 };

        await renderer.RenderAsync(state, CancellationToken.None);

        Assert.Contains("\"text\":\"CRITICAL\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"P1 OPEN\"", handler.LastRequestBody);
        Assert.Contains("\"text\":\"Count:3\"", handler.LastRequestBody);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test test/PsaToolAgent.Tests/PsaToolAgent.Tests.csproj --filter BusyBarRendererTests`
Expected: FAIL with a compile error — `BusyBarRenderer` does not exist yet.

- [ ] **Step 4: Write `BusyBarRenderer.cs`**

```csharp
using Microsoft.Extensions.Options;
using PsaToolAgent.Dashboard;

namespace PsaToolAgent.Display;

public sealed class BusyBarRenderer
{
    private const string ApplicationName = "psatool_busybar_agent";

    private readonly Busy.Bar.BusyBar _bar;
    private readonly DashboardOptions _options;

    public BusyBarRenderer(Busy.Bar.BusyBar bar, IOptions<DashboardOptions> options)
    {
        _bar = bar;
        _options = options.Value;
    }

    public Task RenderAsync(DashboardState state, CancellationToken cancellationToken)
        => state switch
        {
            NormalDashboardState normal => RenderNormalAsync(normal, cancellationToken),
            SlaWarningDashboardState slaWarning => RenderSlaWarningAsync(slaWarning, cancellationToken),
            CriticalDashboardState critical => RenderCriticalAsync(critical, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(state))
        };

    private Task RenderNormalAsync(NormalDashboardState state, CancellationToken cancellationToken)
    {
        var thirdLine = state.UnassignedCount > 0 ? $"UNASSIGN:{state.UnassignedCount}" : "SLA: OK";
        return DrawAsync(new[] { _options.HeaderText, $"P1:{state.Rank1Count} P2:{state.Rank2Count}", thirdLine }, cancellationToken);
    }

    private Task RenderSlaWarningAsync(SlaWarningDashboardState state, CancellationToken cancellationToken)
        => DrawAsync(new[] { "SLA RISK", $"Ticket #{state.TicketId}", $"{state.MinutesRemaining}m REMAIN" }, cancellationToken);

    private Task RenderCriticalAsync(CriticalDashboardState state, CancellationToken cancellationToken)
        => DrawAsync(new[] { "CRITICAL", state.Reason, $"Count:{state.Count}" }, cancellationToken);

    private Task DrawAsync(IReadOnlyList<string> lines, CancellationToken cancellationToken)
    {
        var elements = new List<Busy.Bar.DisplayElement>(lines.Count);
        for (var i = 0; i < lines.Count; i++)
        {
            elements.Add(new Busy.Bar.TextElement
            {
                Id = i.ToString(),
                Text = lines[i],
                Font = Busy.Bar.TextFont.Normal,
                // Stacked lines at 5px intervals — a starting point only. The BUSY Bar's 16px-tall
                // canvas is tight for 3 lines of normal-size text; confirm actual spacing against a
                // real device (see busybar-dotnet's samples/LiveDeviceTest for the pattern used to
                // do this) and adjust here, or switch TextFont, if lines overlap or clip.
                Y = i * 5
            });
        }

        return _bar.DisplayDrawAsync(new Busy.Bar.DisplayDrawParams
        {
            ApplicationName = ApplicationName,
            Elements = elements
        }, cancellationToken: cancellationToken);
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test test/PsaToolAgent.Tests/PsaToolAgent.Tests.csproj --filter BusyBarRendererTests`
Expected: `Passed! - Failed: 0, Passed: 4, Skipped: 0, Total: 4`

- [ ] **Step 6: Commit**

```bash
git add src/PsaToolAgent/Display/ test/PsaToolAgent.Tests/Display/
git commit -m "Add BusyBarRenderer, rendering DashboardState to the BUSY Bar display"
```

---

### Task 7: PollingBackgroundService and DI wiring

**Files:**
- Create: `src/PsaToolAgent/Display/BusyBarOptions.cs`
- Create: `src/PsaToolAgent/PollingBackgroundService.cs`
- Modify: `src/PsaToolAgent/Program.cs`
- Modify: `src/PsaToolAgent/appsettings.json`
- Test: `test/PsaToolAgent.Tests/PollingBackgroundServiceTests.cs`

**Interfaces:**
- Consumes: `IPsaDataProvider`, `PsaOptions` (Task 2); `PriorityEngine` (Task 3); `HaloOptions`,
  `HaloAuthClient`, `HaloPsaDataProvider` (Tasks 4–5); `BusyBarRenderer`, `DashboardOptions` (Task 6).
- Produces: `PollingBackgroundService` (a `BackgroundService`), fully wired `Program.cs` — this is
  the last application-code task; Task 8 only adds deployment artifacts.

- [ ] **Step 1: Write `BusyBarOptions.cs`**

```csharp
namespace PsaToolAgent.Display;

public sealed class BusyBarOptions
{
    public const string SectionName = "BusyBar";

    public string Address { get; init; } = "10.0.4.20";
}
```

- [ ] **Step 2: Write the failing tests**

`test/PsaToolAgent.Tests/PollingBackgroundServiceTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using PsaToolAgent.Display;
using PsaToolAgent.Psa;
using PsaToolAgent.Tests.Internal;
using Xunit;

namespace PsaToolAgent.Tests;

public class PollingBackgroundServiceTests
{
    private sealed class StubPsaDataProvider : IPsaDataProvider
    {
        public Func<CancellationToken, Task<PsaSnapshot>>? OnGetSnapshot { get; set; }

        public Task<PsaSnapshot> GetSnapshotAsync(CancellationToken cancellationToken)
            => OnGetSnapshot is not null
                ? OnGetSnapshot(cancellationToken)
                : Task.FromResult(new PsaSnapshot
                {
                    OpenTicketCount = 0,
                    PriorityCounts = new Dictionary<int, int>(),
                    SlaRiskTickets = Array.Empty<SlaRiskTicket>(),
                    UnassignedTicketCount = 0,
                    VipTickets = Array.Empty<VipTicket>()
                });
    }

    private static (PollingBackgroundService service, FakeHttpMessageHandler handler, StubPsaDataProvider provider) CreateService()
    {
        var handler = new FakeHttpMessageHandler { ResponseBody = "{\"result\":\"OK\"}" };
        var http = new HttpClient(handler) { BaseAddress = new Uri("http://10.0.4.20/") };
        var bar = new Busy.Bar.BusyBar(http, new Busy.Bar.BusyBarOptions());
        var renderer = new BusyBarRenderer(bar, Options.Create(new DashboardOptions()));
        var provider = new StubPsaDataProvider();
        var options = Options.Create(new PsaOptions { Provider = "Stub", PollIntervalSeconds = 60, SlaRiskThresholdMinutes = 60 });
        var service = new PollingBackgroundService(provider, renderer, options, NullLogger<PollingBackgroundService>.Instance);
        return (service, handler, provider);
    }

    [Fact]
    public async Task PollOnceAsync_RendersDashboard_OnSuccessfulPoll()
    {
        var (service, handler, _) = CreateService();

        await service.PollOnceAsync(CancellationToken.None);

        Assert.NotNull(handler.LastRequest);
    }

    [Fact]
    public async Task PollOnceAsync_DoesNotThrow_WhenProviderFails()
    {
        var (service, _, provider) = CreateService();
        provider.OnGetSnapshot = _ => throw new HttpRequestException("Halo unreachable");

        var exception = await Record.ExceptionAsync(() => service.PollOnceAsync(CancellationToken.None));

        Assert.Null(exception);
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test test/PsaToolAgent.Tests/PsaToolAgent.Tests.csproj --filter PollingBackgroundServiceTests`
Expected: FAIL with a compile error — `PollingBackgroundService` does not exist yet.

- [ ] **Step 4: Write `PollingBackgroundService.cs`**

```csharp
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using PsaToolAgent.Dashboard;
using PsaToolAgent.Display;
using PsaToolAgent.Psa;

namespace PsaToolAgent;

public sealed class PollingBackgroundService : BackgroundService
{
    private readonly IPsaDataProvider _provider;
    private readonly BusyBarRenderer _renderer;
    private readonly IOptions<PsaOptions> _options;
    private readonly ILogger<PollingBackgroundService> _logger;

    public PollingBackgroundService(
        IPsaDataProvider provider,
        BusyBarRenderer renderer,
        IOptions<PsaOptions> options,
        ILogger<PollingBackgroundService> logger)
    {
        _provider = provider;
        _renderer = renderer;
        _options = options;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = TimeSpan.FromSeconds(_options.Value.PollIntervalSeconds);
        using var timer = new PeriodicTimer(interval);

        do
        {
            await PollOnceAsync(stoppingToken).ConfigureAwait(false);
        } while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    /// <summary>
    /// One poll-evaluate-render cycle. Internal (not private) so tests can drive it directly,
    /// without the <see cref="PeriodicTimer"/> loop. A failure here is logged and swallowed — the
    /// service must keep running and try again next interval, not crash on a transient blip.
    /// </summary>
    internal async Task PollOnceAsync(CancellationToken cancellationToken)
    {
        try
        {
            var snapshot = await _provider.GetSnapshotAsync(cancellationToken).ConfigureAwait(false);
            var state = PriorityEngine.Evaluate(snapshot, _options.Value.SlaRiskThresholdMinutes);
            await _renderer.RenderAsync(state, cancellationToken).ConfigureAwait(false);
            _logger.LogInformation("Dashboard updated: {StateType}", state.GetType().Name);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Poll cycle failed; will retry next interval.");
        }
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test test/PsaToolAgent.Tests/PsaToolAgent.Tests.csproj --filter PollingBackgroundServiceTests`
Expected: `Passed! - Failed: 0, Passed: 2, Skipped: 0, Total: 2`

- [ ] **Step 6: Wire everything together in `Program.cs`**

```csharp
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using PsaToolAgent;
using PsaToolAgent.Display;
using PsaToolAgent.Psa;
using PsaToolAgent.Psa.Halo;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.Configure<PsaOptions>(builder.Configuration.GetSection(PsaOptions.SectionName));
builder.Services.Configure<HaloOptions>(builder.Configuration.GetSection(HaloOptions.SectionName));
builder.Services.Configure<BusyBarOptions>(builder.Configuration.GetSection(BusyBarOptions.SectionName));
builder.Services.Configure<DashboardOptions>(builder.Configuration.GetSection(DashboardOptions.SectionName));

builder.Services.AddHttpClient<HaloAuthClient>((provider, client) =>
{
    var options = provider.GetRequiredService<IOptions<HaloOptions>>().Value;
    client.BaseAddress = new Uri(options.BaseUrl);
});

var psaProviderName = builder.Configuration.GetSection(PsaOptions.SectionName)["Provider"];
if (string.Equals(psaProviderName, "Halo", StringComparison.OrdinalIgnoreCase))
{
    builder.Services.AddHttpClient<IPsaDataProvider, HaloPsaDataProvider>((provider, client) =>
    {
        var options = provider.GetRequiredService<IOptions<HaloOptions>>().Value;
        client.BaseAddress = new Uri(options.BaseUrl);
    });
}
else
{
    throw new InvalidOperationException($"Unknown Psa:Provider '{psaProviderName}'. Supported: Halo.");
}

builder.Services.AddSingleton(provider =>
{
    var options = provider.GetRequiredService<IOptions<BusyBarOptions>>().Value;
    return new Busy.Bar.BusyBar(new Busy.Bar.BusyBarOptions { Addr = options.Address });
});
builder.Services.AddSingleton<BusyBarRenderer>();

builder.Services.AddHostedService<PollingBackgroundService>();

var host = builder.Build();
host.Run();
```

- [ ] **Step 7: Update `appsettings.json` with the full configuration skeleton**

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
      "Scope": "read:tickets"
    }
  },
  "BusyBar": {
    "Address": "10.0.4.20"
  },
  "Dashboard": {
    "HeaderText": "WRC SERVICE DESK"
  }
}
```

`ClientId`/`ClientSecret` are left empty here deliberately — set them via environment variables
(`Psa__Halo__ClientId`, `Psa__Halo__ClientSecret`) or `dotnet user-secrets` locally, never commit
real values into `appsettings.json`.

- [ ] **Step 8: Run the full test suite and verify the solution builds**

Run: `dotnet build PsaToolAgent.sln`
Expected: Build succeeds, 0 errors, 0 warnings.

Run: `dotnet test PsaToolAgent.sln`
Expected: `Passed! - Failed: 0, Passed: 23, Skipped: 0, Total: 23`

- [ ] **Step 9: Commit**

```bash
git add src/PsaToolAgent/ test/PsaToolAgent.Tests/PollingBackgroundServiceTests.cs
git commit -m "Add PollingBackgroundService and wire the full pipeline in Program.cs"
```

---

### Task 8: Docker deployment and docs

**Files:**
- Create: `Dockerfile`
- Create: `.dockerignore`
- Create: `docker-compose.yml`
- Modify: `README.md`

**Interfaces:**
- Produces: a buildable, runnable Docker image and the deployment/config/least-privilege
  documentation the spec called for. No new application code.

- [ ] **Step 1: Write `Dockerfile`**

```dockerfile
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/PsaToolAgent/PsaToolAgent.csproj src/PsaToolAgent/
RUN dotnet restore src/PsaToolAgent/PsaToolAgent.csproj
COPY src/PsaToolAgent/ src/PsaToolAgent/
RUN dotnet publish src/PsaToolAgent/PsaToolAgent.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
RUN addgroup --system --gid 1000 psatool \
    && adduser --system --uid 1000 --ingroup psatool psatool
COPY --from=build /app .
USER psatool
ENTRYPOINT ["dotnet", "PsaToolAgent.dll"]
```

- [ ] **Step 2: Write `.dockerignore`**

```
bin/
obj/
test/
docs/
.git/
```

- [ ] **Step 3: Write `docker-compose.yml`**

```yaml
services:
  psatool-busybar-agent:
    build: .
    restart: unless-stopped
    # Linux Docker hosts: host networking is the simplest way to reach the BUSY Bar's
    # USB-Ethernet IP. See README.md#docker-networking for the Windows/Docker Desktop equivalent.
    network_mode: host
    environment:
      - Psa__Halo__ClientId=${HALO_CLIENT_ID}
      - Psa__Halo__ClientSecret=${HALO_CLIENT_SECRET}
      - Psa__Halo__BaseUrl=${HALO_BASE_URL}
      - BusyBar__Address=${BUSYBAR_ADDRESS:-10.0.4.20}
```

- [ ] **Step 4: Verify the image builds**

Run: `docker build -t psatool-busybar-agent .`
Expected: Build succeeds, final image runs `dotnet PsaToolAgent.dll` as a non-root user (verify
with `docker run --rm psatool-busybar-agent whoami` → `psatool`, not `root`).

- [ ] **Step 5: Expand `README.md`**

```markdown
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
- **Container**: the image runs as a non-root user (`psatool`, uid 1000) with no extra
  capabilities granted. Don't add `privileged: true` or extra `cap_add` entries to
  `docker-compose.yml` — nothing here needs them.

## Docker networking

The BUSY Bar presents as a USB-Ethernet adapter with its own IP (e.g. `10.0.4.20`) — reaching it
from a container is a network-routing question, not a USB-passthrough one.

- **Linux Docker host**: `network_mode: host` (already set in `docker-compose.yml`) is simplest —
  the container shares the host's network stack and reaches the BUSY Bar exactly как the host does.
- **Windows (Docker Desktop / WSL2)**: `network_mode: host` does **not** expose host-only adapters
  (like the BUSY Bar's USB-Ethernet interface) into the WSL2 VM automatically — WSL2 has its own
  network namespace. Two working options:
  1. **Bridge the adapter into WSL2**: share the BUSY Bar's USB-Ethernet adapter with the WSL2
     network via `wsl --shutdown` + a `.wslconfig` `[wsl2] networkingMode=mirrored` setting
     (Windows 11 23H2+), which mirrors host network interfaces (including the USB-Ethernet one)
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
```

- [ ] **Step 6: Add `.env.example`**

```
HALO_CLIENT_ID=
HALO_CLIENT_SECRET=
HALO_BASE_URL=https://your-tenant.halopsa.com/
BUSYBAR_ADDRESS=10.0.4.20
```

- [ ] **Step 7: Commit**

```bash
git add Dockerfile .dockerignore docker-compose.yml .env.example README.md
git commit -m "Add Docker deployment, least-privilege and networking docs"
```

---

## Out of Scope for This Plan

- The public Docusaurus docs site (`psatool-busybar-agent.homotechsual.dev`) — build this as a
  follow-up once the worker is running and stable, mirroring `busybar-dotnet`'s docs-site work
  (which was also done as a separate phase after the core library was complete).
- A second `IPsaDataProvider` implementation.
- Multiple BUSY Bar devices.
