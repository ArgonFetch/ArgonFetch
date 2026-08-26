# Developer setup

Running ArgonFetch from source means two pieces: the .NET API and the Angular frontend.
There is no database to stand up.

## Prerequisites

- Git
- [.NET 10 SDK](https://dotnet.microsoft.com/download)
- [Bun](https://bun.sh) 1.x
- Docker (only to build the container image; the app itself does not need it)

`FFmpeg` and `yt-dlp` are fetched by the API at startup, the same way they are in the container -
you do not need to install them yourself.

## 1. Clone

```bash
git clone https://github.com/ArgonFetch/ArgonFetch.git
cd ArgonFetch
```

## 2. Run both halves

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
| `ArgonFetch.Infrastructure` | External services and media tooling |
| `ArgonFetch.Frontend` | Angular 22 SPA |
| `ArgonFetch.Tests` | Test suite |

## Persistent state

There is none. Nothing survives a restart except the `yt-dlp` and `FFmpeg` binaries under
`TOOLS_PATH`, and those are re-fetched when they are missing. ArgonFetch keeps no record of
what it has been asked to fetch, and no count of how often.

## Tests

```bash
dotnet test
dotnet test --collect:"XPlat Code Coverage"
```

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

**Port already in use.** The API defaults to `5114` (change with `ASPNETCORE_URLS`) and the
frontend to `4200` (`ng serve --port`).

**Fetches answer 503.** The API is still downloading `yt-dlp` and `FFmpeg`. Give it a few seconds;
`GET /api/App` reports the state in its `maintenance` field.

## Contributing

Fork, branch, change, add a test where one fits, and open a pull request against `main`. Issues
and feature requests go to the
[issue tracker](https://github.com/ArgonFetch/ArgonFetch/issues).
