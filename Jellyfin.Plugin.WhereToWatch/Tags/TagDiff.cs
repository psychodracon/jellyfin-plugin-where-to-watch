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
            .Where(tag => tag.StartsWith(Prefix, StringComparison.OrdinalIgnoreCase))
            .Where(tag => !desired.Contains(tag, StringComparer.OrdinalIgnoreCase))
            .ToList();
        return new TagChanges(toAdd, toRemove);
    }
}
