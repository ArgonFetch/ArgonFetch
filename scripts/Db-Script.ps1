param (
    [string]$Command
)

function Show-Help {
    Write-Host "Usage: .\Manage-Migrations.ps1 [-Command | --Command]"
    Write-Host "Commands:"
    Write-Host "  -add-migration, --add-migration        - Adds a new migration (asks for a name)"
    Write-Host "  -start-db, --start-db                  - Starts the development database"
    Write-Host "  -recreate-db, --recreate-db            - Recreates the development database"
    Write-Host "  -stop-db, --stop-db                    - Stops the development database"
    Write-Host "  -delete-migrations, --delete-migrations - Deletes the Migrations directory"
    Write-Host "  -full-reset, --full-reset               - Deletes migrations, recreates the DB, and adds an 'Init' migration"
    Write-Host "  -help, --help                           - Show this help message"
    exit 0
}

# Remove any leading dashes from the command
$Command = $Command -replace '^-+', ''

if (-not $Command -or $Command -eq "help") {
    Show-Help
}

# Set the correct paths relative to the script location
$RootPath = Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$MigrationsPath = Join-Path -Path $RootPath -ChildPath "src\DockiUp.Infrastructure"
$DockerComposeFile = Join-Path -Path $RootPath -ChildPath "compose.dev.yml"

switch ($Command) {
    "add-migration" {
        $MigrationName = Read-Host "Enter migration name"
        if (-not $MigrationName) {
            Write-Host "Migration name cannot be empty." -ForegroundColor Red
            exit 1
        }
        Push-Location $MigrationsPath
        dotnet ef migrations add $MigrationName
        Pop-Location
    }
    "start-db" {
        Push-Location $RootPath
        docker compose -f $DockerComposeFile up -d
        Pop-Location
    }
    "recreate-db" {
		Push-Location $RootPath
		docker compose -f $DockerComposeFile down

		docker volume prune -f

		docker compose -f $DockerComposeFile up -d
		Pop-Location
	}
    "stop-db" {
        Push-Location $RootPath
        docker compose -f $DockerComposeFile down
        Pop-Location
    }
    "delete-migrations" {
        $MigrationDir = Join-Path -Path $MigrationsPath -ChildPath "Migrations"
        if (Test-Path $MigrationDir) {
            Remove-Item -Recurse -Force $MigrationDir
            Write-Host "Migrations directory deleted." -ForegroundColor Green
        } else {
            Write-Host "Migrations directory not found." -ForegroundColor Yellow
        }
    }
    "full-reset" {
        Write-Host "Performing full reset..." -ForegroundColor Yellow
        $MigrationDir = Join-Path -Path $MigrationsPath -ChildPath "Migrations"
        if (Test-Path $MigrationDir) {
            Remove-Item -Recurse -Force $MigrationDir
            Write-Host "Migrations directory deleted." -ForegroundColor Green
        } else {
            Write-Host "Migrations directory not found." -ForegroundColor Yellow
        }
        
        Push-Location $RootPath
        docker compose -f $DockerComposeFile down
        docker compose -f $DockerComposeFile up -d
        Pop-Location
        
        Push-Location $MigrationsPath
        dotnet ef migrations add Init
        Pop-Location
        
        Write-Host "Full reset completed with 'Init' migration." -ForegroundColor Green
    }
    default {
        Write-Host "Invalid command: $Command. Use '-help' or '--help' for a list of commands." -ForegroundColor Red
        exit 1
    }
}