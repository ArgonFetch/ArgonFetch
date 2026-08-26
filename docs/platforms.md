# Supported platforms

Extraction runs on [`yt-dlp`](https://github.com/yt-dlp/yt-dlp), so a link from almost any site
it knows will resolve. The sites yt-dlp does not handle are covered by **plugins** - Spotify is
the clearest case, resolved from the public track page rather than by yt-dlp at all.

Plugins are installed by name and are not built in, so Spotify and TikTok need two lines of
[configuration](/configuration#plugins) before they work.

The table below is the shorter list that ArgonFetch tests and supports directly. Anything outside
it is worth trying, but is not something we chase when it breaks.

| Platform | Status | Notes |
|---|---|---|
| YouTube | ✅ Video and audio | Muxes separate video/audio streams when no pre-muxed format exists |
| Spotify | ✅ Tracks, playlists and albums | Needs the `spotify` [plugin](/configuration#plugins). Metadata is read from the public pages; audio comes from the matching YouTube Music result |
| TikTok | ✅ | Needs the `tiktok` [plugin](/configuration#plugins) |
| SoundCloud | ✅ | Except the licensed catalogue, which is DRM protected and refused - see [below](#drm-protected-tracks) |
| Instagram | ⚠️ Needs a signed-in session | Instagram serves media only to logged-in accounts. Point [`COOKIES_PATH`](/configuration) at an exported session and it works |
| Playlists | ✅ Any source | Resolved as a collection; download the tracks individually or the whole list as one zip |

## Spotify without an API key

Spotify links need the `spotify` plugin installed, but no credentials. ArgonFetch reads the title, artist and cover from the
public track page, then finds the matching result on YouTube Music and streams the audio from
there. There is no developer app to register and no secret to rotate.

Playlists and albums work the same way. The track list is read through the same API the
Spotify web player uses, which needs no credentials either, and each track is then matched on
YouTube Music like a single one.

## DRM protected tracks

Some sources hand back media that is DRM protected and cannot be decoded - SoundCloud does this
for its licensed catalogue. The link is correct and the track is really there; the source simply
refuses to serve it in a usable form, and there is nothing ArgonFetch can do about that.

These answer `415 Unsupported Media Type` with the reason in `detail`, rather than the `404` that
would send you looking for a typo. See [Errors](/api#errors).

## Sites that need an account

Instagram serves media only to a signed-in session, so a link that opens fine in your own browser
returns nothing to a server that is not logged in. This is not a bug in the extractor and no
retry will help - the request never had the credentials it needed.

Point [`COOKIES_PATH`](/configuration) at a Netscape-format cookies file exported from a
signed-in browser and the session travels with the fetch. Without it, the request never had the
credentials it needed and no retry will help.

## When a platform stops working

Extraction is `yt-dlp`'s job, and sites change. `yt-dlp` updates itself every 12 hours inside the
container, so most breakage fixes itself; restarting the container pulls the current version
immediately. If a platform stays broken after that,
[open an issue](https://github.com/ArgonFetch/ArgonFetch/issues).
