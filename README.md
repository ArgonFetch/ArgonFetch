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
  <a href="https://github.com/ArgonFetch/ArgonFetch?tab=readme-ov-file#-quick-start"><img src="https://img.shields.io/badge/Selfhost-Instructions-9f54e5.svg"/></a>
  <a href="https://github.com/ArgonFetch/ArgonFetch/blob/main/devenv.md"><img src="https://img.shields.io/badge/Development-Setup-9f54e5.svg"/></a>
</p>

---

> **⚠️ Important Note:** This project is currently under development and may not function as described directly from the main branch. For a working version, please check the [Releases tab](https://github.com/ArgonFetch/ArgonFetch/releases) for the latest stable release.

## ✨ What it does

Paste a link, get the media. ArgonFetch resolves the URL, picks the best available
streams and serves them back as a normal file download.

- **No API keys.** Nothing to register, no credentials to configure — including Spotify.
- **Audio or video**, at a quality you choose.
- **Web interface and REST API**, with Swagger docs.
- **One container plus a database.** Runs anywhere Docker does.

## 📸 Screenshots

![ArgonFetch Homepage](./assets/startpage.png)

## 🎯 Platform support

| Platform | Status | Notes |
|---|---|---|
| YouTube | ✅ Video and audio | Muxes separate video/audio streams when no pre-muxed format exists |
| Spotify | ✅ Single tracks | Metadata is read from the public track page; audio comes from the matching YouTube Music result |
| TikTok | ✅ | |
| Spotify playlists / albums | ❌ Not supported | [#171](https://github.com/ArgonFetch/ArgonFetch/issues/171) |
| Playlists generally | ❌ Not supported | [#76](https://github.com/ArgonFetch/ArgonFetch/issues/76) |
| SoundCloud | ⚠️ Unreliable | [#58](https://github.com/ArgonFetch/ArgonFetch/issues/58) |
| Instagram Reels | ⚠️ Preview issues | [#58](https://github.com/ArgonFetch/ArgonFetch/issues/58) |

Audio downloads are delivered as MP3 and video as MP4.

## 🚀 Quick start

**1. Create `compose.yml`:**

```yaml
services:
  postgres:
    image: postgres:18
    container_name: argonfetch-db
    env_file: .env
    volumes:
      # Postgres 18 stores data in a version-specific subdirectory, so the volume
      # goes here and NOT on /var/lib/postgresql/data.
      - postgres_data:/var/lib/postgresql
    restart: unless-stopped

  argonfetch:
    image: ghcr.io/argonfetch/argonfetch:latest
    # Alternative: docker.io/pianonic/argonfetch:latest
    container_name: argonfetch
    env_file: .env
    environment:
      ConnectionStrings__ArgonFetchDatabase: "Host=postgres;Port=5432;Database=${POSTGRES_DB};Username=${POSTGRES_USER};Password=${POSTGRES_PASSWORD}"
    ports:
      - "8080:8080"
    depends_on:
      - postgres
    restart: unless-stopped

volumes:
  postgres_data:
```

**2. Create `.env` next to it:**

```env
POSTGRES_USER=argonfetch
POSTGRES_PASSWORD=changeme123
POSTGRES_DB=argonfetch

# Origins allowed to call the API. Set this to your own host in production.
CORS_ALLOWED_ORIGINS=http://localhost:8080
```

**3. Start it:**

```bash
docker compose up -d
```

Open `http://localhost:8080`. No further configuration is required.

## ⚙️ Configuration

Everything is set through environment variables in `.env`.

| Variable | Required | Description |
|---|---|---|
| `POSTGRES_USER` / `POSTGRES_PASSWORD` / `POSTGRES_DB` | yes | Database credentials, shared by both containers |
| `ConnectionStrings__ArgonFetchDatabase` | yes | Set in `compose.yml` from the values above |
| `CORS_ALLOWED_ORIGINS` | in production | Comma-separated origins allowed to call the API. Defaults to `http://localhost:4200`, and the app warns at startup if that default is still in use in production |
| `ASPNETCORE_ENVIRONMENT` | no | `Production` by default. `Development` also enables Swagger UI |

`yt-dlp` updates itself inside the container every 12 hours, so extractor fixes
land without rebuilding the image.

### Upgrading from a release older than Postgres 18

The database image moved from Postgres 15 to 18, which is a major upgrade: the
on-disk format changed and the volume mount path moved. An existing
`postgres_data` volume will not start under 18 — dump the old database and
restore it into the new one, or run `pg_upgrade`.

## 🛠️ Usage

1. Navigate to `http://localhost:8080`
2. Paste a media URL and press Enter
3. Pick a quality, then download

API docs are at `http://localhost:8080/swagger` when running in `Development`.

## 📋 Roadmap

- [x] Spotify tracks
- [x] YouTube media
- [x] TikTok
- [ ] Spotify playlists and albums
- [ ] SoundCloud
- [ ] Playlist support

### Future plans
- [ ] Wider social media support (X, Instagram, ...)

## 💻 Development

See the [Development Guide](devenv.md).

## 📜 License

This project is licensed under the GPL-3.0 License. See [LICENSE](LICENSE) for details.

---
