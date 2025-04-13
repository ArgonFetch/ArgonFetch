# <p align="center">ArgonFetch</p>
<p align="center">
  <img src="assets/logo-simple.svg" width="200" alt="ArgonFetch Logo">
</p>
<p align="center">
  <strong>ArgonFetch is Yet Another Media Downloader.</strong> 
  A powerful tool for downloading videos, music, and other media from various online sources.
</p>
<p align="center">
  <a><img src="https://hits.seeyoufarm.com/api/count/incr/badge.svg?url=https%3A%2F%2Fgithub.com%2FArgonFetch%2FArgonFetch&count_bg=%23A855F6&title_bg=%23555555&icon=powershell.svg&icon_color=%23E7E7E7&title=Visits&edge_flat=false"/></a>
  <a href="https://www.argonfetch.dev/"><img src="https://img.shields.io/badge/Cloud%20Version-argonfetch.dev-9f54e5.svg"/></a>
  <a href="https://github.com/ArgonFetch/ArgonFetch?tab=readme-ov-file#-installation"><img src="https://img.shields.io/badge/Selfhost-Instructions-9f54e5.svg"/></a>
  <a href="https://github.com/ArgonFetch/ArgonFetch/blob/main/devenv.md"><img src="https://img.shields.io/badge/Development-Setup-9f54e5.svg"/></a>
</p>

---

> **⚠️ Important Note:** This project is currently under development and may not function as described directly from the main branch. For a working version, please check the [Releases tab](https://github.com/ArgonFetch/ArgonFetch/releases) for the latest stable release.

<!-- [![Version](https://img.shields.io/github/v/release/ArgonFetch/ArgonFetch?color=%230567ff&label=Latest%20Release&style=for-the-badge)](https://github.com/ArgonFetch/ArgonFetch/releases/latest) -->

## 🚀 Features
- 📥 Download videos, music, and more from multiple sources 
- 🎯 Easy-to-use interface with powerful options 
- 🔗 Supports a wide range of websites 

## 📸 Screenshots
<p align="center">
  <img src=".\assets\startpage.png" width="1000" alt="ArgonFetch Screenshot">
</p>

## 📦 Installation

### Preparations:

- For Spotify Support you'll need to [create an App using Spotify for Developers](https://developer.spotify.com/documentation/web-api/concepts/apps).
  From there you'll get a Client ID and a Client Secret, which you will have to provide during the install.

#### Clone Run the setup script:
   
   **Linux/macOS:**
   ```sh
   # Download Source
   curl -fsSL -o argonfetch.tar.gz https://github.com/ArgonFetch/ArgonFetch/archive/refs/heads/main.tar.gz
   # Unpack Source, remove tar and cd into the source directory
   tar -xzf argonfetch.tar.gz
   rm argonfetch.tar.gz
   cd ArgonFetch-main
   # Make the script executable
   chmod +x setup.sh
   
   # Run interactively
   ./setup.sh
   ```
   
   **Windows (PowerShell):**
   ```powershell   
   # Download the source using Invoke-WebRequest
   Invoke-WebRequest -Uri "https://github.com/ArgonFetch/ArgonFetch/archive/refs/heads/main.tar.gz" -OutFile "argonfetch.tar.gz"
   # Extract the tar.gz file (Windows 10 and later include tar)
   tar -xzf argonfetch.tar.gz
   # Remove the downloaded archive
   Remove-Item argonfetch.tar.gz
   # Change directory into the extracted folder
   Set-Location ArgonFetch-main

   # Run interactively
   .\setup.ps1
   ```

After the installation, ArgonFetch should be accessible at http://localhost:8080.

## 🛠️ Usage
Simply launch ArgonFetch and paste the URL of the media you want to download. 
<!--- Choose your preferred format and quality, then start downloading! --->

## 🛣️ Roadmap
- [x] Spotify Songs
- [ ] Spotify Playlists
- [ ] Spotify Albums
- [x] YouTube Media
- [ ] SoundCloud Media
### Future Plans
- [ ] Social Media Support (X, Instagram, TikTok ...)

## 💻 Development Environment Setup
To setup the development environment follow [this](devenv.md) guide.

## 📜 License
This project is licensed under the GPL-3.0 License. 
See the [LICENSE](LICENSE) file for more details.

## Contributors
<a href="https://github.com/argonfetch/argonfetch/graphs/contributors">
  <img src="https://contrib.rocks/image?repo=argonfetch/argonfetch " />
</a>

---
<p align="center">Made with ❤️ by <a href="https://github.com/Pianonic">PianoNic</a> and <a href="https://github.com/MyDrift-user">MyDrift</a></p>
