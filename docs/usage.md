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
passing the original bytes through is faster and lossless. Every rendition states its `mimeType`
and `fileExtension`, so you always know what you are getting.

The one thing pass-through cannot do is carry tags, which makes this a real choice rather than a
formality:

| You want | Do this | You get |
|---|---|---|
| The audio exactly as the source has it | Take the audio rendition as-is | Byte-identical, no re-encode, **no tags inside the file** |
| A tagged file your music player will file correctly | Add `?format=mp3` to the stream URL | Re-encoded to MP3 with the title and artist written in |
| Video | Take a video rendition | MP4, tagged while it is muxed |

Sources in a container ArgonFetch does not recognise are converted to MP3 automatically, so those
arrive tagged either way.

## Filenames and tags

Every download names itself. Both stream endpoints send a `Content-Disposition` built from the
media, so the file lands as `Artist - Title.ext` rather than a cache key - in a browser, in `curl
-OJ`, and in anything else that reads the header:

```
Content-Disposition: attachment; filename="Rammstein - Sonne.webm"; filename*=UTF-8''Rammstein%20-%20Sonne.webm
```

The name is written twice on purpose. The quoted form stays ASCII so clients that read only that
still get something sensible, and `filename*` carries the real name for everything else - which is
what makes a title in Japanese or Cyrillic survive the trip.

Tags inside the file are a separate matter, and only anything ArgonFetch converts gets them:

- **Converted output** - MP3, and the muxed MP4 - carries the title and artist internally, written
  while the file is being built.
- **Pass-through** carries whatever the source shipped with, which is usually nothing. Tagging it
  would mean remuxing, and that would cost the exact byte length the response already promised and
  the [range requests](/api#range-requests) a client may be making against it. The filename carries
  the information instead.

There is no cover art in either case. Embedding a picture needs a seekable output, and these
responses are pipes.

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
