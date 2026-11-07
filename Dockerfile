# Build
#
# On the machine doing the building, whatever the image is for: the SDK compiles for the target
# with -a rather than being run under emulation, which for an arm64 image built on an amd64
# runner (the published image) is minutes against the better part of an hour, and is known to
# fall over. Building on the Pi itself, the two are the same and nothing changes.
#
# TARGETARCH comes from BuildKit. The ${...:+...} form leaves the flag out entirely without it,
# so a builder that does not set it still gets the plain build it always got.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG TARGETARCH
WORKDIR /src

# Restore first so the package layer is cached across source changes.
COPY Simplz.Babytracker.csproj .
RUN dotnet restore ${TARGETARCH:+-a $TARGETARCH}

COPY . .
# Note: no --no-restore here. Publishing with it skips the framework's static web assets,
# which leaves _framework/blazor.web.js out of the published asset manifest (404 at runtime).
# Framework-dependent, said explicitly: naming an architecture must not make it self-contained,
# because the runtime image below already carries the runtime.
RUN dotnet publish -c Release -o /app ${TARGETARCH:+-a $TARGETARCH} --self-contained false

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

# Ties the published image to this repository on GitHub, which is what links the package to it
# and lets the repository's own permissions cover it.
LABEL org.opencontainers.image.source="https://github.com/kapiosk/Simplz.Babytracker" \
      org.opencontainers.image.description="A self-hosted newborn tracker: feeds, sleep, nappies, pumping, health." \
      org.opencontainers.image.licenses="MIT"

# tzdata so the TZ environment variable actually shifts the displayed times.
RUN apt-get update \
    && apt-get install -y --no-install-recommends tzdata curl \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Default="Data Source=/data/babytracker.db" \
    TZ=UTC

# SQLite file lives on a mounted volume, owned by the non-root app user.
RUN mkdir -p /data && chown -R $APP_UID:$APP_UID /data
VOLUME /data
USER $APP_UID

EXPOSE 8080
HEALTHCHECK --interval=30s --timeout=3s --start-period=10s \
    CMD curl -fsS http://localhost:8080/healthz || exit 1

ENTRYPOINT ["dotnet", "Simplz.Babytracker.dll"]
