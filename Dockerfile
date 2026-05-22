FROM node:20-alpine AS frontend
WORKDIR /app
ARG VITE_GOOGLE_CLIENT_ID
ENV VITE_GOOGLE_CLIENT_ID=${VITE_GOOGLE_CLIENT_ID}
COPY frontend/package*.json ./
RUN npm ci
COPY frontend/ .
RUN npm run build

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend
WORKDIR /src
COPY . .
RUN dotnet restore backend/ALRrx.sln
RUN dotnet publish backend/ALRrx.Api/ALRrx.Api.csproj -c Release -o /publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0
RUN apt-get update && apt-get install -y nginx && rm -rf /var/lib/apt/lists/*
COPY --from=frontend /app/dist /usr/share/nginx/html
COPY nginx-combined.conf /etc/nginx/sites-enabled/default
COPY --from=backend /publish /app
WORKDIR /app
EXPOSE 80
ENV PORT=5000
CMD nginx -g 'daemon off;' & dotnet ALRrx.Api.dll
