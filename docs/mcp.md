# MCP

ArgonFetch speaks [Model Context Protocol](https://modelcontextprotocol.io) at `/mcp`, so an AI
assistant can resolve a link and download the file without a person translating the REST API for
it.

There is no authentication, exactly as with the REST API. Anyone who can reach `/mcp` can use it,
so an instance on the open internet is an instance anyone can download through - the same caveat
as [Self-hosting](/self-host).

## Connecting

The endpoint is [streamable HTTP](https://modelcontextprotocol.io/specification/basic/transports),
which every current MCP client supports. Point yours at:

```
https://app.argonfetch.dev/mcp
```

Swap the host for your own instance - `http://localhost:8080/mcp` by default.

**Claude Code:**

```bash
claude mcp add --transport http argonfetch https://app.argonfetch.dev/mcp
```

**Claude Desktop, and anything else reading a JSON config:**

```json
{
  "mcpServers": {
    "argonfetch": {
      "type": "http",
      "url": "https://app.argonfetch.dev/mcp"
    }
  }
}
```

The server is stateless - it never calls back to the client - so nothing has to be pinned to one
replica and there is no session to keep alive.

## The tool

One tool, `fetch_media`, taking the page URL:

| Argument | |
|---|---|
| `url` | The page to resolve, for example `https://www.youtube.com/watch?v=dQw4w9WgXcQ` |

It answers with the title, author, cover and the renditions, split into `video` and `audio`. Each
rendition carries a `downloadUrl` that is a plain `GET` and needs nothing added to it:

```json
{
  "type": "Media",
  "items": [
    {
      "title": "Rick Astley - Never Gonna Give You Up (Official Video) (4K Remaster)",
      "author": "Rick Astley",
      "sourceUrl": "https://www.youtube.com/watch?v=dQw4w9WgXcQ",
      "video": [
        {
          "label": "1080p",
          "fileExtension": ".mp4",
          "fileSizeBytes": 84345754,
          "downloadUrl": "https://app.argonfetch.dev/api/Stream/Combined/0UStZ80UE8E7onWo"
        }
      ],
      "audio": [
        {
          "label": "192 kbps",
          "description": "Converted from 129 kbps",
          "fileExtension": ".mp3",
          "downloadUrl": "https://app.argonfetch.dev/api/Stream/Media/ZMWVLizyW7957Knd?format=mp3"
        }
      ]
    }
  ]
}
```

This is the difference from calling the REST API directly. There, a rendition carries a `key`, and
the caller has to know that `urlType` decides between two endpoints and that a converted rendition
needs `?format=mp3`. The tool resolves all of that, so the model has a URL rather than a puzzle -
see [API](/api) for the endpoints underneath.

The URLs stop working an hour after the call that produced them, which the server tells the model
up front.

### Playlists

A playlist answers with `type: "PlayList"` and one entry per track, each with a title and a
`sourceUrl` but no renditions - resolving two hundred tracks up front would take minutes. Calling
`fetch_media` again with a track's `sourceUrl` gets that track's download URLs.

## Instructions the server sends

At `initialize`, ArgonFetch sends a set of instructions describing when to use the tool, how to
pick a rendition when the user did not say, and that a `downloadUrl` is to be fetched or handed
over rather than pulled through the tool result. Clients that show a server's instructions - or
that pass them to the model, which is most of them - need nothing configured on top.

They live in `Program.cs`, next to `AddMcpServer`, so an instance that changes them changes what
every connected assistant is told.

## Browser-based clients

A client running in a browser needs the instance to allow its origin, the same as any other
cross-origin caller - see [Configuration](/configuration#cors). Clients that run outside the
browser, which includes Claude Desktop and Claude Code, are unaffected.

## Errors

A failure reaches the model as a tool error carrying the reason, so it can tell the user something
useful rather than retrying blindly:

| What happened | What the model is told |
|---|---|
| The link resolved to nothing | `Nothing was found at <url>.` |
| DRM, or a link shape ArgonFetch does not handle | The reason, as `GetResource` reports it |
| The instance is updating `yt-dlp` and `FFmpeg` | The current activity, and that the call will work shortly |
| Anything else | `Could not fetch <url>.`, with the detail in the server log |
