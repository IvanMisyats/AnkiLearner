# AnkiLearner production image: Angular SPA + ASP.NET Core API in one container.
#
# Build from the REPOSITORY ROOT (it needs both backend/ and frontend/):
#   docker build -f backend/AnkiLearner.Api/Dockerfile -t ankilearner:local .
#
# Why the SPA ships inside the API image rather than being served by host nginx:
#   * single origin, so no CORS and the httpOnly+Secure refresh cookie keeps working;
#   * host nginx stays a pure proxy to 127.0.0.1:8081 and needs no read access into this app's
#     data directory (it shares the box with an unrelated application);
#   * CI never writes files onto the VPS — the only artefact it produces is this image.

# ── 1. Angular ───────────────────────────────────────────────────────
FROM node:22-alpine AS frontend
WORKDIR /src/frontend

# Copy manifests first so `npm ci` is cached until dependencies actually change.
COPY frontend/package.json frontend/package-lock.json ./
RUN npm ci

COPY frontend/ ./
RUN npm run build

# ── 2. .NET ──────────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend
WORKDIR /src

# Central package management: these must be present before restore.
COPY backend/Directory.Build.props backend/Directory.Packages.props backend/
COPY backend/AnkiLearner.Core/AnkiLearner.Core.csproj                     backend/AnkiLearner.Core/
COPY backend/AnkiLearner.Infrastructure/AnkiLearner.Infrastructure.csproj backend/AnkiLearner.Infrastructure/
COPY backend/AnkiLearner.Api/AnkiLearner.Api.csproj                       backend/AnkiLearner.Api/
RUN dotnet restore backend/AnkiLearner.Api/AnkiLearner.Api.csproj

COPY backend/ backend/
RUN dotnet publish backend/AnkiLearner.Api/AnkiLearner.Api.csproj \
      -c Release -o /app/publish --no-restore

# ── 3. Runtime ───────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Kestrel listens on 8080 in-container; the host maps it to 127.0.0.1:8081
# (8080 on the host belongs to the other application on this box).
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    DOTNET_gcServer=0

COPY --from=backend  /app/publish            ./
# @angular/build:application emits to dist/<project>/browser
COPY --from=frontend /src/frontend/dist/frontend/browser ./wwwroot

# The image declares no USER: in production it runs with `user: "0:0"` under a ROOTLESS Docker
# daemon, where container-root maps to an unprivileged host account and every capability is
# dropped by compose. Do not run this image on a rootful daemon without setting a user.
ENTRYPOINT ["dotnet", "AnkiLearner.Api.dll"]
