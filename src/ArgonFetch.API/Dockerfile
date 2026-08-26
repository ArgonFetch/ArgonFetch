# Stage 1: Base runtime
# Azure Linux rather than Debian: the image no longer installs anything, so all it has to
# provide is glibc and CA certificates, and this base is smaller and carries fewer CVEs.
# Alpine would not work - the static yt-dlp and FFmpeg builds fetched below are glibc.
FROM mcr.microsoft.com/dotnet/aspnet:10.0-azurelinux3.0 AS base
WORKDIR /app
USER root

# No media tooling is baked in. yt-dlp breaks whenever the sites it extracts from change, so
# a binary built into the image is stale before it ships; the app fetches both tools on boot
# and reports itself as under maintenance until they are ready. Written here and replaced in
# place by yt-dlp's own updater, so the directory has to be writable by the runtime user.
#
# Mount a volume over /tools to keep them across restarts - without one, roughly 100MB is
# re-fetched every time the container starts.
ENV TOOLS_PATH=/tools
RUN mkdir -p /tools && chown -R $APP_UID:0 /tools && chmod 0775 /tools
VOLUME /tools

# The fetched FFmpeg is built against OpenSSL's own default paths, which are Debian's and do
# not exist here, so every HTTPS input failed certificate verification. Pointing it at this
# image's bundle fixes that; the CA data itself is already in the base image.
ENV SSL_CERT_FILE=/etc/pki/tls/certs/ca-bundle.crt
ENV SSL_CERT_DIR=/etc/pki/tls/certs

USER $APP_UID
EXPOSE 8080 8081

# Stage 2: Backend dependencies restore
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS backend-deps
WORKDIR /src
# Directory.Build.props stamps the version from application.properties into every
# assembly. MSBuild finds it by walking up from each project, so both files have to sit
# above the projects inside the image - copying application.properties to / (as this
# previously did) put it somewhere nothing reads, and left every assembly on the SDK
# default 1.0.0.
COPY Directory.Build.props application.properties ./
# Copy all csproj files for better caching
COPY ["src/ArgonFetch.API/ArgonFetch.API.csproj", "src/ArgonFetch.API/"]
COPY ["src/ArgonFetch.Application/ArgonFetch.Application.csproj", "src/ArgonFetch.Application/"]
COPY ["src/ArgonFetch.Infrastructure/ArgonFetch.Infrastructure.csproj", "src/ArgonFetch.Infrastructure/"]
COPY ["src/ArgonFetch.Domain/ArgonFetch.Domain.csproj", "src/ArgonFetch.Domain/"]
RUN dotnet restore "src/ArgonFetch.API/ArgonFetch.API.csproj"

# Stage 3: Backend build
FROM backend-deps AS backend-build
ARG BUILD_CONFIGURATION=Release
# Set by the release workflow from the git tag. Empty for local builds, where the
# version falls back to application.properties.
ARG APP_VERSION=
COPY ./src .
WORKDIR "/src/ArgonFetch.API"
RUN dotnet publish "ArgonFetch.API.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false \
    ${APP_VERSION:+/p:Version=$APP_VERSION}

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