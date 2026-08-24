# Usage

## The web interface

1. Open [app.argonfetch.dev](https://app.argonfetch.dev) - or your own instance, at
   `http://localhost:8080` unless you moved it.
2. Paste a media URL and press Enter.
3. ArgonFetch resolves the link and shows the title, author and cover, with the qualities the
   source actually offers.
4. Pick one. The file downloads through your browser like any other.

Nothing is queued and nothing is kept - the stream is proxied through as it is fetched.

## From the command line

The API is a handful of `GET` endpoints, so `curl` is enough. Resolve first, then stream the
rendition you want.

The examples below run against the hosted instance, so they work as they stand. Swap the host for
your own - `http://localhost:8080` by default - if you are self-hosting.

**Resolve a link:**

```bash
curl -s "https://app.argonfetch.dev/api/Fetch/GetResource?url=https://www.youtube.com/watch?v=dQw4w9WgXcQ"
```

The response carries a `key` for each available rendition (see [the API
reference](/operations/GetResource) for the full shape).

**Download that rendition:**

```bash
curl -L "https://app.argonfetch.dev/api/Stream/Media/<key>" -o track.webm
```

**Ask for MP3 instead of the source container:**

```bash
curl -L "https://app.argonfetch.dev/api/Stream/Media/<key>?format=mp3" -o track.mp3
```

**When video and audio are separate streams**, the rendition's `urlType` is `Combined` and the
muxing endpoint is the one to call:

```bash
curl -L "https://app.argonfetch.dev/api/Stream/Combined/<key>" -o video.mp4
```

## Formats

Audio is delivered in whatever container the source uses - Opus in WebM for YouTube - because
passing the original bytes through is both faster and better than re-encoding. Every rendition
states its `mimeType` and `fileExtension`, so you always know what you are getting.

| You want | Do this |
|---|---|
| The best audio, fastest | Take the audio rendition as-is |
| MP3 specifically | Add `?format=mp3` to the stream URL |
| Video | Take a video rendition; ArgonFetch delivers MP4 |

Sources in a container ArgonFetch does not recognise are converted to MP3 automatically.

## Trying the API without curl

Every endpoint has a page under [API](/api) with a playground that sends the request for you,
against the hosted instance or your own. Swagger UI is also served at `/swagger`, though only when
the instance runs with `ASPNETCORE_ENVIRONMENT=Development` - so on a local build, not on
app.argonfetch.dev. The schema is checked in at [`openapi.json`](/openapi.json) either way, for
generating clients without running anything.

## A note on what you download

ArgonFetch does not check what you are allowed to keep. Make sure you have the right to download
the content you point it at.

## Next

- [**Supported platforms**](/platforms) - what works today and what does not.
- [**API reference**](/api) - every endpoint and response shape.
