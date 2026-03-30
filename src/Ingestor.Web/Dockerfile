# Stage 1 – Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

COPY Directory.Build.props .

# RestorePackagesWithLockFile=false for Ingestor.Web (Blazor implicit framework
# assets are only resolved with full source present, not in the restore-only stage)
COPY src/Ingestor.Contracts/Ingestor.Contracts.csproj     src/Ingestor.Contracts/
COPY src/Ingestor.Contracts/packages.lock.json             src/Ingestor.Contracts/
COPY src/Ingestor.Web/Ingestor.Web.csproj                  src/Ingestor.Web/

COPY src/ src/

RUN dotnet publish src/Ingestor.Web/Ingestor.Web.csproj \
    -c Release \
    -o /app/publish

# Stage 2 – Runtime (ASP.NET Core, no SDK, no source code)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Ingestor.Web.dll"]
