using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Jellyfin.Plugin.WhereToWatch.Middleware;

/// <summary>
/// Puts the badge middleware at the front of the request pipeline.
/// </summary>
public sealed class BadgeStartupFilter : IStartupFilter
{
    /// <inheritdoc />
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
    {
        return builder =>
        {
            builder.UseMiddleware<BadgeMiddleware>();
            next(builder);
        };
    }
}
