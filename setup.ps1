# ArgonFetch Setup Script for Windows
# This script automates the setup process for ArgonFetch

param (
    [string]$SpotifyId,
    [string]$SpotifySecret
)

$ShortcutName = "ArgonFetch Setup.lnk"
$desktopPath = [Environment]::GetFolderPath("Desktop")
$shortcutPath = [System.IO.Path]::Combine($desktopPath, $ShortcutName)

$repoPath = (Get-Location).Path
$scriptPath = $repoPath + "\setup.ps1"

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

# Function to handle yes/no prompts (empty input defaults to yes)
function Read-YesNo {
    param (
        [string]$Prompt
    )
    
    $response = Read-Host "$Prompt (Y/n, default: Y)"
    
    # Return true if empty or y/Y, false otherwise
    return [string]::IsNullOrWhiteSpace($response) -or $response -eq "y" -or $response -eq "Y"
}

function DockerDesktop-Instructions {
    Write-Color "To install Docker Desktop manually:" "Red"
    Write-Color "1. Download Docker Desktop from https://www.docker.com/products/docker-desktop" "White"
    Write-Color "2. Run the installer and follow the instructions" "White"
    Write-Color "3. After installation, restart this script" "White"
}

# Check if PowerShell is running as Administrator
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    Write-Color "This script must be run as Administrator. Please run PowerShell as Administrator and try again." "Red"
    exit 1
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

#===========================================================================
# Check if Docker is installed and install if not + optional Shortcut creation
#===========================================================================

function Check-DockerInstallation {
    Write-Color "Checking if Docker is installed..." "Blue"
    
    $dockerCheck = Get-Command docker -ErrorAction SilentlyContinue
    
    if (-not $dockerCheck) {
        Write-Color "Docker is not installed but required for ArgonFetch." "Yellow"
        
        $installDocker = Read-YesNo "Would you like Docker Desktop to be installed automatically via winget? (Make sure to update App-Installer first: https://apps.microsoft.com/detail/9NBLGGH4NNS1?hl=de-de&gl=CH&ocid=pdpshare)"
        
        if (-not $installDocker) {
            Write-Color "Docker installation cancelled. Docker is required to run ArgonFetch." "Red"
            DockerDesktop-Instructions
            exit 1
        }
        
        # Check if winget is available
        $wingetCheck = Get-Command winget -ErrorAction SilentlyContinue
        
        if ($wingetCheck) {
            # Install Docker Desktop using winget
            Write-Color "Installing Docker Desktop using winget..." "Yellow"
            try {
                winget install Docker.DockerDesktop --accept-package-agreements --accept-source-agreements --silent
            } catch {
                Write-Color "Error: Failed to install Docker Desktop using winget." "Red"
                DockerDesktop-Instructions
                exit 1
            }
        } else {
            DockerDesktop-Instructions
            exit 1
        }
        
        Write-Color "Docker Desktop has been installed. You will need to restart your computer and finish the Docker Desktop Setup (will open automatically after restart)." "Green"
        
        $createShortcut = Read-YesNo "Would you like to create a desktop shortcut for this script?"
        if ($createShortcut) {
            $WScriptShell = New-Object -ComObject WScript.Shell
            $shortcut = $WScriptShell.CreateShortcut($shortcutPath)
            $shortcut.TargetPath = "powershell.exe"
            $shortcut.Arguments = "-ExecutionPolicy Bypass -File `"$scriptPath`""
            $shortcut.IconLocation = "$repoPath\assets\logo-simple.ico"
            $shortcut.WorkingDirectory = [System.IO.Path]::GetDirectoryName($scriptPath)
            $shortcut.Save()
            Write-Color "Shortcut created on the desktop to run this script as admin." "Green"
            Write-Color "After restarting, please execute the shortcut to run the script." "Green"
        } else {
            Write-Color "No shortcut will be created." "Yellow"
            Write-Color "After restarting, please run this script again." "Green"
        }
        exit 1
    } else {
        # Check if Docker service is running
        try {
            $dockerInfo = docker info 2>&1
            if ($LASTEXITCODE -ne 0) {
                Write-Color "Docker is installed but not running. Please start Docker Desktop and run this script again." "Yellow"
                exit 1
            }
            Write-Color "Docker is installed and running." "Green"
        } catch {
            Write-Color "Docker is installed but not running. Please start Docker Desktop and run this script again." "Yellow"
            exit 1
        }
    }
}

# Check Docker installation before starting
Check-DockerInstallation

write-host ""

#===========================================================================
# Spotify Credentials + .env file creation
#===========================================================================

# Check if .env file already exists
$envFileExists = Test-Path ".env"
$useExistingEnv = $false

if ($envFileExists) {
    # Read the existing .env file to check if it has the required credentials
    $envContent = Get-Content ".env" -Raw
    $hasSpotifyId = $envContent -match "SPOTIFY_CLIENT_ID=.+"
    $hasSpotifySecret = $envContent -match "SPOTIFY_CLIENT_SECRET=.+"
    
    if ($hasSpotifyId -and $hasSpotifySecret) {
        Write-Color "> The existing .env file contains Spotify credentials." "Green"
        $useExistingEnv = Read-YesNo "Would you like to use the existing credentials?"
    } else {
        Write-Color "> Incomplete or invalid .env file found." "Yellow"
        $useExistingEnv = $false
    }
}

# Only ask for credentials if we're not using an existing .env file
if (-not $useExistingEnv) {
    # Display information about Spotify credentials
    if (-not $SpotifyId -or -not $SpotifySecret) {
        Write-Color "📝 Spotify Credentials Required" "Blue"
        Write-Color "For Spotify support, you will need to create an App using Spotify for Developers:" "White"
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
}

#===========================================================================
# Docker Compose Start
#===========================================================================

# Ask for confirmation before starting Docker Compose
Write-Color "Setup is ready to start ArgonFetch using Docker Compose." "Blue"
$startNow = Read-YesNo "Would you like to start ArgonFetch now?"

if ($startNow) {
    # Start Docker Compose
    Write-Color "Starting ArgonFetch with Docker Compose..." "Green"
    docker compose up -d

    # Print success message
    Write-Color "✅ ArgonFetch is now running at http://localhost:8080" "Green"
} else {
    Write-Color "ArgonFetch setup preparation is complete but not started." "Yellow"
    Write-Color "You can start it later by running: docker compose up -d" "Yellow"
}

#===========================================================================
# Remove existing shortcut (optional after docker installation is complete for easier re-launching)
#===========================================================================

if (Test-Path $shortcutPath) {
    Remove-Item $shortcutPath -Force
}