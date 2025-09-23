# Stage 1: Base runtime with dependencies
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
WORKDIR /app
USER root
# Install dependencies in one layer with cleanup
RUN apt-get update && \
    apt-get install -y --no-install-recommends ffmpeg wget ca-certificates && \
    wget -q -O /usr/local/bin/yt-dlp https://github.com/yt-dlp/yt-dlp/releases/latest/download/yt-dlp && \
    chmod a+rx /usr/local/bin/yt-dlp && \
    apt-get remove -y wget && \
    apt-get autoremove -y && \
    rm -rf /var/lib/apt/lists/* /tmp/* /var/tmp/*
USER $APP_UID
EXPOSE 8080 8081

# Stage 2: Backend dependencies restore
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-deps
WORKDIR /src
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
FROM node:18-alpine AS frontend-deps
WORKDIR /src/frontend
COPY src/ArgonFetch.Frontend/package*.json ./
RUN npm ci --prefer-offline --no-audit --no-fund

# Stage 5: Frontend build
FROM frontend-deps AS frontend-build
COPY src/ArgonFetch.Frontend .
RUN npm run build:agent -- --output-path=/.artifacts/frontend

# Stage 6: Final image
FROM base AS final
WORKDIR /app
COPY --from=backend-build /app/publish .
COPY --from=frontend-build /.artifacts/frontend ./wwwroot
ENTRYPOINT ["dotnet", "ArgonFetch.API.dll"]