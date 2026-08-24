---
layout: home

hero:
  name: ArgonFetch
  text: Paste a link. Keep the file.
  tagline: A self-hosted media downloader for YouTube, Spotify, TikTok, SoundCloud and everything else yt-dlp can reach. No account, no API keys, one compose file.
  image:
    src: /logo.svg
    alt: ArgonFetch
  actions:
    - theme: brand
      text: Self-host ArgonFetch
      link: /self-host
    - theme: alt
      text: What is ArgonFetch?
      link: /intro
    - theme: alt
      text: GitHub
      link: https://github.com/ArgonFetch/ArgonFetch

features:
  - title: No API keys
    details: Nothing to register and no credentials to configure - Spotify included. Start the container and it works.
  - title: Audio or video, your quality
    details: Every rendition the source offers is listed. Audio passes through in its original container; MP3 on request.
  - title: Web UI and REST API
    details: One interface for people, one JSON API for scripts, with a Swagger page and a checked-in OpenAPI schema.
  - title: One container plus a database
    details: yt-dlp and FFmpeg are fetched at boot and update themselves, so extractor fixes land without rebuilding anything.
---
