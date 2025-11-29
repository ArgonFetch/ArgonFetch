using ArgonFetch.Application.Services;
using System.Xml.Linq;

namespace ArgonFetch.Infrastructure.Services;

public class ApplicationInfoService : IApplicationInfoService
{
    private readonly string _version;

    public ApplicationInfoService()
    {
        _version = LoadVersionFromPropertiesFile();
    }

    public string GetVersion()
    {
        return _version;
    }

    private static string LoadVersionFromPropertiesFile()
    {
        try
        {
            // Navigate to the root directory where application.properties is located
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
                return "unknown";
            }

            var doc = XDocument.Load(propertiesPath);
            var versionElement = doc.Root?.Element("version");

            return versionElement?.Value ?? "unknown";
        }
        catch
        {
            return "unknown";
        }
    }
}
