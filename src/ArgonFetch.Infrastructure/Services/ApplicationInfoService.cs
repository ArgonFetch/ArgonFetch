using ArgonFetch.Application.Services;
using System.Reflection;
using System.Xml.Linq;

namespace ArgonFetch.Infrastructure.Services;

public class ApplicationInfoService : IApplicationInfoService
{
    private const string UnknownVersion = "unknown";

    private readonly string _version;

    public ApplicationInfoService()
    {
        _version = LoadVersion();
    }

    public string GetVersion()
    {
        return _version;
    }

    private static string LoadVersion()
    {
        // The version is stamped into the assembly at build time from
        // application.properties (see ReadVersionFromProperties in ArgonFetch.API.csproj),
        // so the assembly is the reliable source at runtime - the properties file itself
        // is not part of the published output.
        return LoadVersionFromAssembly() ?? LoadVersionFromPropertiesFile() ?? UnknownVersion;
    }

    private static string? LoadVersionFromAssembly()
    {
        var assembly = typeof(ApplicationInfoService).Assembly;

        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        // AssemblyInformationalVersion carries the source revision as "0.1.1+<commit sha>"
        // when SourceLink is active; only the version part is wanted here.
        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var version = informationalVersion.Split('+', 2)[0];
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version;
            }
        }

        var assemblyVersion = assembly.GetName().Version;

        // AssemblyVersion is always four-part; application.properties uses three,
        // so drop a trailing zero revision to keep the two in sync.
        return assemblyVersion is null
            ? null
            : assemblyVersion.Revision == 0
                ? assemblyVersion.ToString(3)
                : assemblyVersion.ToString();
    }

    private static string? LoadVersionFromPropertiesFile()
    {
        try
        {
            // Development fallback: application.properties lives at the repository root.
            var currentDirectory = Directory.GetCurrentDirectory();
            var propertiesPath = Path.Combine(currentDirectory, "application.properties");

            // Check if running from src/ArgonFetch.API directory
            if (!File.Exists(propertiesPath))
            {
                // Try going up directories to find the root
                var parentPath = Path.Combine(currentDirectory, "..", "..", "application.properties");
                if (File.Exists(parentPath))
                {
                    propertiesPath = Path.GetFullPath(parentPath);
                }
            }

            if (!File.Exists(propertiesPath))
            {
                return null;
            }

            var doc = XDocument.Load(propertiesPath);
            var versionElement = doc.Root?.Element("version");

            return string.IsNullOrWhiteSpace(versionElement?.Value) ? null : versionElement.Value;
        }
        catch
        {
            return null;
        }
    }
}
