# Configuration

Everything is set through environment variables, normally in the `.env` file next to
`compose.yml`. There is no configuration file and no admin UI.

## Variables

| Variable | Required | Description |
|---|---|---|
| `CORS_ALLOWED_ORIGINS` | in production | Comma-separated origins allowed to call the API. Defaults to `http://localhost:4200`, and the app warns at startup if that default is still in use in production |
| `ASPNETCORE_ENVIRONMENT` | no | `Production` by default. `Development` also enables the Swagger UI |
| `TOOLS_PATH` | no | Where `yt-dlp` and `FFmpeg` are downloaded to. `/tools` in the image, which is the path the compose file mounts a volume on. Must be writable by the runtime user |
| `PROXY_LIST_PATH` | no | File with one proxy per line, rotated across `yt-dlp` fetches so they do not all leave from the same IP. See [below](#proxy-rotation) |
| `COOKIES_PATH` | no | Netscape-format cookies file exported from a signed-in browser, for sources that serve nothing to strangers. See [Platforms](/platforms#sites-that-need-an-account) |
| `Plugins__*` | for Spotify and TikTok | Which plugins to install and where from. See [below](#plugins) |

A complete `.env` for a public deployment:

```ini
ASPNETCORE_ENVIRONMENT=Production
CORS_ALLOWED_ORIGINS=https://argonfetch.example.com

Plugins__Repositories__0=https://raw.githubusercontent.com/ArgonFetch/ArgonFetchPlugins/repo/index.json
Plugins__Install__0=spotify
Plugins__Install__1=tiktok
```

## Plugins

`yt-dlp` fetches most links on its own. A few sources need something else first - Spotify
serves no audio anyone can download, TikTok watermarks what it serves - and those are
**plugins**, installed by name rather than built in.

```ini
Plugins__Repositories__0=https://raw.githubusercontent.com/ArgonFetch/ArgonFetchPlugins/repo/index.json
Plugins__Install__0=spotify
Plugins__Install__1=tiktok
```

Or, if you configure with a file rather than the environment:

```jsonc
"Plugins": {
  "Repositories": [ "https://raw.githubusercontent.com/ArgonFetch/ArgonFetchPlugins/repo/index.json" ],
  "Install": [ "spotify", "tiktok" ]
}
```

The list is read as **desired state**: what you name is installed, what you leave out is
removed. Pin a version with `spotify@1.0.0`, or name the plugin alone for the newest build
that fits this release.

The order is also precedence - if two plugins claim the same link, the one listed first
wins, and the other is mentioned in the log so you can see it happened.

Plugins are downloaded once at startup and checked against the hash the repository
published, never during a request. A repository that cannot be reached leaves whatever is
already installed alone rather than stopping the app from starting.

Anyone can serve a repository: it is a JSON index and some zip files on a URL. To write a
plugin, start from
[ArgonFetchPluginTemplate](https://github.com/ArgonFetch/ArgonFetchPluginTemplate).

## CORS

`CORS_ALLOWED_ORIGINS` is the one variable you must not forget in production. It lists the
origins the browser is allowed to call the API from - normally just the origin ArgonFetch itself
is served on. Several are accepted, comma-separated:

```ini
CORS_ALLOWED_ORIGINS=https://app.argonfetch.dev,https://argonfetch.dev,http://localhost:4200
```

Leave it at the default and the app logs a warning at startup, because a production deployment
that still only trusts `http://localhost:4200` is almost certainly a mistake.

## Proxy rotation

If the host's IP gets rate limited, point `PROXY_LIST_PATH` at a file listing one proxy per
line. Each fetch takes the next proxy in the list, and a failed fetch is retried through the
following one.

```ini
PROXY_LIST_PATH=/config/proxies.txt
```

```yaml
    volumes:
      - ./proxies.txt:/config/proxies.txt:ro
```

Both `http://user:pass@host:port` and the `host:port:user:pass` export format used by providers
such as Webshare are accepted; blank lines and `#` comments are ignored. Without the variable,
fetches go out from the server's own IP as before.

## Media tooling

`yt-dlp` and `FFmpeg` are fetched into `/tools` when the container starts rather than baked into
the image, and `yt-dlp` updates itself every 12 hours. Two consequences worth knowing:

- **Mount a volume at `/tools`.** Otherwise roughly 100MB is downloaded again on every start.
- **A restart fixes a broken `yt-dlp`.** Extractor fixes land without rebuilding the image.

`/tools` is the image's default; `TOOLS_PATH` moves it, which is what you want if the container
runs with a read-only root filesystem or your volume layout puts writable storage elsewhere. Move
the mount with it - wherever it points has to be writable by the runtime user.

While the binaries are being fetched the app reports itself as under maintenance: the web UI
shows a maintenance screen and fetch requests answer `503`. `GET /api/App` reports the same
state in its `maintenance` field.
