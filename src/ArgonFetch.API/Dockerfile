FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS base
USER $APP_UID
WORKDIR /app
EXPOSE 8080
EXPOSE 8081

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS backend-build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["src/ArgonFetch.API/ArgonFetch.API.csproj", "src/ArgonFetch.API/"]
RUN dotnet restore "src/ArgonFetch.API/ArgonFetch.API.csproj"
COPY ./src .
WORKDIR "/src/ArgonFetch.API"
RUN dotnet build "ArgonFetch.API.csproj" -c $BUILD_CONFIGURATION -o /app/build

# Frontend build
FROM node:18 AS frontend-build
WORKDIR /src/frontend
RUN npm install -g @angular/cli@19
COPY src/ArgonFetch.Frontend/package*.json .
RUN npm ci
COPY src/ArgonFetch.Frontend .
RUN npm run build:agent --output-path /.artifacts/frontend

FROM backend-build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "ArgonFetch.API.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false
COPY --from=frontend-build /.artifacts/frontend /app/publish/wwwroot

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "ArgonFetch.API.dll"]
