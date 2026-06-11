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
# 2026-06-11-bust-28: Fix CRITICO bug de Twilio - el SDK
#     CallResource.ReadAsync IGNORA los filtros de fecha y devuelve
#     las primeras 1000 calls sin importar el periodo (por eso
#     today/week/month devolvian el mismo totalCost=10.6875). Reescrito
#     FetchAllCallsAsync usando HttpClient directo con query params
#     StartTime= y EndTime= (formato ISO 8601 UTC) + paginacion manual
#     via next_page_uri. Parseo manual del JSON a TwilioCallDto.
# Bump CACHE_BUST a 2026-06-11-bust-28.
ARG CACHE_BUST=2026-06-11-bust-28

# ─── Stage 1: Build React frontend (ALRrx + Slice) ───────────────────────────
FROM node:20-alpine AS frontend
WORKDIR /app
ARG CACHE_BUST
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ .
RUN npm run build

# ─── Stage 2a: Build ALRrx.Api ────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS alrrx-api
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
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS slice-api
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
FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update && apt-get install -y nginx supervisor && rm -rf /var/lib/apt/lists/*
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
