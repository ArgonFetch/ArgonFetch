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
| [`GET /api/Fetch/GetResource`](/operations/GetResource) | Resolves a media URL into metadata and available renditions |
| [`GET /api/Stream/Media/{key}`](/operations/Media) | Streams one rendition; `?format=mp3` re-encodes audio |
| [`GET /api/Stream/Combined/{key}`](/operations/Combined) | Muxes separate video and audio into MP4 |

`Fetch` and `Stream` are the pair you want: resolve a URL, then stream the `key` you picked out of
the response. [Usage](/usage#from-the-command-line) walks through both with `curl`.

## Filenames

Both stream endpoints send a `Content-Disposition` built from the media, so a client that respects
it saves `Artist - Title.ext` rather than a cache key:

```
Content-Disposition: attachment; filename="Rammstein - Sonne.webm"; filename*=UTF-8''Rammstein%20-%20Sonne.webm
```

The name appears twice: the quoted form is ASCII-only for clients that read nothing else, and
`filename*` ([RFC 5987](https://datatracker.ietf.org/doc/html/rfc5987)) carries the real name, so
titles outside ASCII survive. Read the second one if you can.

The header is listed in `Access-Control-Expose-Headers`, so browser callers can read it too - by
default a cross-origin response hides it, which is why a `fetch` that ignores this ends up naming
files itself.

`curl` does not use it unless you ask: `-OJ` takes the server's name, plain `-O` uses the URL,
which here is the key.

Files ArgonFetch converts - MP3, and the muxed MP4 - also carry the title and artist as tags
inside the file. Pass-through responses do not; see [Formats](/usage#formats).

## Range requests

`GET /api/Stream/Media/{key}` honours the `Range` header, so a download can be resumed and a
player can seek without pulling the whole file first. A ranged request answers `206 Partial
Content`, and a range the media cannot satisfy answers `416`.

```bash
curl -r 0-1048575 "https://app.argonfetch.dev/api/Stream/Media/<key>" -o part.webm
```

`GET /api/Stream/Combined/{key}` does not: it muxes video and audio as it sends them, so there is
no known length to seek within. It answers `200` and streams from the start.

## Errors

Failures come back as [RFC 7807](https://datatracker.ietf.org/doc/html/rfc7807) `ProblemDetails`:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Unsupported Media Type",
  "status": 415,
  "detail": "This media is DRM protected and cannot be downloaded."
}
```

What `GetResource` answers, and what each one means:

| Status | Title | Means |
|---|---|---|
| `400` | Bad Request | The `url` parameter is missing or malformed |
| `404` | Resource Not Found | The link did not resolve to anything |
| `415` | Unsupported Media Type | The source refused, or the link shape is not handled. `detail` says which |
| `502` | Fetch Failed | Extraction failed for some other reason |
| `503` | *the current activity* | The instance is updating `yt-dlp` and `FFmpeg` and is briefly unavailable |

`415` is the one worth handling separately. It is not a broken link: it means the media was found
and cannot be delivered - most often DRM, which SoundCloud applies to its licensed catalogue and
`yt-dlp` refuses. The `detail` field carries the reason, so a caller can tell DRM apart from a link
ArgonFetch does not handle yet. Only `415` and `503` fill `detail` in; the rest carry `title` alone.

A `503` means the container is still fetching its media tooling - [`GET
/api/App`](/operations/GetAppInfo) says so in its `maintenance` field, and it clears itself within
seconds of a start.

## The schema

These pages are generated from [`openapi.json`](/openapi.json), which is checked in at
`docs/public/openapi.json` so clients can be generated without running the app. The reference UI is also
served at `/scalar` when `ASPNETCORE_ENVIRONMENT=Development`.

Refresh the schema from a running instance after changing any endpoint or DTO - the pages here
follow automatically:

```bash
dotnet build src/ArgonFetch.API
```
