# Self-hosting

ArgonFetch runs as a single container. There is no database, nothing else to install and no
credentials to register.

## Prerequisites

- Docker and Docker Compose
- A few hundred MB of disk for the `yt-dlp` and `FFmpeg` binaries the container fetches at boot

## 1. Create `compose.yml`

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
      # The served-request counter, and nothing else.
      - data:/data
    restart: unless-stopped

volumes:
  tools:
  data:
```

## 2. Create `.env` next to it

```ini
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

## Volumes

The compose file above mounts two, and neither is a database.

`/tools` holds `yt-dlp` and `FFmpeg`. Keep it - without one, roughly 100MB is downloaded again
every time the container starts.

`/data` holds a single JSON file with the number of requests this instance has served. Drop it
if you do not care about the total; the only consequence is that it restarts from zero.

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

## Upgrading from a release that still used Postgres

ArgonFetch no longer ships a database. Delete the `postgres` service, its `postgres_data`
volume and the `POSTGRES_*` and `ConnectionStrings__ArgonFetchDatabase` variables from your
`.env`, then add the `data` volume shown above. Nothing needs migrating - the only thing the
database held was the request counter, and that total starts again from zero.

## Next

- [**Configuration**](/configuration) - every variable, plus proxy rotation.
- [**Usage**](/usage) - what to do with it once it is up.
