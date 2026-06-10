# ─── CACHE BUST ──────────────────────────────────────────────────────────────
# Bumping CACHE_BUST below forces Docker to invalidate every cache layer
# downstream — use this when slice-backend changes are not being picked up by
# the registry. The default of 1 is harmless; CI overrides it to the commit SHA.
# 2026-06-09-bust-15: arreglar dos bugs en el parser / merge del Excel.
# (1) Las 4 metricas de orders del bloque Global (Conv %, Order Count,
# Refunded Orders, % Orders with errors) siempre quedaban en 0 porque
# ParseDailyGlobalSection las leia del Excel y devolvia ceros cuando
# el archivo no las traia. Ahora Merge() llama a
# BackfillOrderMetricsFromShopDaily, que cruza DailyGlobal.Pod con
# ShopCallMetrics (puente Pod <-> ShopId) para derivar OrderCount,
# RefundedOrders y PctOrdersWithErrors de ShopDaily.
# (2) Desde el commit 2b64547 (offset del bloque Shop, columna A
# como margen) el bloque Shop no se detectaba porque el parser buscaba
# 'Shop Daily' solo en columna A. Ahora ParseRowGrid escanea todas
# las celdas de cada fila y tambien acepta el formato corto donde el
# titulo 'Shop' (o 'Global' / 'Agent') esta centrado en una sola
# celda de la fila, sin tocar la columna A.
ARG CACHE_BUST=2026-06-09-bust-15

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
