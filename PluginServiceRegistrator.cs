using Jellyfin.Plugin.KavasakiPresence.Services;
using MediaBrowser.Controller.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.KavasakiPresence;

public class PluginServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection serviceCollection, IServiceProvider applicationServiceProvider)
    {
        serviceCollection.AddSingleton<DiscordService>();
        serviceCollection.AddSingleton<ImdbService>();
        serviceCollection.AddHostedService<PlaybackMonitorService>();
        serviceCollection.AddHttpClient("KavasakiPresence");
    }
}
