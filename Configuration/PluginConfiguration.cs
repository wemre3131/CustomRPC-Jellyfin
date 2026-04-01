using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.KavasakiPresence.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string DiscordToken { get; set; } = string.Empty;
}
