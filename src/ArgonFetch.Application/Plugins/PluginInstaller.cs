using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using ArgonFetch.Abstractions;
using Microsoft.Extensions.Logging;

namespace ArgonFetch.Application.Plugins
{
    public class PluginInstaller
    {
        private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

        private readonly IHttpClientFactory _httpClientFactory;
        private readonly ILogger<PluginInstaller> _logger;

        public PluginInstaller(IHttpClientFactory httpClientFactory, ILogger<PluginInstaller> logger)
        {
            _httpClientFactory = httpClientFactory;
            _logger = logger;
        }

        public async Task InstallAsync(PluginOptions options, string root, CancellationToken cancellationToken = default)
        {
            try
            {
                Directory.CreateDirectory(root);
            }
            catch (Exception ex)
            {
                // Nothing below can work, but a downloader that will not start is worse than one
                // without plugins - and this threw before any of the guards further down.
                _logger.LogError(ex, "Cannot use {Root} for plugins, so none will be installed", root);
                return;
            }

            var wanted = options.Install
                .Select(Parse)
                .Where(request => request is not null)
                .Select(request => request!.Value)
                .ToList();

            Prune(root, wanted.Select(w => w.Id).ToHashSet(StringComparer.OrdinalIgnoreCase));

            if (wanted.Count == 0)
                return;

            var index = await ReadIndexesAsync(options.Repositories, cancellationToken);

            foreach (var (id, version) in wanted)
            {
                try
                {
                    await InstallOneAsync(root, id, version, index, cancellationToken);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Could not install the {Id} plugin", id);
                }
            }
        }

        private async Task InstallOneAsync(
            string root,
            string id,
            string? version,
            IReadOnlyDictionary<string, List<(PluginIndexEntry Entry, Uri BaseUri)>> index,
            CancellationToken cancellationToken)
        {
            var installed = InstalledVersion(root, id);

            if (!index.TryGetValue(id, out var candidates))
            {
                if (installed is not null)
                {
                    _logger.LogInformation("Keeping the {Id} plugin at {Version}; no repository listed it", id, installed);
                    return;
                }

                _logger.LogWarning("No repository offers a plugin called {Id}", id);
                return;
            }

            var compatible = candidates
                .Where(c => c.Entry.Abi == ArgonFetchPluginAttribute.CurrentAbi)
                .Where(c => version is null || string.Equals(c.Entry.Version, version, StringComparison.OrdinalIgnoreCase))
                .OrderByDescending(c => ParseVersion(c.Entry.Version))
                .ToList();

            if (compatible.Count == 0)
            {
                _logger.LogWarning(
                    "No build of {Id} matches{Pinned} and contract {Abi}",
                    id, version is null ? string.Empty : $" version {version}", ArgonFetchPluginAttribute.CurrentAbi);
                return;
            }

            var (entry, baseUri) = compatible[0];

            if (installed == entry.Version)
                return;

            _logger.LogInformation("Installing {Id} {Version}", id, entry.Version);

            await DownloadAsync(root, entry, baseUri, cancellationToken);
        }

        private async Task DownloadAsync(string root, PluginIndexEntry entry, Uri baseUri, CancellationToken cancellationToken)
        {
            var httpClient = _httpClientFactory.CreateClient();
            var address = new Uri(baseUri, entry.File);

            var bytes = await httpClient.GetByteArrayAsync(address, cancellationToken);
            var digest = Convert.ToHexString(SHA256.HashData(bytes));

            if (!digest.Equals(entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"{address} did not hash to what the index said it would.");

            var folder = Path.Combine(root, entry.Id);

            if (Directory.Exists(folder))
                Directory.Delete(folder, recursive: true);

            Directory.CreateDirectory(folder);

            using var archive = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
            archive.ExtractToDirectory(folder, overwriteFiles: true);

            await File.WriteAllTextAsync(Path.Combine(folder, ".version"), entry.Version, cancellationToken);
        }

        private async Task<IReadOnlyDictionary<string, List<(PluginIndexEntry, Uri)>>> ReadIndexesAsync(
            IEnumerable<string> repositories,
            CancellationToken cancellationToken)
        {
            var index = new Dictionary<string, List<(PluginIndexEntry, Uri)>>(StringComparer.OrdinalIgnoreCase);
            var httpClient = _httpClientFactory.CreateClient();

            foreach (var repository in repositories)
            {
                if (!Uri.TryCreate(repository, UriKind.Absolute, out var uri))
                {
                    _logger.LogWarning("Skipping plugin repository {Repository}: not a URL", repository);
                    continue;
                }

                try
                {
                    var body = await httpClient.GetStringAsync(uri, cancellationToken);
                    var entries = JsonSerializer.Deserialize<List<PluginIndexEntry>>(body, Json) ?? [];

                    foreach (var entry in entries)
                    {
                        if (string.IsNullOrWhiteSpace(entry.Id))
                            continue;

                        if (index.TryGetValue(entry.Id, out var existing) && existing[0].Item2 != uri)
                        {
                            _logger.LogWarning(
                                "{Repository} also offers a plugin called {Id}; keeping the one from {Winner}",
                                uri, entry.Id, existing[0].Item2);
                            continue;
                        }

                        if (!index.TryGetValue(entry.Id, out var list))
                            index[entry.Id] = list = [];

                        list.Add((entry, uri));
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not read the plugin repository at {Repository}", uri);
                }
            }

            return index;
        }

        private void Prune(string root, HashSet<string> wanted)
        {
            foreach (var folder in Directory.GetDirectories(root))
            {
                var id = Path.GetFileName(folder);

                if (wanted.Contains(id))
                    continue;

                _logger.LogInformation("Removing the {Id} plugin; it is no longer listed", id);

                try
                {
                    Directory.Delete(folder, recursive: true);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Could not remove the {Id} plugin", id);
                }
            }
        }

        private static string? InstalledVersion(string root, string id)
        {
            var marker = Path.Combine(root, id, ".version");

            return File.Exists(marker) ? File.ReadAllText(marker).Trim() : null;
        }

        internal static (string Id, string? Version)? Parse(string request)
        {
            if (string.IsNullOrWhiteSpace(request))
                return null;

            var parts = request.Split('@', 2, StringSplitOptions.TrimEntries);

            return parts.Length == 2 && parts[1].Length > 0
                ? (parts[0], parts[1])
                : (parts[0], null);
        }

        private static Version ParseVersion(string version) =>
            Version.TryParse(version.Split('-')[0], out var parsed) ? parsed : new Version(0, 0);
    }
}
