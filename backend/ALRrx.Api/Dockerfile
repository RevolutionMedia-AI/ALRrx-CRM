FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ALRrx.sln ./
COPY ALRrx.Domain/*.csproj ALRrx.Domain/
COPY ALRrx.Application/*.csproj ALRrx.Application/
COPY ALRrx.Infrastructure/*.csproj ALRrx.Infrastructure/
COPY ALRrx.Api/*.csproj ALRrx.Api/
RUN dotnet restore

COPY . .
RUN dotnet publish ALRrx.Api/ALRrx.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
EXPOSE 8080
COPY --from=build /app .
ENTRYPOINT ["dotnet", "ALRrx.Api.dll"]
