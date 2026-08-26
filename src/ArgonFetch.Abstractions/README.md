# ArgonFetch.Abstractions

Everything you need to write an ArgonFetch source plugin, and nothing else.

yt-dlp is what fetches things the ordinary way. A plugin exists for links that need something
else first: one that has to become a different link before anything can be downloaded, one that
lists a collection, or one that a separate piece of code fetches on its own.

```csharp
[assembly: ArgonFetchPlugin("example", ArgonFetchPluginAttribute.CurrentAbi, Name = "Example")]

public sealed class ExampleProvider : ISourceProvider
{
    public string Id => "example";

    public bool CanHandle(Uri url) => url.Host.EndsWith("example.com");

    public async Task<ProviderOutcome> PrepareAsync(Uri url, IProviderContext ctx, CancellationToken ct)
    {
        var real = await FindRealUrl(url, ctx, ct);

        return ProviderOutcome.Rewrite(real, new MediaTags("Title", "Artist"));
    }
}
```

## Reference this without shipping it

```xml
<PackageReference Include="ArgonFetch.Abstractions" Version="1.0.0" ExcludeAssets="runtime" />
```

`ExcludeAssets="runtime"` matters. The host loads this assembly and hands your plugin types from
its own copy. A plugin that carries its own copy gets a second, unrelated `ISourceProvider`, and
the load fails complaining that `ISourceProvider` cannot be cast to `ISourceProvider`.

## Rules worth knowing before you write one

- `CanHandle` is asked of every installed plugin on every request. Keep it to inspecting the URL:
  no network, no throwing, no waiting.
- Return raw addresses in `MediaStream`. Caching them, hiding them behind keys and building the
  URLs a client sees is the host's business, and it changes.
- Cache through `IProviderContext.CacheKey`, never a bare string - the cache is shared.

MIT licensed, so what you write with it is yours to license as you please.
