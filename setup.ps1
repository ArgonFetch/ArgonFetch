# ArgonFetch Setup Script for Windows
# This script automates the setup process for ArgonFetch

param (
    [string]$SpotifyId,
    [string]$SpotifySecret
)

# Print colored text
function Write-Color {
    param (
        [string]$Text,
        [string]$Color
    )
    
    $originalColor = $host.UI.RawUI.ForegroundColor
    $host.UI.RawUI.ForegroundColor = $Color
    Write-Output $Text
    $host.UI.RawUI.ForegroundColor = $originalColor
}

# Print header
Write-Color @"

  █████╗ ██████╗  ██████╗  ██████╗ ███╗   ██╗███████╗███████╗████████╗ ██████╗██╗  ██╗
 ██╔══██╗██╔══██╗██╔════╝ ██╔═══██╗████╗  ██║██╔════╝██╔════╝╚══██╔══╝██╔════╝██║  ██║
 ███████║██████╔╝██║  ███╗██║   ██║██╔██╗ ██║█████╗  █████╗     ██║   ██║     ███████║
 ██╔══██║██╔══██╗██║   ██║██║   ██║██║╚██╗██║██╔══╝  ██╔══╝     ██║   ██║     ██╔══██║
 ██║  ██║██║  ██║╚██████╔╝╚██████╔╝██║ ╚████║██║     ███████╗   ██║   ╚██████╗██║  ██║
 ╚═╝  ╚═╝╚═╝  ╚═╝ ╚═════╝  ╚═════╝ ╚═╝  ╚═══╝╚═╝     ╚══════╝   ╚═╝    ╚═════╝╚═╝  ╚═╝
                                                                                      
"@ "Magenta"

Write-Color "Setup Script" "Cyan"
Write-Output ""

# Display information about Spotify credentials
if (-not $SpotifyId -or -not $SpotifySecret) {
    Write-Color "📝 Spotify Credentials Required" "Blue"
    Write-Color "For Spotify support, you'll need to create an App using Spotify for Developers:" "White"
    Write-Color "https://developer.spotify.com/documentation/web-api/concepts/apps" "White"
    Write-Output ""
}

# If Spotify credentials are not provided, ask for them
if (-not $SpotifyId) {
    Write-Color "Enter your Spotify Client ID:" "Yellow"
    $secureId = Read-Host -AsSecureString
    $BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureId)
    $SpotifyId = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)
}

if (-not $SpotifySecret) {
    Write-Color "Enter your Spotify Client Secret:" "Yellow"
    $secureSecret = Read-Host -AsSecureString
    $BSTR = [System.Runtime.InteropServices.Marshal]::SecureStringToBSTR($secureSecret)
    $SpotifySecret = [System.Runtime.InteropServices.Marshal]::PtrToStringAuto($BSTR)
    [System.Runtime.InteropServices.Marshal]::ZeroFreeBSTR($BSTR)
}

# Check if credentials are provided
if (-not $SpotifyId -or -not $SpotifySecret) {
    Write-Color "Error: Spotify credentials are required." "Red"
    exit 1
}

# Create .env file
Write-Color "Creating .env file..." "Green"
@"
SPOTIFY_CLIENT_ID=$SpotifyId
SPOTIFY_CLIENT_SECRET=$SpotifySecret
"@ | Out-File -FilePath ".env" -Encoding utf8

# Start Docker Compose
Write-Color "Starting ArgonFetch with Docker Compose..." "Green"
docker compose up -d

# Print success message
Write-Color "✅ ArgonFetch is now running at http://localhost:8080" "Green" 