using Jellyfin.Plugin.KavasakiPresence.Services;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.KavasakiPresence;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServerApplicationHost applicationHost)
    {
        serviceCollection.AddSingleton<DiscordService>();
        serviceCollection.AddSingleton<ImdbService>();
        serviceCollection.AddHostedService<PlaybackMonitorService>();
        serviceCollection.AddHttpClient("KavasakiPresence");
    }
}
