using Jellyfin.Plugin.KavasakiPresence.Services;
using MediaBrowser.Common.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.KavasakiPresence;

/// <summary>
/// Registers plugin services.
/// </summary>
public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    /// <inheritdoc />
    public void RegisterServices(IServiceCollection serviceCollection, IServiceProvider applicationServiceProvider)
    {
        serviceCollection.AddSingleton<DiscordService>();
        serviceCollection.AddSingleton<ImdbService>();
        serviceCollection.AddHostedService<PlaybackMonitorService>();
        serviceCollection.AddHttpClient("KavasakiPresence");
    }
}
