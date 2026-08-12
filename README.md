# psatool-busybar-agent

A .NET Worker Service that polls a pluggable PSA provider (Halo and Gorelo) and drives a physical
BUSY Bar with a priority-ranked helpdesk dashboard. Consumes the
[`BusyBar`](https://www.nuget.org/packages/BusyBar) NuGet package.

Full docs — configuration reference, least-privilege setup, provider notes, and Docker
networking — live at
[psatool.busy.homotechsual.dev](https://psatool.busy.homotechsual.dev).

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

See [Deployment](https://psatool.busy.homotechsual.dev/deployment) for least-privilege guidance
and Docker networking on Windows.
