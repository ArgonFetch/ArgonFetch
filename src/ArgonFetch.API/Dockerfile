# Stage 1: Base runtime with dependencies
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
USER root
# Install dependencies and yt-dlp standalone binary
RUN apt-get update && \
    apt-get install -y --no-install-recommends \
        ffmpeg \
        python3 \
        curl \
        ca-certificates && \
    # Download yt-dlp standalone binary (still needs Python to run).
    # It lives in /opt/yt-dlp rather than /usr/local/bin because yt-dlp --update
    # replaces its own binary in place, which needs the directory and the file to be
    # writable by the runtime user ($APP_UID), not just by root.
    mkdir -p /opt/yt-dlp && \
    curl -L https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp -o /opt/yt-dlp/yt-dlp && \
    chmod 0755 /opt/yt-dlp/yt-dlp && \
    chown -R $APP_UID:0 /opt/yt-dlp && \
    ln -s /opt/yt-dlp/yt-dlp /usr/local/bin/yt-dlp && \
    # Verify installations
    yt-dlp --version && \
    ffmpeg -version && \
    # Clean up apt cache and curl
    apt-get remove -y curl && \
    apt-get autoremove -y && \
    rm -rf /var/lib/apt/lists/* /tmp/* /var/tmp/*
USER $APP_UID
EXPOSE 8080 8081

# Stage 2: Backend dependencies restore
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-deps
WORKDIR /src
# Copy version properties file
COPY application.properties /application.properties
# Copy all csproj files for better caching
COPY ["src/ArgonFetch.API/ArgonFetch.API.csproj", "src/ArgonFetch.API/"]
COPY ["src/ArgonFetch.Application/ArgonFetch.Application.csproj", "src/ArgonFetch.Application/"]
COPY ["src/ArgonFetch.Infrastructure/ArgonFetch.Infrastructure.csproj", "src/ArgonFetch.Infrastructure/"]
COPY ["src/ArgonFetch.Domain/ArgonFetch.Domain.csproj", "src/ArgonFetch.Domain/"]
RUN dotnet restore "src/ArgonFetch.API/ArgonFetch.API.csproj"

# Stage 3: Backend build
FROM backend-deps AS backend-build
ARG BUILD_CONFIGURATION=Release
COPY ./src .
WORKDIR "/src/ArgonFetch.API"
RUN dotnet publish "ArgonFetch.API.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false

# Stage 4: Frontend dependencies
# Bun is the package manager and script runner, but the Angular CLI still runs on Node
# and enforces a minimum version. The oven/bun image ships Node v24.3, which Angular 22
# rejects (it wants >= v24.15), so the bun binary is copied onto a current Node base
# instead. No npm is involved.
FROM oven/bun:1-alpine AS bun
FROM node:24-alpine AS frontend-deps
COPY --from=bun /usr/local/bin/bun /usr/local/bin/bun
WORKDIR /src/frontend
COPY src/ArgonFetch.Frontend/package.json src/ArgonFetch.Frontend/bun.lock ./
# --frozen-lockfile is the equivalent of `npm ci`: fail rather than resolve anew.
RUN bun install --frozen-lockfile

# Stage 5: Frontend build
FROM frontend-deps AS frontend-build
COPY src/ArgonFetch.Frontend .
RUN bun run build:agent --output-path=/.artifacts/frontend

# Stage 6: Final image
FROM base AS final
WORKDIR /app
COPY --from=backend-build /app/publish .
COPY --from=frontend-build /.artifacts/frontend ./wwwroot
ENTRYPOINT ["dotnet", "ArgonFetch.API.dll"]