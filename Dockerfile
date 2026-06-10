# ─── CACHE BUST ──────────────────────────────────────────────────────────────
# Bumping CACHE_BUST below forces Docker to invalidate every cache layer
# downstream — use this when slice-backend changes are not being picked up by
# the registry. The default of 1 is harmless; CI overrides it to the commit SHA.
# 2026-06-09-bust-16: arreglar el join imposible entre los dos CSVs de shop.
# El usuario subio un ZIP con 12 CSVs. shop_level_-_call_metrics.csv usa un
# esquema de ShopId (e.g. 853=Papa Ray's, 73=Capri) y shop_level_-_orders_metrics.csv
# usa OTRO esquema (e.g. 112770=Cj's Pizza, 112919=Capri's) — los rangos no
# se solapan. El codigo hacia un join por ShopId entre ShopCallMetrics y
# ShopDaily, asi que los 4 campos de orders del Global siempre quedaban en
# 0 y el bloque Shop del export solo mostraba la fila virtual
# EXTERNAL_OVERFLOW_DAILY. Cambios:
# (1) BackfillOrderMetricsFromShopDaily ahora usa ShopName (normalizado,
# case-insensitive, sin sufijos 'pizza'/'pizzeria'/'llc'/'inc'/'co'/
# 'restaurant'/'kitchen') como puente entre DailyGlobal.Pod y ShopDaily
# en lugar de ShopId.
# (2) ParseShopDailySection reescrito: lee el header hibrido de 18 cols
# del Excel del usuario (3 cols de shop + 11 de call metrics + 4 de orders)
# y popula TANTO ShopCallMetrics como ShopDaily, descartando la fila virtual
# EXTERNAL_OVERFLOW_DAILY para no duplicarla.
# (3) El bloque Shop del XLSX/CSV export ahora se divide en dos sub-tablas
# independientes: 'Shop Call Metrics (by shop, pod, week)' y
# 'Shop Orders (by shop)'. Ya no intenta cruzar por ShopId.
# Bump CACHE_BUST a 2026-06-09-bust-16.
ARG CACHE_BUST=2026-06-09-bust-16

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
