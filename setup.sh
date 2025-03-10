#!/bin/bash

# ArgonFetch Setup Script
# This script automates the setup process for ArgonFetch

# Print colored text
print_color() {
  COLOR=$1
  TEXT=$2
  echo -e "\033[${COLOR}m${TEXT}\033[0m"
}

# Function to handle yes/no prompts (empty input defaults to yes)
read_yes_no() {
  local PROMPT="$1"
  local RESPONSE
  read -p "${PROMPT} (Y/n, default: Y): " RESPONSE
  
  # Return true (0) if empty or y/Y, false (1) otherwise
  if [[ -z "$RESPONSE" || "$RESPONSE" == "y" || "$RESPONSE" == "Y" ]]; then
    return 0
  else
    return 1
  fi
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

#===========================================================================
# Check if Docker is installed and install if necessary
#===========================================================================

check_docker_installation() {
  print_color "34" "Checking if Docker is installed..."
  
  # Initialize DOCKER_CMD to run Docker directly
  export DOCKER_CMD=""
  
  if ! command -v docker &> /dev/null; then
    print_color "33" "Docker is not installed but is required to run ArgonFetch."
    
    # Ask for permission to install Docker
    if read_yes_no "Would you like to install Docker now?"; then
      
      # Detect OS
      if [ -f /etc/os-release ]; then
        . /etc/os-release
        OS=$ID
      else
        OS=$(uname -s)
      fi
      
      case $OS in
        ubuntu|debian|linuxmint)
          print_color "33" "Installing Docker on $OS..."
          sudo apt-get update
          sudo apt-get install -y apt-transport-https ca-certificates curl software-properties-common
          curl -fsSL https://download.docker.com/linux/$OS/gpg | sudo apt-key add -
          sudo add-apt-repository "deb [arch=amd64] https://download.docker.com/linux/$OS $(lsb_release -cs) stable"
          sudo apt-get update
          sudo apt-get install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin
          ;;
        fedora|centos|rhel)
          print_color "33" "Installing Docker on $OS..."
          sudo dnf -y install dnf-plugins-core
          sudo dnf config-manager --add-repo https://download.docker.com/linux/$OS/docker-ce.repo
          sudo dnf install -y docker-ce docker-ce-cli containerd.io docker-compose-plugin
          ;;
        arch|manjaro)
          print_color "33" "Installing Docker on $OS..."
          sudo pacman -Sy docker docker-compose --noconfirm
          ;;
        *)
          print_color "31" "Unsupported OS: $OS"
          print_color "33" "Please install Docker manually from https://docs.docker.com/engine/install/"
          print_color "33" "After installation, restart this script."
          exit 1
          ;;
      esac
      
      # Start and enable Docker service
      sudo systemctl start docker
      sudo systemctl enable docker
      
      # Add current user to docker group to avoid using sudo
      sudo usermod -aG docker $USER
      
      # Try to use Docker without sudo after installation
      if docker info &> /dev/null; then
        print_color "32" "Docker has been installed and is working correctly."
      else
        # Check if we're on Linux
        if [[ "$(uname -s)" == "Linux" ]]; then
          print_color "33" "Docker has been installed, but you need permission to use Docker without sudo."
          print_color "33" "Let's try to apply the group changes without logging out..."
          
          # Try using sg command which executes a command with the permissions of the specified group
          if sg docker -c "docker info" &> /dev/null; then
            print_color "32" "Successfully applied Docker group permissions!"
            # Use sg for the rest of the Docker operations in this script
            export DOCKER_CMD="sg docker -c"
          else
            print_color "33" "Docker has been installed, but you may need to log out and log back in for group changes to take effect."
            print_color "31" "Please log out and log back in, then run this script again."
            exit 1
          fi
        else
          # For non-Linux systems
          print_color "33" "Docker has been installed but may require additional configuration."
          print_color "33" "Please restart your system and run this script again."
          exit 1
        fi
      fi
    else
      print_color "31" "Docker installation cancelled. Docker is required to run ArgonFetch."
      print_color "31" "Please install Docker manually and run this script again."
      exit 1
    fi
  else
    # Check if Docker service is running
    if ! docker info &> /dev/null; then
      print_color "33" "Docker is installed but not running. Starting Docker service..."
      sudo systemctl start docker
      
      # Check again if Docker is running
      if ! docker info &> /dev/null; then
        print_color "31" "Failed to start Docker service. Please start it manually and run this script again."
        exit 1
      fi
    fi
    
    print_color "32" "Docker is installed and running."
    
    # Check if docker-compose is available
    if ! command -v docker-compose &> /dev/null && ! docker compose &> /dev/null; then
      print_color "33" "Docker Compose is not installed. Installing Docker Compose..."
      
      # Install Docker Compose
      sudo curl -L "https://github.com/docker/compose/releases/download/v2.18.1/docker-compose-$(uname -s)-$(uname -m)" -o /usr/local/bin/docker-compose
      sudo chmod +x /usr/local/bin/docker-compose
      
      print_color "32" "Docker Compose has been installed."
    else
      print_color "32" "Docker Compose is installed."
    fi
  fi
}

# Check Docker installation before starting
check_docker_installation

#===========================================================================
# Spotify Credentials + .env file creation
#===========================================================================

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

# Check if .env file already exists
USE_EXISTING_ENV=false
if [ -f ".env" ]; then
  # Check if the .env file contains the required credentials
  if grep -q "SPOTIFY_CLIENT_ID=.*" .env && grep -q "SPOTIFY_CLIENT_SECRET=.*" .env; then
    print_color "32" "> The existing .env file contains Spotify credentials."
    if read_yes_no "Would you like to use the existing credentials?"; then
      USE_EXISTING_ENV=true
    else
      print_color "33" "You've chosen to update the Spotify credentials."
    fi
  else
    print_color "33" "> Incomplete or invalid .env file found."
  fi
fi

# Only ask for credentials if we're not using an existing .env file
if [ "$USE_EXISTING_ENV" = false ]; then
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
fi

#===========================================================================
# Docker Compose Start
#===========================================================================

# Ask for confirmation before starting Docker Compose
print_color "34" "Setup is ready to start ArgonFetch using Docker Compose."
if read_yes_no "Would you like to start ArgonFetch now?"; then
  # Start Docker Compose
  print_color "32" "Starting ArgonFetch with Docker Compose..."
  if [ -n "$DOCKER_CMD" ]; then
    $DOCKER_CMD "docker compose up -d"
  else
    docker compose up -d
  fi

  # Print success message
  print_color "32" "✅  ArgonFetch is now running at http://localhost:8080"
else
  print_color "33" "ArgonFetch setup preparation is complete but not started."
  print_color "33" "You can start it later by running: docker compose up -d"
fi