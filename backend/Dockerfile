# Image du backend Blindify (ASP.NET Core + SignalR) — voir docs/architecture.md section 2.
# Seul le backend est dockerisé (jamais le frontend, voir CLAUDE.md). Les données (tracks.json,
# stats.json, audio, covers) ne sont jamais copiées dans l'image : elles sont montées en volume
# par docker-compose.yml, à la racine du repo.

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copie des .csproj seuls d'abord pour que `dotnet restore` profite du cache Docker tant que les
# dépendances ne changent pas, même si le code source change.
COPY src/Blindify.Domain/Blindify.Domain.csproj src/Blindify.Domain/
COPY src/Blindify.Application/Blindify.Application.csproj src/Blindify.Application/
COPY src/Blindify.Infrastructure/Blindify.Infrastructure.csproj src/Blindify.Infrastructure/
COPY src/Blindify.Api/Blindify.Api.csproj src/Blindify.Api/
RUN dotnet restore src/Blindify.Api/Blindify.Api.csproj

COPY src/ src/
RUN dotnet publish src/Blindify.Api/Blindify.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app .

# Port interne du conteneur — republié en 5000 côté LAN via docker-compose (architecture.md section 2).
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "Blindify.Api.dll"]
