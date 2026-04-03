using MediaBrowser.Model.Plugins;

namespace Jellyfin.Plugin.KavasakiPresence.Configuration;

public class PluginConfiguration : BasePluginConfiguration
{
    public string DiscordToken { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
    public bool ShowTitle { get; set; } = true;
    public bool ShowMediaType { get; set; } = true;
    public bool ShowImdbRating { get; set; } = true;
    public bool ShowImdbButton { get; set; } = true;
    public bool ShowTimestamp { get; set; } = true;
    public bool ShowEpisodeInfo { get; set; } = true;
    public bool ShowYear { get; set; } = true;
    public string CustomStatusText { get; set; } = string.Empty;
    public string DiscordAppClientId { get; set; } = "1489212028252979270";
    public bool ShowJellyfinButton { get; set; } = false;
    public string JellyfinPublicUrl { get; set; } = string.Empty;

    public PluginConfiguration()
    {
        // Default constructor
    }
}
