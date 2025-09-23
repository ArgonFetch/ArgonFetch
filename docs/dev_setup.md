# Development setup instructions

[![.NET](https://img.shields.io/badge/.NET-9.0-purple.svg)](https://dotnet.microsoft.com/)
[![EF Core](https://img.shields.io/badge/EF_Core-9.0-blue.svg)](https://docs.microsoft.com/en-us/ef/core/)
[![Docker](https://img.shields.io/badge/docker-required-blue.svg)](https://www.docker.com/)

This document contains essential commands and configurations for developers working on ArgonFetch.

## Development Environment Setup

### User Secrets Configuration

Add the following to your user secrets:

```json
{
  "Spotify": {
    "ClientId": "",
    "ClientSecret": ""
  },
  "ConnectionStrings": {
    "ArgonFetchDatabase": "Host=localhost;Port=3941;Database=argonfetchdb-dev;Username=postgres;Password=d4vpas8w0rd13!!!;"
  }
}
```

## Docker Commands

### Start Development Database

```sh
docker compose -f compose.dev.yml up -d
```

## Entity Framework (EF) Migrations

### Add a Migration

inside the `src\\ArgonFetch.Infrastructure`, execute:
```sh
dotnet ef migrations add Changes
```

## Helper Scripts

The `scripts` directory in the root of the project contains helper scripts to automate common development tasks.

### `Db-Script.ps1`

This PowerShell script provides a command-line interface for managing the development database and Entity Framework migrations.

**Usage:**

Open a PowerShell terminal in the `scripts` directory and run:

```powershell
.\Db-Script.ps1 -Command <command_name>
```

**Available Commands:**

*   `add-migration`: Prompts for a migration name and adds a new EF migration.
*   `start-db`: Starts the development database using `docker compose -f compose.dev.yml up -d`.
*   `recreate-db`: Stops the database, prunes Docker volumes, and starts the database again.
*   `stop-db`: Stops the development database using `docker compose -f compose.dev.yml down`.
*   `delete-migrations`: Deletes the `Migrations` directory from `src\\ArgonFetch.Infrastructure`.
*   `full-reset`: Performs a full reset by deleting the migrations directory, recreating the database, and adding an initial 'Init' migration.
*   `help`: Shows the help message with all available commands.

**Example:**

To add a new migration:

```powershell
.\Db-Script.ps1 -Command add-migration
```

### `Db-Script-GUI.py`

This Python script provides a graphical user interface (GUI) for the database and migration management tasks available in `Db-Script.ps1`. It offers a more visual way to perform operations like starting/stopping the database, adding migrations, etc.

**Requirements:**

*   Python 3.x
*   CustomTkinter library (`pip install customtkinter`)

**Launching the GUI:**

For Windows users, the recommended way to start the GUI is by using the `start_argonfetch_gui.vbs` script located in the `scripts` directory.

*   **`start_argonfetch_gui.vbs`**:
    *   **Purpose**: This VBScript utility starts the `Db-Script-GUI.py` application silently (without a console window appearing), providing a cleaner user experience.
    *   **Usage**: Simply double-click the `start_argonfetch_gui.vbs` file in the `scripts` directory. It will automatically locate and run the Python GUI. This is the easiest way to launch the GUI on Windows.

**Alternative (Command Line):**

If you prefer, or are not on Windows, you can run the GUI directly from the command line. Navigate to the `scripts` directory and execute:

```bash
python Db-Script-GUI.py
```
Note: This method might open a console window alongside the GUI.