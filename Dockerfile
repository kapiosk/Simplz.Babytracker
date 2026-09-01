# Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first so the package layer is cached across source changes.
COPY Simplz.Babytracker.csproj .
RUN dotnet restore

COPY . .
# Note: no --no-restore here. Publishing with it skips the framework's static web assets,
# which leaves _framework/blazor.web.js out of the published asset manifest (404 at runtime).
RUN dotnet publish -c Release -o /app

# Run
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final

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
