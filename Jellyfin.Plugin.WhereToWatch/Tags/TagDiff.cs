namespace Jellyfin.Plugin.WhereToWatch.Tags;

/// <summary>
/// Tags to add to and remove from an item.
/// </summary>
/// <param name="ToAdd">Tags the item should gain.</param>
/// <param name="ToRemove">Tags the item should lose.</param>
public sealed record TagChanges(IReadOnlyList<string> ToAdd, IReadOnlyList<string> ToRemove);

/// <summary>
/// Computes which <c>stream:</c> tags an item needs.
/// </summary>
public static class TagDiff
{
    /// <summary>
    /// Prefix reserved for tags managed by this plugin.
    /// </summary>
    public const string Prefix = "stream:";

    /// <summary>
    /// Compares an item's current tags with the providers it should be tagged with.
    /// </summary>
    /// <param name="existingTags">All tags currently on the item.</param>
    /// <param name="providerNames">Names of the providers the item is available on.</param>
    /// <returns>The tags to add and remove.</returns>
    public static TagChanges Compute(IEnumerable<string> existingTags, IEnumerable<string> providerNames)
    {
        var desired = providerNames.Select(name => Prefix + name).ToList();
        var existing = existingTags.ToList();

        var toAdd = desired.Where(tag => !existing.Contains(tag, StringComparer.OrdinalIgnoreCase)).ToList();
        var toRemove = existing
            .Where(IsManaged)
            .Where(tag => !desired.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .ToList();
        return new TagChanges(toAdd, toRemove);
    }

    /// <summary>
    /// Tells whether a tag is one this plugin manages.
    /// </summary>
    /// <param name="tag">A tag.</param>
    /// <returns>True when the tag carries the <c>stream:</c> prefix.</returns>
    public static bool IsManaged(string tag) => tag.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Applies computed changes to a tag list.
    /// </summary>
    /// <param name="existingTags">The item's current tags.</param>
    /// <param name="changes">Changes from <see cref="Compute"/>.</param>
    /// <returns>The new tag list; tags that are not managed are kept as they were.</returns>
    public static string[] Apply(IEnumerable<string> existingTags, TagChanges changes)
    {
        return existingTags
            .Where(tag => !changes.ToRemove.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .Concat(changes.ToAdd)
            .ToArray();
    }
}
