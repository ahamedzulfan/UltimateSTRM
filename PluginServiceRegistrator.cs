using Jellyfin.Plugin.UltimateStrm.CustomIframe.Data;
using Jellyfin.Plugin.UltimateStrm.Strm.Data;
using Jellyfin.Plugin.UltimateStrm.Yt2Strm.Data;
using Jellyfin.Plugin.UltimateStrm.Yt2Strm.Resolver;
using Jellyfin.Plugin.UltimateStrm.Yt2Strm.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.UltimateStrm;

/// <summary>
/// Registers every sub-module's services with Jellyfin's dependency injection container.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        // --- Strm Manager ---
        serviceCollection.AddSingleton<StrmRepository>();

        // --- yt2strm ---
        serviceCollection.AddSingleton<LinkRepository>();
        serviceCollection.AddSingleton<YtDlpResolver>();
        serviceCollection.AddSingleton<ActivityLog>();

        // LinkRefreshService is both a regular singleton (so the API controller and
        // the scheduled task can call methods on it directly) AND the IHostedService
        // that owns the background worker loop. Registering it both ways against the
        // same factory means everyone shares the one running instance.
        serviceCollection.AddSingleton<LinkRefreshService>();
        serviceCollection.AddHostedService<LinkRefreshService>(provider =>
            provider.GetRequiredService<LinkRefreshService>());

        // --- Custom Iframe Player ---
        serviceCollection.AddSingleton<IframeMappingRepository>();
    }
}
