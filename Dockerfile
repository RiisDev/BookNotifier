FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY ["BookNotifier.csproj", "."]
COPY ["Integrations/ScribbleHub.Project/ScribbleHub.Project.csproj", "Integrations/ScribbleHub.Project/"]

RUN dotnet restore "BookNotifier.csproj"
RUN dotnet restore "Integrations/ScribbleHub.Project/ScribbleHub.Project.csproj"

COPY . .

RUN dotnet publish "BookNotifier.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
RUN dotnet publish "Integrations/ScribbleHub.Project/ScribbleHub.Project.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "BookNotifier.dll"]