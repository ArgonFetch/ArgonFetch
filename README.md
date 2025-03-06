# <p align="center">ArgonFetch</p>

<p align="center">
  <img src="https://github.com/user-attachments/assets/bf03eee6-0aa5-4508-8d2f-cb6fd6b1485f" width="200" alt="ArgonFetch Logo">
</p>

<p align="center">
  <strong>ArgonFetch is Yet Another Media Downloader.</strong> 
  A powerful tool for downloading videos, music, and other media from various online sources.
</p>
<p align="center">
  <a href="https://hits.seeyoufarm.com"><img src="https://hits.seeyoufarm.com/api/count/incr/badge.svg?url=https%3A%2F%2Fgithub.com%2FArgonFetch%2FArgonFetch&count_bg=%23A855F6&title_bg=%23555555&icon=powershell.svg&icon_color=%23E7E7E7&title=Visits&edge_flat=false"/></a>
</p>

---

## 🚀 Features

- 📥 Download videos, music, and more from multiple sources 
- 🎯 Easy-to-use interface with powerful options 
- 🔗 Supports a wide range of websites 

## 📸 Screenshots

<p align="center">
  <img src=".\assets\startpage.png" width="1000" alt="ArgonFetch Screenshot">

## 📦 Installation

> [!WARNING]  
> This does not work yet.

```sh
# Clone the repository
git clone https://github.com/Pianonic/ArgonFetch.git

# Navigate to the project directory
cd ArgonFetch

# Run the application using Docker Compose
docker-compose up -d
```
After running the command, ArgonFetch should be accessible at http://localhost:8000.

## 🛠️ Usage

Simply launch ArgonFetch and paste the URL of the media you want to download. 
Choose your preferred format and quality, then start downloading!

## 💻 Development Environment Setup

### Prerequisites
- Git
- Node.js and npm
- Angular CLI
- Python 3.x or .NET SDK (depending on which backend you want to use)

### Setting Up the Environment

1. Clone the repository:
   ```sh
   git clone https://github.com/Pianonic/ArgonFetch.git
   cd ArgonFetch
   ```

2. Configure API Credentials:

   **For Python Backend:**
   Create a `.env` file in the `tmp/ArgonFetch.Backend` directory with:
   ```
   SPOTIFY_CLIENT_ID=your_spotify_client_id
   SPOTIFY_CLIENT_SECRET=your_spotify_client_secret
   ```

   **For C# Backend:**
   Set up user secrets:
   ```sh
   cd src/ArgonFetch.API
   dotnet user-secrets init
   dotnet user-secrets set "Spotify:ClientId" "your_spotify_client_id"
   dotnet user-secrets set "Spotify:ClientSecret" "your_spotify_client_secret"
   ```

3. Install frontend dependencies:
   ```sh
   cd src/ArgonFetch.Frontend
   npm install
   ```

### Running the Application

The following steps are required to run ArgonFetch in both development and production environments:

1. Start the proxy server (required for API communication):
   ```sh
   cd scripts
   run_proxy.bat
   ```

2. Start the backend server:

   **For Python Backend:**
   ```sh
   cd tmp/ArgonFetch.Backend
   # Start the Python server
   python app.py
   ```

   **OR For C# Backend:**
   ```sh
   cd src/ArgonFetch.API
   dotnet run
   ```

3. Start the frontend application:
   ```sh
   cd src/ArgonFetch.Frontend
   ng serve
   ```

4. Access the application at `http://localhost:4200`

**Important Note:** The proxy server is not just for testing - it's an essential component of the application architecture. It handles API requests and bypasses CORS restrictions in both development and production environments.

## 📜 License

This project is licensed under the GPL-3.0 License. 
See the [LICENSE](LICENSE) file for more details.

## Contributors
<a href = "https://github.com/ArgonFetch/ArgonFetch/graphs/contributors">
  <img src = "https://contrib.rocks/image?repo=ArgonFetch/ArgonFetch"/>
</a>

---

<p align="center">Made with ❤️ by <a href="https://github.com/Pianonic">PianoNic</a> and <a href="https://github.com/MyDrift-user">MyDrift</a></p>
