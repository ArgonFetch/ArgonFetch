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
        return LoadVersionFromAssembly() ?? LoadVersionFromPropertiesFile() ?? UnknownVersion;
    }

    private static string? LoadVersionFromAssembly()
    {
        var assembly = typeof(ApplicationInfoService).Assembly;

        var informationalVersion = assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            var version = informationalVersion.Split('+', 2)[0];
            if (!string.IsNullOrWhiteSpace(version))
            {
                return version;
            }
        }

        var assemblyVersion = assembly.GetName().Version;

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
            var currentDirectory = Directory.GetCurrentDirectory();
            var propertiesPath = Path.Combine(currentDirectory, "application.properties");

            if (!File.Exists(propertiesPath))
            {
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
