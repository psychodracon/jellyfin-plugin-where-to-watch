using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.WhereToWatch.Rendering;

/// <summary>
/// Disposable on-disk cache of rendered posters.
/// </summary>
public sealed partial class RenderCache
{
    /// <summary>
    /// The size the cache is kept under.
    /// </summary>
    public const long MaxBytes = 512L * 1024 * 1024;

    private const long PruneEveryBytes = 32L * 1024 * 1024;

    private static readonly (string Extension, string ContentType)[] Types =
    [
        (".jpg", "image/jpeg"),
        (".png", "image/png"),
        (".webp", "image/webp"),
    ];

    // Query parameters of Jellyfin's image endpoint that change the returned bytes.
    private static readonly HashSet<string> ImageParameters = new(
        [
            "tag", "format", "quality", "width", "height", "maxWidth", "maxHeight", "fillWidth", "fillHeight",
            "blur", "backgroundColor", "foregroundLayer", "cropWhitespace", "addPlayedIndicator", "percentPlayed", "unplayedCount", "imageIndex",
        ],
        StringComparer.OrdinalIgnoreCase);

    private readonly string _root;
    private long _writtenSincePrune;
    private int _pruning;

    /// <summary>
    /// Initializes a new instance of the <see cref="RenderCache"/> class.
    /// </summary>
    /// <param name="paths">Jellyfin application paths.</param>
    public RenderCache(IApplicationPaths paths)
        : this(Path.Combine(paths.CachePath, "wheretowatch"))
    {
    }

    internal RenderCache(string root)
    {
        _root = root;
    }

    /// <summary>
    /// Builds a cache key from the parts that determine a rendered image.
    /// </summary>
    /// <param name="parts">Everything the render depends on.</param>
    /// <returns>A hex key.</returns>
    public static string Key(params string[] parts)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join('\n', parts))));
    }

    /// <summary>
    /// Reduces a query string to the parameters that change what Jellyfin returns for an image, so unrelated
    /// parameters (api keys, cache busters) neither multiply cache entries nor let a client fill the cache.
    /// </summary>
    /// <param name="queryString">The raw query string, with or without the leading '?'.</param>
    /// <returns>Whitelisted parameters, lower-cased by name and sorted.</returns>
    public static string NormalizeQuery(string queryString)
    {
        var query = Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(queryString);
        return string.Join(
            '&',
            query
                .Where(q => ImageParameters.Contains(q.Key))
                .OrderBy(q => q.Key, StringComparer.OrdinalIgnoreCase)
                .Select(q => q.Key.ToLowerInvariant() + "=" + q.Value.ToString()));
    }

    /// <summary>
    /// Reduces an Accept header to the image formats Jellyfin chooses between.
    /// </summary>
    /// <param name="accept">The Accept header value.</param>
    /// <returns>The recognised image types, sorted.</returns>
    public static string NormalizeAccept(string accept)
    {
        return string.Join(
            ',',
            AcceptedFormat().Matches(accept).Select(m => m.Value.ToLowerInvariant()).Distinct().Order(StringComparer.Ordinal));
    }

    /// <summary>
    /// Reads a cached render.
    /// </summary>
    /// <param name="key">Cache key.</param>
    /// <returns>The image, or null on a miss or when the file cannot be read.</returns>
    public RenderedImage? Get(string key)
    {
        foreach (var (extension, contentType) in Types)
        {
            try
            {
                return new RenderedImage(File.ReadAllBytes(PathFor(key, extension)), contentType);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Missing, or removed by a prune while reading: treated as a miss.
            }
        }

        return null;
    }

    /// <summary>
    /// Stores a render. A failure to write is not an error: the cache is only an optimisation.
    /// </summary>
    /// <param name="key">Cache key.</param>
    /// <param name="image">The rendered image.</param>
    /// <returns>True when the render was stored.</returns>
    public bool Set(string key, RenderedImage image)
    {
        var extension = Types.FirstOrDefault(t => t.ContentType == image.ContentType).Extension;
        if (extension is null)
        {
            return false;
        }

        string? temp = null;
        try
        {
            Directory.CreateDirectory(_root);
            var path = PathFor(key, extension);
            temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
            File.WriteAllBytes(temp, image.Bytes);
            File.Move(temp, path, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            TryDelete(temp);
            return false;
        }

        // Renders can be requested between scans, so the size limit is also enforced as the cache grows.
        if (Interlocked.Add(ref _writtenSincePrune, image.Bytes.Length) >= PruneEveryBytes
            && Interlocked.CompareExchange(ref _pruning, 1, 0) == 0)
        {
            Interlocked.Exchange(ref _writtenSincePrune, 0);
            _ = Task.Run(() =>
            {
                try
                {
                    Prune(MaxBytes);
                }
                finally
                {
                    Interlocked.Exchange(ref _pruning, 0);
                }
            });
        }

        return true;
    }

    /// <summary>
    /// Deletes the oldest renders until the cache fits the size limit.
    /// </summary>
    /// <param name="maxBytes">Maximum total size of cached renders.</param>
    public void Prune(long maxBytes)
    {
        List<FileInfo> files;
        try
        {
            if (!Directory.Exists(_root))
            {
                return;
            }

            files = new DirectoryInfo(_root).GetFiles().Where(f => Types.Any(t => t.Extension == f.Extension)).OrderBy(f => f.LastWriteTimeUtc).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return;
        }

        var total = files.Sum(f => f.Length);
        foreach (var file in files)
        {
            if (total <= maxBytes)
            {
                break;
            }

            total -= file.Length;
            TryDelete(file.FullName);
        }
    }

    /// <summary>
    /// Deletes every cached render.
    /// </summary>
    public void Clear()
    {
        try
        {
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A render being written at the same moment can hold the folder; the rest is pruned later.
        }
    }

    [GeneratedRegex(@"image/(?:webp|avif|png|jpe?g|gif|apng)", RegexOptions.IgnoreCase)]
    private static partial Regex AcceptedFormat();

    private static void TryDelete(string? path)
    {
        try
        {
            if (path is not null)
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    }

    private string PathFor(string key, string extension) => Path.Combine(_root, key + extension);
}
