# Self-hosting

ArgonFetch runs as two containers: the app and a Postgres database. There is nothing else to
install and no credentials to register.

## Prerequisites

- Docker and Docker Compose
- A few hundred MB of disk for the `yt-dlp` and `FFmpeg` binaries the container fetches at boot

## 1. Create `compose.yml`

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
    volumes:
      # yt-dlp and FFmpeg are fetched on boot instead of being baked into the image.
      # Keeping them here means a restart reuses them rather than downloading again.
      - tools:/tools
    depends_on:
      - postgres
    restart: unless-stopped

volumes:
  postgres_data:
  tools:
```

## 2. Create `.env` next to it

```ini
POSTGRES_USER=argonfetch
POSTGRES_PASSWORD=changeme123
POSTGRES_DB=argonfetch

# Origins allowed to call the API. Set this to your own host in production.
CORS_ALLOWED_ORIGINS=http://localhost:8080
```

## 3. Start it

```bash
docker compose up -d
```

Open `http://localhost:8080`. No further configuration is required.

::: tip First boot takes a moment
The container downloads `yt-dlp` and `FFmpeg` before it serves traffic. Until they are ready the
web UI shows a maintenance screen and fetches answer `503`. It clears itself after a few seconds.
:::

Useful commands:

```bash
docker compose logs -f     # follow the logs
docker compose down        # stop everything
docker compose pull        # grab a newer image, then `up -d` again
```

## Images

ArgonFetch images are published to two registries - pick whichever suits your setup:

| Registry | Image |
|---|---|
| GitHub Container Registry | `ghcr.io/argonfetch/argonfetch:latest` |
| Docker Hub | `docker.io/pianonic/argonfetch:latest` |

## Mount `/tools`

The compose file above mounts a `tools` volume at `/tools`. Keep it. Without one, roughly 100MB
of `yt-dlp` and `FFmpeg` is downloaded again every time the container starts.

## Behind a reverse proxy

ArgonFetch listens on `8080` inside the container and speaks plain HTTP; terminate TLS in front
of it. An Nginx server block:

```nginx
server {
    listen 80;
    server_name argonfetch.example.com;

    location / {
        proxy_pass http://localhost:8080;
        proxy_set_header Host $host;
        proxy_set_header X-Real-IP $remote_addr;
        proxy_set_header X-Forwarded-For $proxy_add_x_forwarded_for;
        proxy_set_header X-Forwarded-Proto $scheme;

        # Downloads are streamed as they are fetched, so give them room.
        proxy_buffering off;
        proxy_read_timeout 1h;
    }
}
```

Then set `CORS_ALLOWED_ORIGINS` to the public origin you just published:

```ini
CORS_ALLOWED_ORIGINS=https://argonfetch.example.com
```

## Upgrading from a release older than Postgres 18

The database image moved from Postgres 15 to 18, which is a major upgrade: the on-disk format
changed and the volume mount path moved. An existing `postgres_data` volume will not start under
18 - dump the old database and restore it into the new one, or run `pg_upgrade`.

## Next

- [**Configuration**](/configuration) - every variable, plus proxy rotation.
- [**Usage**](/usage) - what to do with it once it is up.
