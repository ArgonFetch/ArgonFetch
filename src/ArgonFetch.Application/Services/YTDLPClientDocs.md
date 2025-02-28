# YTDLPClient Documentation

## Overview
The `YTDLPClient` class is a wrapper for the `yt-dlp` command-line tool, providing methods to interact with video platforms. This class allows users to verify the existence of `yt-dlp`, retrieve video information, and execute commands asynchronously.

## Initialization
The `YTDLPClient` constructor determines the appropriate `yt-dlp` executable path based on the operating system.

```csharp
public YTDLPClient()
{
    _ytdlpPath = GetYtdlpPath();
}
```

### Determining Executable Path
The `GetYtdlpPath` method ensures the correct executable is used for the operating system.

```csharp
private static string GetYtdlpPath()
{
    return RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
        ? "yt-dlp.exe"
        : "yt-dlp";
}
```

## Methods

### VerifyYtdlpExistsAsync
Checks if the `yt-dlp` executable is available and accessible on the system.

```csharp
public async Task<bool> VerifyYtdlpExistsAsync()
```

**Returns:**
- `true` if `yt-dlp` is available.
- `false` if `yt-dlp` is missing or an error occurs.

### GetVideoInfoAsync
Retrieves detailed information about a video given its URL.

```csharp
public async Task<string> GetVideoInfoAsync(string url)
```

**Parameters:**
- `url` (string): The URL of the video to fetch information from.

**Returns:**
- A JSON string containing video details.

**Exceptions:**
- Throws an exception if `yt-dlp` fails to retrieve video information.

### ExecuteCommandAsync
Executes a `yt-dlp` command with the specified arguments.

```csharp
private async Task<string> ExecuteCommandAsync(string arguments)
```

**Parameters:**
- `arguments` (string): Command-line arguments to pass to `yt-dlp`.

**Returns:**
- The command output as a string.

**Exceptions:**
- Throws an exception if the command execution fails.

## Usage Example

```csharp
var client = new YTDLPClient();
if (await client.VerifyYtdlpExistsAsync())
{
    string videoInfo = await client.GetVideoInfoAsync("https://www.youtube.com/watch?v=example");
    Console.WriteLine(videoInfo);
}
else
{
    Console.WriteLine("yt-dlp is not available.");
}
```

## Error Handling
If `yt-dlp` fails to execute, an exception is thrown with the error message captured from the standard error output.