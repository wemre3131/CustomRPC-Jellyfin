using System;
using System.Threading;
using System.Threading.Tasks;
using Jellyfin.Plugin.KavasakiPresence.Configuration;
using Jellyfin.Plugin.KavasakiPresence.Services;
using MediaBrowser.Controller.Library;
using MediaBrowser.Controller.Session;
using MediaBrowser.Model.Entities;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KavasakiPresence.Services;

/// <summary>
/// Background service that listens for Jellyfin playback events
/// and updates Discord Rich Presence accordingly.
/// </summary>
public class PlaybackMonitorService : IHostedService, IDisposable
{
    private readonly ISessionManager _sessionManager;
    private readonly ILogger<PlaybackMonitorService> _logger;
    private readonly DiscordService _discordService;
    private readonly ImdbService _imdbService;
    private bool _disposed;

    /// <summary>
    /// Initializes a new instance of the <see cref="PlaybackMonitorService"/> class.
    /// </summary>
    public PlaybackMonitorService(
        ISessionManager sessionManager,
        ILogger<PlaybackMonitorService> logger,
        DiscordService discordService,
        ImdbService imdbService)
    {
        _sessionManager = sessionManager;
        _logger = logger;
        _discordService = discordService;
        _imdbService = imdbService;
    }

    /// <inheritdoc />
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart += OnPlaybackStart;
        _sessionManager.PlaybackStopped += OnPlaybackStopped;
        _sessionManager.PlaybackProgress += OnPlaybackProgress;

        _logger.LogInformation("[KavasakiPresence] Playback monitor started.");

        // Connect to Discord if token is configured
        var config = Plugin.Instance?.Configuration;
        if (config is not null && !string.IsNullOrEmpty(config.DiscordToken) && config.IsEnabled)
        {
            await _discordService.ConnectAsync(config.DiscordToken, cancellationToken).ConfigureAwait(false);
        }
    }

    /// <inheritdoc />
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _sessionManager.PlaybackStart -= OnPlaybackStart;
        _sessionManager.PlaybackStopped -= OnPlaybackStopped;
        _sessionManager.PlaybackProgress -= OnPlaybackProgress;

        await _discordService.ClearPresenceAsync(cancellationToken).ConfigureAwait(false);
        await _discordService.DisconnectAsync().ConfigureAwait(false);

        _logger.LogInformation("[KavasakiPresence] Playback monitor stopped.");
    }

    private async void OnPlaybackStart(object? sender, PlaybackProgressEventArgs e)
    {
        try
        {
            await UpdatePresenceFromEventAsync(e).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KavasakiPresence] Error on playback start.");
        }
    }

    private async void OnPlaybackProgress(object? sender, PlaybackProgressEventArgs e)
    {
        // Only update every 30 seconds to avoid spam
        if (e.Session?.PlayState?.PositionTicks % (TimeSpan.TicksPerSecond * 30) < TimeSpan.TicksPerSecond * 2)
        {
            try
            {
                await UpdatePresenceFromEventAsync(e).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[KavasakiPresence] Error on playback progress.");
            }
        }
    }

    private async void OnPlaybackStopped(object? sender, PlaybackStopEventArgs e)
    {
        try
        {
            await _discordService.ClearPresenceAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KavasakiPresence] Error on playback stop.");
        }
    }

    private async Task UpdatePresenceFromEventAsync(PlaybackProgressEventArgs e)
    {
        var config = Plugin.Instance?.Configuration;
        if (config is null || !config.IsEnabled || string.IsNullOrEmpty(config.DiscordToken))
            return;

        var item = e.Item;
        if (item is null) return;

        string title = item.Name ?? "Unknown";
        int? year = item.ProductionYear;
        string mediaType = item.GetType().Name; // Movie, Episode, Audio, etc.

        // Determine episode info for TV shows
        string? episodeInfo = null;
        if (item is MediaBrowser.Controller.Entities.TV.Episode episode)
        {
            title = episode.SeriesName ?? title;
            if (config.ShowEpisodeInfo)
            {
                episodeInfo = $"S{episode.ParentIndexNumber:D2}E{episode.IndexNumber:D2}";
                if (!string.IsNullOrEmpty(episode.Name))
                    episodeInfo += $" - {episode.Name}";
            }
            year = episode.PremiereDate?.Year;
        }

        // Look up IMDB ID and rating
        string? imdbId = item.GetProviderId(MetadataProvider.Imdb);
        double? imdbRating = null;

        if (config.ShowImdbRating || config.ShowImdbButton)
        {
            if (!string.IsNullOrEmpty(imdbId))
            {
                // We have an IMDB ID from Jellyfin metadata — get the rating
                imdbRating = await FetchImdbRatingDirectAsync(imdbId).ConfigureAwait(false);
            }
            else
            {
                // Search IMDB by title + year
                var imdbResult = await _imdbService.SearchAsync(title, year).ConfigureAwait(false);
                if (imdbResult is not null)
                {
                    imdbId = imdbResult.ImdbId;
                    imdbRating = imdbResult.Rating;
                }
            }
        }

        // Build Jellyfin URL if configured
        string? jellyfinUrl = null;
        if (config.ShowJellyfinButton && !string.IsNullOrEmpty(config.JellyfinPublicUrl))
        {
            jellyfinUrl = $"{config.JellyfinPublicUrl.TrimEnd('/')}/web/index.html#!/details?id={item.Id}";
        }

        // Calculate start timestamp (when playback started = now - current position)
        long? startTimestamp = null;
        if (config.ShowTimestamp && e.Session?.PlayState?.PositionTicks.HasValue == true)
        {
            var positionSeconds = e.Session.PlayState.PositionTicks!.Value / TimeSpan.TicksPerSecond;
            startTimestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - positionSeconds;
        }

        var presenceData = new RichPresenceData
        {
            Title = title,
            Year = year,
            MediaType = GetFriendlyMediaType(mediaType),
            EpisodeInfo = episodeInfo,
            ImdbId = imdbId,
            ImdbRating = imdbRating,
            StartTimestamp = startTimestamp,
            JellyfinUrl = jellyfinUrl,
            ShowImdbButton = config.ShowImdbButton,
            ShowImdbRating = config.ShowImdbRating,
            ShowJellyfinButton = config.ShowJellyfinButton,
            ShowTimestamp = config.ShowTimestamp,
            ShowEpisodeInfo = config.ShowEpisodeInfo,
            ShowYear = config.ShowYear,
            ShowMediaType = config.ShowMediaType
        };

        await _discordService.UpdatePresenceAsync(presenceData).ConfigureAwait(false);
    }

    private async Task<double?> FetchImdbRatingDirectAsync(string imdbId)
    {
        // Uses the ImdbService's internal rating fetcher
        var tempResult = await _imdbService.SearchAsync(imdbId, null).ConfigureAwait(false);
        return tempResult?.Rating;
    }

    private static string GetFriendlyMediaType(string type) => type switch
    {
        "Movie" => "🎬 Movie",
        "Episode" => "📺 TV Show",
        "Audio" => "🎵 Music",
        "MusicVideo" => "🎬 Music Video",
        "LiveTvChannel" => "📡 Live TV",
        "Book" => "📖 Book",
        _ => "🎮 Media"
    };

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _discordService.Dispose();
        GC.SuppressFinalize(this);
    }
}
