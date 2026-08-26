# <p align="center">ArgonFetch</p>
<p align="center">
  <img src="assets/logo-simple.svg" width="200" alt="ArgonFetch Logo">
</p>
<p align="center">
  <strong>ArgonFetch is Yet Another Media Downloader.</strong>
  A powerful tool for downloading videos, music, and other media from various online sources.
</p>
<p align="center">
  <a href="https://github.com/ArgonFetch/ArgonFetch"><img src="https://badgetrack.pianonic.ch/badge?tag=argon-fetch&label=visits&color=9f54e5&style=flat" alt="visits" /></a>
  <a href="https://www.argonfetch.dev/"><img src="https://img.shields.io/badge/Cloud%20Version-argonfetch.dev-9f54e5.svg"/></a>
  <a href="https://docs.argonfetch.dev/self-host"><img src="https://img.shields.io/badge/Selfhost-Instructions-9f54e5.svg"/></a>
  <a href="https://docs.argonfetch.dev"><img src="https://img.shields.io/badge/Documentation-docs.argonfetch.dev-9f54e5.svg"/></a>
  <a href="https://docs.argonfetch.dev/dev-setup"><img src="https://img.shields.io/badge/Development-Setup-9f54e5.svg"/></a>
</p>

---

> **⚠️ Important Note:** This project is currently under development and may not function as described directly from the main branch. For a working version, please check the [Releases tab](https://github.com/ArgonFetch/ArgonFetch/releases) for the latest stable release.

## What it does

Paste a link, get the media. ArgonFetch resolves the URL, picks the best available
streams and serves them back as a normal file download.

- **No API keys.** Nothing to register, no credentials to configure — including Spotify.
- **Audio or video**, at a quality you choose.
- **Web interface and REST API**, with Swagger docs.
- **One container, no database.** Runs anywhere Docker does.

## Screenshots

![ArgonFetch Homepage](./assets/startpage.png)

## Platform support

| Platform | Status | Notes |
|---|---|---|
| YouTube | ✅ Video and audio | Muxes separate video/audio streams when no pre-muxed format exists |
| Spotify | ✅ Tracks, playlists and albums | Metadata is read from the public pages; audio comes from the matching YouTube Music result |
| TikTok | ✅ | |
| SoundCloud | ✅ | Except the licensed catalogue, which is DRM protected and refused |
| Instagram | ⚠️ Needs `COOKIES_PATH` | Instagram serves media only to a signed-in session; supply one and it works |
| Playlists | ✅ Any source | Resolved as a collection; download them individually or the whole list as one zip |

Downloads carry the title and artist: in the filename always, and written into the file
itself when it is converted to MP3. A pass-through download is served byte for byte, so it
carries whatever tags the source shipped with - usually none - and only its name identifies
it.

Audio is delivered in whatever container the source uses - Opus in WebM for YouTube -
because passing the original bytes through is both faster and better than re-encoding
them to MP3. Every response states its `mimeType`. MP3 is still available on request:
add `?format=mp3` to the stream URL, which is also what happens automatically for
sources in a container ArgonFetch does not recognise. Video is delivered as MP4.

## Quick start

**1. Create `compose.yml`:**

```yaml
services:
  argonfetch:
    image: ghcr.io/argonfetch/argonfetch:latest
    # Alternative: docker.io/pianonic/argonfetch:latest
    container_name: argonfetch
    env_file: .env
    ports:
      - "8080:8080"
    volumes:
      # yt-dlp and FFmpeg are fetched on boot instead of being baked into the image.
      # Keeping them here means a restart reuses them rather than downloading again.
      - tools:/tools
    restart: unless-stopped

volumes:
  tools:
```

**2. Create `.env` next to it:**

```env
# Origins allowed to call the API. Set this to your own host in production.
CORS_ALLOWED_ORIGINS=http://localhost:8080
```

**3. Start it:**

```bash
docker compose up -d
```

Open `http://localhost:8080`. No further configuration is required.

## Configuration

Everything is set through environment variables in `.env`.

| Variable | Required | Description |
|---|---|---|
| `CORS_ALLOWED_ORIGINS` | in production | Comma-separated origins allowed to call the API. Defaults to `http://localhost:4200`, and the app warns at startup if that default is still in use in production |
| `ASPNETCORE_ENVIRONMENT` | no | `Production` by default. `Development` also enables Swagger UI |
| `PROXY_LIST_PATH` | no | File with one proxy per line, rotated across yt-dlp fetches so they do not all leave from the same IP. See below |
| `COOKIES_PATH` | no | Netscape-format cookies file, for sources that serve media only to a signed-in session. See below |

### Media tooling

`yt-dlp` and `FFmpeg` are not built into the image. The container fetches both when it
starts and reports itself as under maintenance until they are ready - the web UI shows a
maintenance screen and fetches answer `503` for those few seconds. `yt-dlp` then updates
itself every 12 hours, so extractor fixes land without rebuilding anything, and a restart
is enough to recover from a broken version.

Mount a volume at `/tools` (the compose file above does) so the binaries survive restarts.
Without one, roughly 100MB is downloaded again every time the container starts.

### Signed-in sources

Some sources serve nothing to a signed-out request. Instagram is the clearest case - it
requires a session for practically everything - and an age-gated YouTube video wants one
too. Point `COOKIES_PATH` at a Netscape-format cookies file exported from a browser that is
logged in, and every extraction offers it:

```env
COOKIES_PATH=/config/cookies.txt
```

```yaml
    volumes:
      - ./cookies.txt:/config/cookies.txt:ro
```

Without it those sources answer `415` saying a session is needed, rather than pretending the
link is wrong. A path pointing at a file that is not there is ignored, so a mistake in the
setting does not break every other fetch.

Treat the file as a credential: anyone holding it is signed in as you.

### Proxy rotation

If the host's IP gets rate limited, point `PROXY_LIST_PATH` at a file listing one
proxy per line. Each fetch takes the next proxy in the list, and a failed fetch is
retried through the following one.

```env
PROXY_LIST_PATH=/config/proxies.txt
```

```yaml
    volumes:
      - ./proxies.txt:/config/proxies.txt:ro
```

Both `http://user:pass@host:port` and the `host:port:user:pass` export format used
by providers such as Webshare are accepted; blank lines and `#` comments are
ignored. Without the variable, fetches go out from the server's own IP as before.

### Upgrading from a release that still used Postgres

ArgonFetch no longer ships a database. Delete the `postgres` service, its
`postgres_data` volume and the `POSTGRES_*` and
`ConnectionStrings__ArgonFetchDatabase` variables from your `.env`. Nothing needs
migrating — ArgonFetch keeps no persistent state at all.

## Usage

1. Navigate to `http://localhost:8080`
2. Paste a media URL and press Enter
3. Pick a quality, then download

API docs are at `http://localhost:8080/swagger` when running in `Development`.

The schema is also checked in at [`docs/public/openapi.json`](docs/public/openapi.json) for generating
clients elsewhere without running the app. Refresh it from a running instance after changing
any endpoint or DTO:

```bash
curl -s http://localhost:5114/swagger/v1/swagger.json -o docs/public/openapi.json
```

## Development

See the [Development Guide](https://docs.argonfetch.dev/dev-setup) (source: [`docs/dev-setup.md`](docs/dev-setup.md)).

## License

This project is licensed under the GPL-3.0 License. See [LICENSE](LICENSE) for details.

---
