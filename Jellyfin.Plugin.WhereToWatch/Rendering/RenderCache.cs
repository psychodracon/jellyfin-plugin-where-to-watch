using System.Security.Cryptography;
using System.Text;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.WhereToWatch.Rendering;

/// <summary>
/// Disposable on-disk cache of rendered posters.
/// </summary>
public sealed class RenderCache
{
    private static readonly (string Extension, string ContentType)[] Types =
    [
        (".jpg", "image/jpeg"),
        (".png", "image/png"),
        (".webp", "image/webp"),
    ];

    private readonly string _root;

    /// <summary>
    /// Initializes a new instance of the <see cref="RenderCache"/> class.
    /// </summary>
    /// <param name="paths">Jellyfin application paths.</param>
    public RenderCache(IApplicationPaths paths)
    {
        _root = Path.Combine(paths.CachePath, "wheretowatch");
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
    /// Reads a cached render.
    /// </summary>
    /// <param name="key">Cache key.</param>
    /// <returns>The image, or null on a miss.</returns>
    public RenderedImage? Get(string key)
    {
        foreach (var (extension, contentType) in Types)
        {
            var path = PathFor(key, extension);
            if (File.Exists(path))
            {
                return new RenderedImage(File.ReadAllBytes(path), contentType);
            }
        }

        return null;
    }

    /// <summary>
    /// Stores a render.
    /// </summary>
    /// <param name="key">Cache key.</param>
    /// <param name="image">The rendered image.</param>
    public void Set(string key, RenderedImage image)
    {
        Directory.CreateDirectory(_root);
        var extension = Types.First(t => t.ContentType == image.ContentType).Extension;
        var path = PathFor(key, extension);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, image.Bytes);
        File.Move(temp, path, overwrite: true);
    }

    /// <summary>
    /// Deletes the oldest renders until the cache fits the size limit.
    /// </summary>
    /// <param name="maxBytes">Maximum total size of cached renders.</param>
    public void Prune(long maxBytes)
    {
        if (!Directory.Exists(_root))
        {
            return;
        }

        var files = new DirectoryInfo(_root).GetFiles().Where(f => Types.Any(t => t.Extension == f.Extension)).OrderBy(f => f.LastWriteTimeUtc).ToList();
        var total = files.Sum(f => f.Length);
        foreach (var file in files)
        {
            if (total <= maxBytes)
            {
                break;
            }

            total -= file.Length;
            file.Delete();
        }
    }

    /// <summary>
    /// Deletes every cached render.
    /// </summary>
    public void Clear()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string PathFor(string key, string extension) => Path.Combine(_root, key + extension);
}
