#!/bin/bash

# ArgonFetch Setup Script
# This script automates the setup process for ArgonFetch

# Print colored text
print_color() {
  COLOR=$1
  TEXT=$2
  echo -e "\033[${COLOR}m${TEXT}\033[0m"
}

# Print header
print_color "35" "
  █████╗ ██████╗  ██████╗  ██████╗ ███╗   ██╗███████╗███████╗████████╗ ██████╗██╗  ██╗
 ██╔══██╗██╔══██╗██╔════╝ ██╔═══██╗████╗  ██║██╔════╝██╔════╝╚══██╔══╝██╔════╝██║  ██║
 ███████║██████╔╝██║  ███╗██║   ██║██╔██╗ ██║█████╗  █████╗     ██║   ██║     ███████║
 ██╔══██║██╔══██╗██║   ██║██║   ██║██║╚██╗██║██╔══╝  ██╔══╝     ██║   ██║     ██╔══██║
 ██║  ██║██║  ██║╚██████╔╝╚██████╔╝██║ ╚████║██║     ███████╗   ██║   ╚██████╗██║  ██║
 ╚═╝  ╚═╝╚═╝  ╚═╝ ╚═════╝  ╚═════╝ ╚═╝  ╚═══╝╚═╝     ╚══════╝   ╚═╝    ╚═════╝╚═╝  ╚═╝
                                                                                      
"
print_color "36" "Setup Script"
echo ""

# Parse command line arguments
SPOTIFY_CLIENT_ID=""
SPOTIFY_CLIENT_SECRET=""

while [[ "$#" -gt 0 ]]; do
  case $1 in
    --spotify-id) SPOTIFY_CLIENT_ID="$2"; shift ;;
    --spotify-secret) SPOTIFY_CLIENT_SECRET="$2"; shift ;;
    *) echo "Unknown parameter: $1"; exit 1 ;;
  esac
  shift
done

# Display information about Spotify credentials
if [ -z "$SPOTIFY_CLIENT_ID" ] || [ -z "$SPOTIFY_CLIENT_SECRET" ]; then
  print_color "34" "📝 Spotify Credentials Required"
  print_color "37" "For Spotify support, you'll need to create an App using Spotify for Developers:"
  print_color "37" "https://developer.spotify.com/documentation/web-api/concepts/apps"
  echo ""
fi

# If Spotify credentials are not provided, ask for them
if [ -z "$SPOTIFY_CLIENT_ID" ]; then
  print_color "33" "Enter your Spotify Client ID:"
  read -rs SPOTIFY_CLIENT_ID  # -s flag hides the input
  echo # Add a newline after hidden input
fi

if [ -z "$SPOTIFY_CLIENT_SECRET" ]; then
  print_color "33" "Enter your Spotify Client Secret:"
  read -rs SPOTIFY_CLIENT_SECRET  # -s flag hides the input
  echo # Add a newline after hidden input
fi

# Check if credentials are provided
if [ -z "$SPOTIFY_CLIENT_ID" ] || [ -z "$SPOTIFY_CLIENT_SECRET" ]; then
  print_color "31" "Error: Spotify credentials are required."
  exit 1
fi

# Create .env file
print_color "32" "Creating .env file..."
cat > .env << EOF
SPOTIFY_CLIENT_ID=$SPOTIFY_CLIENT_ID
SPOTIFY_CLIENT_SECRET=$SPOTIFY_CLIENT_SECRET
EOF

# Start Docker Compose
print_color "32" "Starting ArgonFetch with Docker Compose..."
docker compose up -d

# Print success message
print_color "32" "✅ ArgonFetch is now running at http://localhost:8080" 