# 💻 Development Environment Setup
## Prerequisites
- Git
- Node.js and npm
- Angular CLI
- Python 3.x or .NET SDK (depending on which backend you want to use)

## Setting Up the Environment
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

## Running the Application
The following steps are required to run ArgonFetch in both development and production environments:


1. Start the backend server:
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

2. Start the frontend application:
   ```sh
   cd src/ArgonFetch.Frontend
   ng serve
   ```

3. Access the application at `http://localhost:4200`