using Jellyfin.Plugin.KavasakiPresence.Services;
using MediaBrowser.Common.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.KavasakiPresence;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection)
    {
        serviceCollection.AddSingleton<DiscordService>();
        serviceCollection.AddSingleton<ImdbService>();
        serviceCollection.AddHostedService<PlaybackMonitorService>();
        serviceCollection.AddHttpClient("KavasakiPresence");
    }
}
