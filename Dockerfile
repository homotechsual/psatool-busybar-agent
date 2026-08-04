FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/PsaToolAgent/PsaToolAgent.csproj src/PsaToolAgent/
RUN dotnet restore src/PsaToolAgent/PsaToolAgent.csproj
COPY src/PsaToolAgent/ src/PsaToolAgent/
RUN dotnet publish src/PsaToolAgent/PsaToolAgent.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app .
USER app
ENTRYPOINT ["dotnet", "PsaToolAgent.dll"]
