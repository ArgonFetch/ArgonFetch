# Developer setup

Running ArgonFetch from source means three pieces: a Postgres container, the .NET API and the
Angular frontend.

## Prerequisites

- Git
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Bun](https://bun.sh) 1.x
- Docker (for the development database)
- `dotnet-ef` (`dotnet tool install --global dotnet-ef`)

`FFmpeg` and `yt-dlp` are fetched by the API at startup, the same way they are in the container -
you do not need to install them yourself.

## 1. Clone

```bash
git clone https://github.com/ArgonFetch/ArgonFetch.git
cd ArgonFetch
```

## 2. Start the development database

```bash
docker compose -f compose.dev.yml up -d
```

That brings up Postgres 18 on port `3941` with the database `argonfetchdb-dev`.

## 3. Point the API at it

Use user secrets so the connection string stays out of the repository:

```bash
cd src/ArgonFetch.API
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:ArgonFetchDatabase" "Host=localhost;Port=3941;Database=argonfetchdb-dev;Username=postgres;Password=d4vpas8w0rd13!!!"
```

In Visual Studio: right-click **ArgonFetch.API** → **Manage User Secrets**, and paste

```json
{
  "ConnectionStrings": {
    "ArgonFetchDatabase": "Host=localhost;Port=3941;Database=argonfetchdb-dev;Username=postgres;Password=d4vpas8w0rd13!!!"
  }
}
```

A `.env` file at the repository root works too:

```ini
ConnectionStrings__ArgonFetchDatabase=Host=localhost;Port=3941;Database=argonfetchdb-dev;Username=postgres;Password=d4vpas8w0rd13!!!
ASPNETCORE_ENVIRONMENT=Development
```

## 4. Apply migrations

```bash
dotnet ef database update -p src/ArgonFetch.Infrastructure -s src/ArgonFetch.API
```

## 5. Run both halves

```bash
# API - http://localhost:5114, Swagger at /swagger
cd src/ArgonFetch.API
dotnet run
```

```bash
# Frontend - http://localhost:4200
cd src/ArgonFetch.Frontend
bun install
bun start
```

The frontend's default CORS origin is `http://localhost:4200`, so the two talk to each other with
no extra configuration in `Development`.

### Or the whole stack in Docker

```bash
cp template.env .env
docker compose up
```

That builds the API image from source and serves everything on `http://localhost:4358`.

## Project layout

| Project | What it holds |
|---|---|
| `ArgonFetch.API` | ASP.NET Core host, controllers, startup |
| `ArgonFetch.Application` | Use cases, DTOs, media resolution |
| `ArgonFetch.Domain` | Entities and domain types |
| `ArgonFetch.Infrastructure` | EF Core context, migrations, external services |
| `ArgonFetch.Frontend` | Angular 22 SPA |
| `ArgonFetch.Tests` | Test suite |

## Entity Framework

```bash
# Add a migration
dotnet ef migrations add MigrationName -p src/ArgonFetch.Infrastructure -s src/ArgonFetch.API

# Apply
dotnet ef database update -p src/ArgonFetch.Infrastructure -s src/ArgonFetch.API

# Drop the last one
dotnet ef migrations remove -p src/ArgonFetch.Infrastructure -s src/ArgonFetch.API
```

## Tests

```bash
dotnet test
dotnet test --collect:"XPlat Code Coverage"
```

## Helper scripts

`scripts/Db-Script.ps1` wraps the database and migration chores:

```powershell
cd scripts
.\Db-Script.ps1 -Command <command>
```

| Command | Does |
|---|---|
| `start-db` | `docker compose -f compose.dev.yml up -d` |
| `stop-db` | `docker compose -f compose.dev.yml down` |
| `recreate-db` | Stops the database, prunes volumes, starts it again |
| `add-migration` | Prompts for a name and adds an EF migration |
| `delete-migrations` | Deletes `src/ArgonFetch.Infrastructure/Migrations` |
| `full-reset` | Deletes migrations, recreates the database, adds an `Init` migration |
| `help` | Lists the commands |

`scripts/Db-Script-GUI.py` is the same thing with a CustomTkinter window
(`pip install customtkinter`). On Windows, double-click `scripts/start_argonfetch_gui.vbs` to
launch it without a console window.

## Regenerating the API client and schema

The frontend's typed API client is generated from the running API:

```bash
cd src/ArgonFetch.Frontend
bun run apigen
```

And the checked-in schema, after changing an endpoint or a DTO:

```bash
curl -s http://localhost:5114/swagger/v1/swagger.json -o docs/public/openapi.json
```

## Building this documentation site

The docs are a self-contained VitePress site in `docs/`:

```bash
cd docs
bun install
bun run dev      # http://localhost:5173
bun run build    # output in docs/.vitepress/dist
```

## Troubleshooting

**Database connection refused.** Check the dev container is up (`docker ps`) and that the port in
your connection string is `3941`, not `5432`.

**Port already in use.** The API defaults to `5114` (change with `ASPNETCORE_URLS`), the frontend
to `4200` (`ng serve --port`), the dev database to `3941` (in `compose.dev.yml`).

**Fetches answer 503.** The API is still downloading `yt-dlp` and `FFmpeg`. Give it a few seconds;
`GET /api/App` reports the state in its `maintenance` field.

## Contributing

Fork, branch, change, add a test where one fits, and open a pull request against `main`. Issues
and feature requests go to the
[issue tracker](https://github.com/ArgonFetch/ArgonFetch/issues).
