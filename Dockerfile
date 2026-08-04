FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/PsaToolAgent/ src/PsaToolAgent/
# A separate `dotnet restore` layer followed by `dotnet publish --no-restore` is a common caching
# optimization, but it's unreliable with this project's current package set on SDK 10.0.302 — a
# fresh restore followed by --no-restore publish fails (confirmed reproducible both in this
# container and directly on a Windows host, with different symptoms in each: NETSDK1064 here,
# MSB3094 on Windows). Restoring as part of publish avoids the split entirely.
RUN dotnet publish src/PsaToolAgent/PsaToolAgent.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "PsaToolAgent.dll"]
