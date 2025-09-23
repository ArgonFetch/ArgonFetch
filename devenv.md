# 💻 Development Environment Setup

## Prerequisites
- Git
- Node.js 18+ and npm
- Angular CLI (`npm install -g @angular/cli@19`)
- .NET 8 SDK
- PostgreSQL 15+ (or use Docker)
- FFmpeg and yt-dlp (for media processing)

## Setting Up the Environment

### 1. Clone the Repository
```bash
git clone https://github.com/ArgonFetch/ArgonFetch.git
cd ArgonFetch
```

### 2. Database Setup

#### Option A: Using Docker (Recommended)
```bash
# Start PostgreSQL using Docker
docker run -d \
  --name argonfetch-db \
  -e POSTGRES_USER=argonfetch \
  -e POSTGRES_PASSWORD=changeme123 \
  -e POSTGRES_DB=argonfetch \
  -p 5432:5432 \
  postgres:15
```

#### Option B: Local PostgreSQL
Create a database and user for ArgonFetch in your existing PostgreSQL installation.

### 3. Configure API Credentials

#### Option A: Using User Secrets (Recommended for Development)
```bash
cd src/ArgonFetch.API
dotnet user-secrets init
dotnet user-secrets set "ConnectionStrings:ArgonFetchDatabase" "Host=localhost;Port=5432;Database=argonfetch;Username=argonfetch;Password=changeme123"
dotnet user-secrets set "Spotify:ClientId" "your_spotify_client_id"
dotnet user-secrets set "Spotify:ClientSecret" "your_spotify_client_secret"
```

#### Option B: Using .env File
Create a `.env` file in the project root:
```env
# Database
ConnectionStrings__ArgonFetchDatabase=Host=localhost;Port=5432;Database=argonfetch;Username=argonfetch;Password=changeme123

# Spotify API
Spotify__ClientId=your_spotify_client_id
Spotify__ClientSecret=your_spotify_client_secret
```

### 4. Install Dependencies

#### Backend Dependencies
```bash
cd src/ArgonFetch.API
dotnet restore
```

#### Frontend Dependencies
```bash
cd src/ArgonFetch.Frontend
npm install
```

### 5. Apply Database Migrations
```bash
cd src/ArgonFetch.API
dotnet ef database update
```

## Running the Application

### Development Mode

#### 1. Start the Backend Server
```bash
cd src/ArgonFetch.API
dotnet run
# Backend will be available at http://localhost:5000
```

#### 2. Start the Frontend Application
```bash
cd src/ArgonFetch.Frontend
npm start
# Frontend will be available at http://localhost:4200
```

### Using Docker Compose for Full Stack

For a complete development environment with database:
```bash
# Copy and configure environment variables
cp .env.example .env
# Edit .env with your Spotify credentials

# Start all services
docker compose up

# Access at http://localhost:8080
```

## Development Tools

### Entity Framework Commands
```bash
# Add a new migration
dotnet ef migrations add MigrationName -p src/ArgonFetch.Infrastructure -s src/ArgonFetch.API

# Update database
dotnet ef database update -p src/ArgonFetch.Infrastructure -s src/ArgonFetch.API

# Remove last migration
dotnet ef migrations remove -p src/ArgonFetch.Infrastructure -s src/ArgonFetch.API
```

### Running Tests
```bash
# Run all tests
dotnet test

# Run with coverage
dotnet test --collect:"XPlat Code Coverage"
```

### Building for Production
```bash
# Build backend
cd src/ArgonFetch.API
dotnet publish -c Release -o ./publish

# Build frontend
cd src/ArgonFetch.Frontend
npm run build:prod
```

## Troubleshooting

### Common Issues

**PostgreSQL Connection Failed**
- Ensure PostgreSQL is running on port 5432
- Check credentials in user secrets or .env file
- Verify database exists

**Spotify API Not Working**
- Verify Client ID and Secret are correct
- Check if credentials are properly set in user secrets or .env

**Port Already in Use**
- Backend default: 5000 (change with `ASPNETCORE_URLS`)
- Frontend default: 4200 (change with `ng serve --port`)
- Database default: 5432 (change in connection string)

## Additional Resources
- [Spotify App Creation](https://developer.spotify.com/documentation/web-api/concepts/apps)
- [Entity Framework Core Docs](https://docs.microsoft.com/en-us/ef/core/)
- [Angular Documentation](https://angular.io/docs)