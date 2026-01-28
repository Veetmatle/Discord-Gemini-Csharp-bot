# Discord Bot AI - Docker Image
# Multi-stage build for optimized image size

# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY ["Discord Bot AI/Discord Bot AI.csproj", "Discord Bot AI/"]
RUN dotnet restore "Discord Bot AI/Discord Bot AI.csproj"

# Copy source code and build
COPY . .
WORKDIR "/src/Discord Bot AI"
RUN dotnet build -c Release -o /app/build

# Publish stage
FROM build AS publish
RUN dotnet publish -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final
WORKDIR /app

# Create non-root user for security
RUN groupadd -r botuser && useradd -r -g botuser botuser

# Create directories for volumes
RUN mkdir -p /app/data /app/cache /app/logs && \
    chown -R botuser:botuser /app

# Copy published application
COPY --from=publish /app/publish .

# Copy font assets (required for image rendering)
COPY ["Discord Bot AI/Assets/Fonts", "/app/Assets/Fonts"]

# Set ownership
RUN chown -R botuser:botuser /app

# Switch to non-root user
USER botuser

# Environment variables with defaults
ENV DOTNET_RUNNING_IN_CONTAINER=true
ENV DATA_PATH=/app/data
ENV CACHE_PATH=/app/cache
ENV LOG_PATH=/app/logs
ENV RIOT_VERSION=14.2.1

# Health check
HEALTHCHECK --interval=30s --timeout=10s --start-period=10s --retries=3 \
    CMD pgrep -f "Discord Bot AI" || exit 1

# Graceful shutdown signal
STOPSIGNAL SIGTERM

ENTRYPOINT ["dotnet", "Discord Bot AI.dll"]
