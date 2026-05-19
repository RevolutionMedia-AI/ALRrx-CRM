FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY backend/ALRrx.sln ./
COPY backend/ALRrx.Domain/*.csproj ALRrx.Domain/
COPY backend/ALRrx.Application/*.csproj ALRrx.Application/
COPY backend/ALRrx.Infrastructure/*.csproj ALRrx.Infrastructure/
COPY backend/ALRrx.Api/*.csproj ALRrx.Api/
RUN dotnet restore

COPY backend/ .
RUN dotnet publish ALRrx.Api/ALRrx.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
EXPOSE 8080
COPY --from=build /app .
ENTRYPOINT ["dotnet", "ALRrx.Api.dll"]
