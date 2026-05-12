# EasySave v3 — LogCentralizer service image.
#
# The CdC v3 requires "un seul et unique fichier journalier quel que soit le
# nombre d'utilisateurs". This image hosts the ASP.NET Core minimal API that
# fan-ins LogEntry POSTs from every workstation and persists them as one
# JSON Lines file per day. Run a SINGLE replica behind a TCP load balancer
# if you need redundancy at the LB layer — scaling out replicas would race
# concurrent appends on the shared volume.
#
# Build context: the REPO ROOT (not LogCentralizer/), so the multi-stage
# restore step can grab EasyLog.csproj alongside LogCentralizer.csproj.
# Build command from the repo root:
#   docker build -f LogCentralizer/Dockerfile -t logcentralizer:v3 .

# ──────────────────────────────────────────────────────────────────────────────
# Stage 1 — restore + publish against the full .NET 8 SDK
# ──────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Project files first so `dotnet restore` is cached as long as deps don't
# change. LogCentralizer references EasyLog; Directory.Build.props pins the
# shared TargetFramework / Nullable settings.
COPY Directory.Build.props ./
COPY LogCentralizer/LogCentralizer.csproj LogCentralizer/
COPY src/EasyLog/EasyLog.csproj          src/EasyLog/

RUN dotnet restore LogCentralizer/LogCentralizer.csproj

# Copy the sources for the two projects we actually build. The other
# projects in the solution (EasySave, EasySave.UI, tests, …) are excluded
# by .dockerignore so they never enter the build context.
COPY LogCentralizer/ LogCentralizer/
COPY src/EasyLog/    src/EasyLog/
COPY docs/schemas/   docs/schemas/

RUN dotnet publish LogCentralizer/LogCentralizer.csproj \
        -c Release \
        -o /app/publish \
        --no-restore \
        /p:UseAppHost=false

# ──────────────────────────────────────────────────────────────────────────────
# Stage 2 — slim ASP.NET Core runtime image
# ──────────────────────────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# The aspnet:8.0 base is Debian slim and ships with neither curl nor wget.
# Install curl so docker-compose can run an HTTP healthcheck against /health
# without needing a bespoke probe binary inside the image. --no-install-
# recommends keeps the layer small (~5 MB).
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish ./

# Run as a non-root user. The aspnet:8.0 image ships with the
# `app` user (UID 1654 on .NET 8). Pre-create the logs dir with the
# right owner so the writer task can append on first request without
# needing to chown at runtime.
#
# IMPORTANT: this RUN MUST come BEFORE the VOLUME instruction. Docker
# silently discards filesystem changes made on a path after it has
# been declared as a volume — so moving these two lines would leave
# /var/log/easysave owned by root and break writes when no host bind
# mount is provided (anonymous-volume case).
RUN install -d -o app -g app /var/log/easysave

# The single daily file lives here. Operators bind-mount the host
# directory (./logs/) onto this path via docker-compose.
VOLUME ["/var/log/easysave"]

USER app

# Listen on every interface inside the container, port 8080. The host
# port is chosen by the compose / docker run command — the container
# port is fixed so the healthcheck and the upstream LB can rely on it.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "LogCentralizer.dll"]
