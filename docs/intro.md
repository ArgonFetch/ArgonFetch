# What is ArgonFetch?

ArgonFetch is a self-hosted web application that turns a media link into a file you keep.

Paste a URL, and ArgonFetch resolves it, lists every quality the source actually offers, and
serves the one you pick back as an ordinary browser download. There is nothing to sign up for
and nothing to configure - not even Spotify credentials.

- **No API keys.** Spotify metadata is read from the public track page, so there is no developer
  app to register and no secret to rotate.
- **Audio or video.** Renditions come straight from the source, with a size and bitrate on each
  so you can see what you are choosing.
- **Web interface and REST API.** The UI is one input box; the API is a handful of `GET`
  endpoints, documented at `/swagger` and checked in as [`openapi.json`](/openapi.json).
- **One container, no database.** Resolved media is cached in memory. Everything else
  runs in the app container.

::: warning Pre-release
ArgonFetch is under development and the `main` branch may not behave as documented. For a
working build, use a tagged image or the
[releases page](https://github.com/ArgonFetch/ArgonFetch/releases).
:::

## How a download works

1. You paste a URL. The frontend calls `GET /api/Fetch/GetResource?url=...`.
2. ArgonFetch resolves it through `yt-dlp` and returns the title, author, cover and the list of
   available renditions - each with a `key`.
3. You pick one. The browser opens `GET /api/Stream/Media/{key}` (or `/api/Stream/Combined/{key}`
   when video and audio have to be muxed) and the bytes come back as a file download.

Nothing is stored on disk for you to clean up afterwards; the stream is proxied through as it is
fetched.

## Formats

Audio is delivered in whatever container the source uses - Opus in WebM for YouTube - because
passing the original bytes through is both faster and better than re-encoding them to MP3. Every
response states its `mimeType`. MP3 is still available on request: add `?format=mp3` to the
stream URL, which is also what happens automatically for sources in a container ArgonFetch does
not recognise. Video is delivered as MP4.

## Media tooling

`yt-dlp` and `FFmpeg` are not built into the image. The container fetches both when it starts
and reports itself as under maintenance until they are ready - the web UI shows a maintenance
screen and fetches answer `503` for those few seconds. `yt-dlp` then updates itself every 12
hours, so extractor fixes land without rebuilding anything, and a restart is enough to recover
from a broken version.

## Next

- [**Self-hosting**](/self-host) - run it with `docker compose`.
- [**Configuration**](/configuration) - every environment variable, including proxy rotation.
- [**Usage**](/usage) - the web UI and the API, end to end.
- [**Developer setup**](/dev-setup) - local dev, tests, and how the projects fit together.
