using System.Security.Cryptography;
using System.Text;
using MediaBrowser.Common.Configuration;

namespace Jellyfin.Plugin.WhereToWatch.Rendering;

/// <summary>
/// Disposable on-disk cache of rendered posters.
/// </summary>
public sealed class RenderCache
{
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
    /// <returns>The JPEG bytes, or null on a miss.</returns>
    public byte[]? Get(string key)
    {
        var path = PathFor(key);
        return File.Exists(path) ? File.ReadAllBytes(path) : null;
    }

    /// <summary>
    /// Stores a render.
    /// </summary>
    /// <param name="key">Cache key.</param>
    /// <param name="bytes">JPEG bytes.</param>
    public void Set(string key, byte[] bytes)
    {
        Directory.CreateDirectory(_root);
        var path = PathFor(key);
        var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        File.WriteAllBytes(temp, bytes);
        File.Move(temp, path, overwrite: true);
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

    private string PathFor(string key) => Path.Combine(_root, key + ".jpg");
}
