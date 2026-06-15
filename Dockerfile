# ─── CACHE BUST ──────────────────────────────────────────────────────────────
# Bumping CACHE_BUST below forces Docker to invalidate every cache layer
# downstream — use this when slice-backend changes are not being picked up by
# the registry. The default of 1 is harmless; CI overrides it to the commit SHA.
# 2026-06-09-bust-17: unificar el JWT entre alrrx y slice para que un solo
# login sirva en ambos backends. Antes cada backend firmaba con su propio
# Key/Issuer/Audience, asi que un token de slice era invalido en alrrx y
# viceversa, causando el bucle infinito 'login slice -> navegar a alrrx ->
# 401 -> redirect a login' para los 4 admins. Cambios:
# (1) slice-backend/Slice.Api/appsettings.json: Jwt.Key, Jwt.Issuer y
#     Jwt.Audience ahora coinciden con backend/ALRrx.Api/appsettings.json.
# (2) frontend/src/utils/sharedToken.ts (nuevo): expone readSharedToken /
#     writeSharedToken / clearSharedToken que leen y escriben una sola
#     key 'auth_token' y la espejean en 'alrrx_token' y 'slice_token' para
#     compatibilidad hacia atras. Cualquier login en cualquiera de los dos
#     backends popula el mismo token.
# (3) AuthContext y SliceAuthContext: ambos leen de readSharedToken y
#     escriben via writeSharedToken. Asi si slice ya hizo login, el
#     AuthContext de alrrx ve el mismo token y no fuerza re-login.
# (4) httpClient.ts y sliceHttpClient.ts: ya no hacen hard-redirect a
#     /login en 401. Solo limpian el Authorization header y el token
#     compartido; el AuthContext decide si mostrar el login.
# (5) Componentes que leian localStorage.getItem('slice_token') ahora
#     usan readSharedToken().
# 2026-06-11-bust-33: Reescribir TwilioCostsPage.tsx con encoding UTF-8
#     correcto. El archivo en git tenia mojibake (Â·, â€", â”€) por uso
#     de PowerShell Set-Content con encoding incorrecto. Reemplazados
#     todos los caracteres especiales (—, ·) con ASCII simple (-) para
#     evitar futuros problemas de encoding entre Windows y Linux.
# 2026-06-15-bust-37: Install libicu-dev alongside ca-certificates.
#     The slim debian image is missing the ICU library, so the
#     first `dotnet restore` call crashed with "Couldn't find a
#     valid ICU package installed on the system" (a hard
#     FailFast inside System.TimeZoneInfo..cctor) — the .NET
#     runtime needs ICU to load timezones for globalization.
#     libicu-dev is the package name in Debian 12 / bookworm.
#     Bump CACHE_BUST to 2026-06-15-bust-37.
# Bump CACHE_BUST a 2026-06-15-bust-37.
ARG CACHE_BUST=2026-06-15-bust-37

# ─── Stage 0: Install .NET 8 SDK + ASP.NET Core 8 runtime ───────────────────
# Single shared installer stage. Pulls both the SDK (needed by the
# build stages below) and the ASP.NET Core runtime (needed by the
# final runtime image). Installed via Microsoft's official script so
# we don't depend on MCR's docker registry.
#
# DEBIAN_FRONTEND=noninteractive + apt-utils are both required: without
# them, the apt install of ca-certificates / openssl / krb5 hangs on
# debconf prompts that the slim image can't answer, and the chained
# `dotnet-install.sh` never runs.
FROM debian:bookworm-slim AS dotnet-installer
ARG CACHE_BUST
ENV DEBIAN_FRONTEND=noninteractive
RUN apt-get update && apt-get install -y --no-install-recommends \
        apt-utils ca-certificates curl libicu-dev && \
    rm -rf /var/lib/apt/lists/* && \
    curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh && \
    chmod +x /tmp/dotnet-install.sh && \
    /tmp/dotnet-install.sh --channel 8.0            --install-dir /usr/share/dotnet && \
    /tmp/dotnet-install.sh --channel 8.0 --runtime aspnetcore --install-dir /usr/share/dotnet && \
    rm /tmp/dotnet-install.sh
ENV DOTNET_ROOT=/usr/share/dotnet
ENV PATH=$PATH:/usr/share/dotnet \
    DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1 \
    DOTNET_SKIP_FIRST_TIME_EXPERIENCE=1

# ─── Stage 1: Build React frontend (ALRrx + Slice) ───────────────────────────
FROM node:20-alpine AS frontend
WORKDIR /app
ARG CACHE_BUST
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ .
RUN npm run build

# ─── Stage 2a: Build ALRrx.Api ────────────────────────────────────────────────
FROM dotnet-installer AS alrrx-api
ARG CACHE_BUST
WORKDIR /src
COPY backend/ALRrx.sln ./
COPY backend/ALRrx.Api/        ALRrx.Api/
COPY backend/ALRrx.Application/ ALRrx.Application/
COPY backend/ALRrx.Domain/     ALRrx.Domain/
COPY backend/ALRrx.Infrastructure/ ALRrx.Infrastructure/
RUN dotnet restore ALRrx.sln
COPY backend/ .
RUN dotnet publish ALRrx.Api/ALRrx.Api.csproj -c Release -o /publish-alrrx

# ─── Stage 2b: Build Slice.Api ────────────────────────────────────────────────
FROM dotnet-installer AS slice-api
ARG CACHE_BUST
WORKDIR /src
COPY slice-backend/Slice.sln ./
COPY slice-backend/Slice.Api/             Slice.Api/
COPY slice-backend/Slice.Application/     Slice.Application/
COPY slice-backend/Slice.Domain/         Slice.Domain/
COPY slice-backend/Slice.Infrastructure/  Slice.Infrastructure/
RUN dotnet restore Slice.sln
COPY slice-backend/ .
RUN dotnet publish Slice.Api/Slice.Api.csproj -c Release -o /publish-slice

# ─── Stage 3: Runtime image (nginx + 2 dotnet apps managed by supervisord) ────
FROM dotnet-installer
RUN apt-get update && apt-get install -y --no-install-recommends nginx supervisor && rm -rf /var/lib/apt/lists/*
# Ensure the SQLite path exists so the slice-api can create /data/slice.db on
# first boot even if the Northflank volume is mounted slightly later.
RUN mkdir -p /data && chmod 0777 /data

# Static SPA
COPY --from=frontend /app/dist /usr/share/nginx/html

# ALRrx API on :5000
COPY --from=alrrx-api /publish-alrrx /app/alrrx
ENV ALRRX__URLS=http://0.0.0.0:5000

# Slice API on :5001
COPY --from=slice-api /publish-slice /app/slice

# .NET runtime tweaks to keep memory pressure low in small Northflank containers:
# - gcServer=0: workstation GC, no separate background GC thread per core
# - GCConserveMemory=9: be aggressive about reclaiming unused segments
# - GCHeapHardLimit: cap the managed heap so the kernel OOM-kill is more
#   predictable (the slice-api only needs ~120MB for its working set)
ENV DOTNET_gcServer=0 \
    DOTNET_GCConserveMemory=9 \
    DOTNET_GCHeapHardLimit=0_C000000 \
    DOTNET_GCRetainVM=0

# nginx + supervisord
COPY nginx-combined.conf /etc/nginx/sites-enabled/default
COPY supervisord.conf    /etc/supervisor/conf.d/supervisord.conf

EXPOSE 80
CMD ["/usr/bin/supervisord", "-c", "/etc/supervisor/conf.d/supervisord.conf"]
