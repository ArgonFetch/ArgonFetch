# API

ArgonFetch exposes a small REST API. Every endpoint is a `GET`, there is no authentication, and
responses are JSON unless they are the media itself.

Every operation has its own page in the sidebar, generated from the schema, with its parameters,
response shapes and a playground you can send real requests from.

::: tip Trying a request
The playground runs in your browser and sends real requests to
[app.argonfetch.dev](https://app.argonfetch.dev), the hosted instance. Use the server selector to
point it at your own instance instead - and make sure that instance lists `docs.argonfetch.dev` in
`CORS_ALLOWED_ORIGINS`, or the browser blocks the response before it reaches the page. See
[Configuration](/configuration#cors).
:::

## Endpoints

| Endpoint | What it does |
|---|---|
| [`GET /api/App`](/operations/GetAppInfo) | Version and health, including why the instance is in maintenance |
| [`GET /api/App/requests`](/operations/GetRequestCount) | How many resolve requests this instance has served |
| [`GET /api/Fetch/GetResource`](/operations/GetResource) | Resolves a media URL into metadata and available renditions |
| [`GET /api/Stream/Media/{key}`](/operations/Media) | Streams one rendition; `?format=mp3` re-encodes audio |
| [`GET /api/Stream/Combined/{key}`](/operations/Combined) | Muxes separate video and audio into MP4 |

`Fetch` and `Stream` are the pair you want: resolve a URL, then stream the `key` you picked out of
the response. [Usage](/usage#from-the-command-line) walks through both with `curl`.

## Errors

Failures come back as [RFC 7807](https://datatracker.ietf.org/doc/html/rfc7807) `ProblemDetails`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "The url parameter is required."
}
```

A `503` from `GetResource` usually means the container is still fetching `yt-dlp` and `FFmpeg` -
[`GET /api/App`](/operations/GetAppInfo) says so in its `maintenance` field.

## The schema

These pages are generated from [`openapi.json`](/openapi.json), which is checked in at
`docs/public/openapi.json` so clients can be generated without running the app. Swagger UI is also
served at `/swagger` when `ASPNETCORE_ENVIRONMENT=Development`.

Refresh the schema from a running instance after changing any endpoint or DTO - the pages here
follow automatically:

```bash
curl -s http://localhost:5114/swagger/v1/swagger.json -o docs/public/openapi.json
```
