# Синхронный REST API. PHP-сайт собирается отдельно из php-site/.
FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
ARG BUILD_CONFIGURATION=Release
ARG NUGET_AUDIT=true
WORKDIR /src
COPY ["MotivCellScopeApiServices/MotivCellScopeApiServices.csproj", "MotivCellScopeApiServices/"]
RUN --mount=type=cache,target=/root/.nuget/packages dotnet restore "MotivCellScopeApiServices/MotivCellScopeApiServices.csproj" -p:NuGetAudit=$NUGET_AUDIT --ignore-failed-sources
COPY . .
WORKDIR /src/MotivCellScopeApiServices
RUN --mount=type=cache,target=/root/.nuget/packages dotnet build "MotivCellScopeApiServices.csproj" -c $BUILD_CONFIGURATION -o /app/build --no-restore

FROM build AS publish
RUN --mount=type=cache,target=/root/.nuget/packages dotnet publish "MotivCellScopeApiServices.csproj" -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM base AS final
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MotivCellScopeApiServices.dll"]
