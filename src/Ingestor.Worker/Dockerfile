# Stage 1 – Build & Publish
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /source

# Solution-level MSBuild defaults (global.json is intentionally excluded –
# the SDK version is controlled by the image tag, not the local pin)
COPY Directory.Build.props .

# Project files + lock files first – restore layer is cached
# until any .csproj or packages.lock.json changes
COPY src/Ingestor.Domain/Ingestor.Domain.csproj            src/Ingestor.Domain/
COPY src/Ingestor.Domain/packages.lock.json                src/Ingestor.Domain/
COPY src/Ingestor.Application/Ingestor.Application.csproj  src/Ingestor.Application/
COPY src/Ingestor.Application/packages.lock.json           src/Ingestor.Application/
COPY src/Ingestor.Infrastructure/Ingestor.Infrastructure.csproj src/Ingestor.Infrastructure/
COPY src/Ingestor.Infrastructure/packages.lock.json        src/Ingestor.Infrastructure/
COPY src/Ingestor.Worker/Ingestor.Worker.csproj            src/Ingestor.Worker/
COPY src/Ingestor.Worker/packages.lock.json                src/Ingestor.Worker/

RUN dotnet restore src/Ingestor.Worker/Ingestor.Worker.csproj --locked-mode

# Full source – copied after restore so the restore layer stays cached
COPY src/ src/

RUN dotnet publish src/Ingestor.Worker/Ingestor.Worker.csproj \
    -c Release \
    --no-restore \
    -o /app/publish

# Stage 2 – Runtime (ASP.NET Core, no SDK, no source code)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
# libgssapi-krb5-2 is required by Npgsql for GSSAPI support at startup
RUN apt-get update && apt-get install -y --no-install-recommends  \
    libgssapi-krb5-2 \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Ingestor.Worker.dll"]