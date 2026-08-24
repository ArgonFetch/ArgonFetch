# Supported platforms

Extraction runs on [`yt-dlp`](https://github.com/yt-dlp/yt-dlp) plus ArgonFetch's own extractors
on top of it, so a link from almost any site yt-dlp knows will resolve, and the sites it does not
handle well get their own handling here - Spotify is the clearest case, resolved from the public
track page rather than by yt-dlp at all.

The table below is the shorter list that ArgonFetch tests and supports directly. Anything outside
it is worth trying, but is not something we chase when it breaks.

| Platform | Status | Notes |
|---|---|---|
| YouTube | ✅ Video and audio | Muxes separate video/audio streams when no pre-muxed format exists |
| Spotify | ✅ Single tracks | Metadata is read from the public track page; audio comes from the matching YouTube Music result |
| TikTok | ✅ | |
| Spotify playlists / albums | ❌ Not supported | [#171](https://github.com/ArgonFetch/ArgonFetch/issues/171) |
| Playlists generally | ❌ Not supported | [#76](https://github.com/ArgonFetch/ArgonFetch/issues/76) |
| SoundCloud | ⚠️ Unreliable | Licensed tracks are DRM protected and refused - see [below](#drm-protected-tracks). [#58](https://github.com/ArgonFetch/ArgonFetch/issues/58) |
| Instagram Reels | ⚠️ Preview issues | [#58](https://github.com/ArgonFetch/ArgonFetch/issues/58) |

## Spotify without an API key

Spotify links do not need credentials. ArgonFetch reads the title, artist and cover from the
public track page, then finds the matching result on YouTube Music and streams the audio from
there. There is no developer app to register and no secret to rotate.

This is also why only single tracks work: playlists and albums need the Spotify API proper.

## DRM protected tracks

Some sources hand back media that is DRM protected and cannot be decoded - SoundCloud does this
for its licensed catalogue. The link is correct and the track is really there; the source simply
refuses to serve it in a usable form, and there is nothing ArgonFetch can do about that.

These answer `415 Unsupported Media Type` with the reason in `detail`, rather than the `404` that
would send you looking for a typo. See [Errors](/api#errors).

## When a platform stops working

Extraction is `yt-dlp`'s job, and sites change. `yt-dlp` updates itself every 12 hours inside the
container, so most breakage fixes itself; restarting the container pulls the current version
immediately. If a platform stays broken after that,
[open an issue](https://github.com/ArgonFetch/ArgonFetch/issues).
