# Base runtime image
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build

ARG BUILD_CONFIGURATION=Release

WORKDIR /src

# Copy csproj and restore
COPY ["BookNotifier.csproj", "./"]
RUN dotnet restore "./BookNotifier.csproj"

# Copy everything else
COPY . .

# Build
RUN dotnet build "./BookNotifier.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/build

# Publish stage
FROM build AS publish

ARG BUILD_CONFIGURATION=Release

RUN dotnet publish "./BookNotifier.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false

# Final runtime image
FROM base AS final

WORKDIR /app

COPY --from=publish /app/publish .

ENTRYPOINT ["dotnet", "BookNotifier.dll"]