# ─── CACHE BUST ──────────────────────────────────────────────────────────────
# Bumping CACHE_BUST below forces Docker to invalidate every cache layer
# downstream — use this when slice-backend changes are not being picked up by
# the registry. The default of 1 is harmless; CI overrides it to the commit SHA.
# 2026-06-09-bust-13: arreglar el bug real de la descarga. La causa
# raiz es que los exports se escriben a /tmp/slice/exports y al
# reiniciarse el contenedor del slice-api (cosa que pasa en cada
# redeploy, incluido el propio bust-12) el archivo en /tmp desaparece
# pero la fila de la DB sigue apuntando a esa ruta, por lo que el
# PhysicalFile devuelve 404 silencioso y el frontend se lo come.
# Ahora los exports van a /data/slice/exports (volume persistente) con
# fallback a /tmp en local dev. Ademas se agrega request logging
# global en slice-api y console.info en el frontend para que el
# proximo test deje un rastro claro.
ARG CACHE_BUST=2026-06-09-bust-13

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

# nginx + supervisord
COPY nginx-combined.conf /etc/nginx/sites-enabled/default
COPY supervisord.conf    /etc/supervisor/conf.d/supervisord.conf

EXPOSE 80
CMD ["/usr/bin/supervisord", "-c", "/etc/supervisor/conf.d/supervisord.conf"]
