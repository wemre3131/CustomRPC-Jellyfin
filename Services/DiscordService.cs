using System;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KavasakiPresence.Services;

/// <summary>
/// Handles Discord Gateway WebSocket connection and Rich Presence updates.
/// Uses Discord's user token to connect and set custom status.
/// </summary>
public class DiscordService : IDisposable
{
    private readonly ILogger<DiscordService> _logger;
    private ClientWebSocket? _webSocket;
    private CancellationTokenSource? _cts;
    private Timer? _heartbeatTimer;
    private int _heartbeatInterval = 41250;
    private string? _sessionId;
    private int? _lastSequence;
    private bool _disposed;
    private bool _isConnected;
    private RichPresenceData? _pendingPresence;

    /// <summary>
    /// Initializes a new instance of the <see cref="DiscordService"/> class.
    /// </summary>
    public DiscordService(ILogger<DiscordService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Connects to Discord Gateway using user token.
    /// </summary>
    public async Task ConnectAsync(string token, CancellationToken cancellationToken = default)
    {
        if (_isConnected)
        {
            await DisconnectAsync().ConfigureAwait(false);
        }

        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _webSocket = new ClientWebSocket();
        _webSocket.Options.SetRequestHeader("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

        _logger.LogInformation("[KavasakiPresence] Connecting to Discord Gateway...");

        try
        {
            await _webSocket.ConnectAsync(
                new Uri("wss://gateway.discord.gg/?v=10&encoding=json"),
                _cts.Token).ConfigureAwait(false);

            _isConnected = true;
            _ = Task.Run(() => ReceiveLoopAsync(token, _cts.Token), _cts.Token);
            _logger.LogInformation("[KavasakiPresence] Connected to Discord Gateway.");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KavasakiPresence] Failed to connect to Discord Gateway.");
            _isConnected = false;
        }
    }

    private async Task ReceiveLoopAsync(string token, CancellationToken ct)
    {
        var buffer = new byte[8192];
        var messageBuilder = new StringBuilder();

        while (!ct.IsCancellationRequested && _webSocket?.State == WebSocketState.Open)
        {
            try
            {
                var result = await _webSocket.ReceiveAsync(new ArraySegment<byte>(buffer), ct).ConfigureAwait(false);

                if (result.MessageType == WebSocketMessageType.Close)
                {
                    _logger.LogWarning("[KavasakiPresence] Discord closed the connection: {Desc}", result.CloseStatusDescription);
                    _isConnected = false;
                    break;
                }

                messageBuilder.Append(Encoding.UTF8.GetString(buffer, 0, result.Count));

                if (result.EndOfMessage)
                {
                    var message = messageBuilder.ToString();
                    messageBuilder.Clear();
                    await HandleMessageAsync(message, token, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[KavasakiPresence] Error receiving from Discord.");
                break;
            }
        }

        _isConnected = false;
    }

    private async Task HandleMessageAsync(string raw, string token, CancellationToken ct)
    {
        try
        {
            var node = JsonNode.Parse(raw);
            if (node is null) return;

            int op = node["op"]?.GetValue<int>() ?? -1;
            var d = node["d"];
            int? s = node["s"]?.GetValue<int?>();

            if (s.HasValue) _lastSequence = s;

            switch (op)
            {
                case 10: // Hello
                    _heartbeatInterval = d?["heartbeat_interval"]?.GetValue<int>() ?? 41250;
                    StartHeartbeat(ct);
                    await IdentifyAsync(token, ct).ConfigureAwait(false);
                    break;

                case 0: // Dispatch
                    string? t = node["t"]?.GetValue<string>();
                    if (t == "READY")
                    {
                        _sessionId = d?["session_id"]?.GetValue<string>();
                        _logger.LogInformation("[KavasakiPresence] Discord READY. Logged in successfully.");
                        if (_pendingPresence is not null)
                        {
                            await UpdatePresenceAsync(_pendingPresence, ct).ConfigureAwait(false);
                        }
                    }
                    break;

                case 11: // Heartbeat ACK
                    _logger.LogDebug("[KavasakiPresence] Heartbeat acknowledged.");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "[KavasakiPresence] Error handling Discord message.");
        }
    }

    private void StartHeartbeat(CancellationToken ct)
    {
        _heartbeatTimer?.Dispose();
        _heartbeatTimer = new Timer(
            async _ =>
            {
                if (_webSocket?.State == WebSocketState.Open)
                {
                    var hb = JsonSerializer.Serialize(new { op = 1, d = _lastSequence });
                    await SendRawAsync(hb, ct).ConfigureAwait(false);
                }
            },
            null,
            _heartbeatInterval,
            _heartbeatInterval);
    }

    private async Task IdentifyAsync(string token, CancellationToken ct)
    {
        var identify = new
        {
            op = 2,
            d = new
            {
                token,
                capabilities = 16381,
                properties = new
                {
                    os = "Windows",
                    browser = "Discord Client",
                    device = "discord"
                },
                presence = new
                {
                    status = "online",
                    since = 0,
                    activities = Array.Empty<object>(),
                    afk = false
                },
                compress = false,
                client_state = new
                {
                    guild_versions = new { }
                }
            }
        };

        await SendRawAsync(JsonSerializer.Serialize(identify), ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Updates Discord Rich Presence with media info.
    /// </summary>
    public async Task UpdatePresenceAsync(RichPresenceData data, CancellationToken ct = default)
    {
        _pendingPresence = data;

        if (!_isConnected || _webSocket?.State != WebSocketState.Open)
        {
            _logger.LogWarning("[KavasakiPresence] Not connected, queuing presence update.");
            return;
        }

        // Build activity buttons
        var buttons = new System.Collections.Generic.List<object>();

        if (data.ShowImdbButton && !string.IsNullOrEmpty(data.ImdbId))
        {
            buttons.Add(new
            {
                label = $"⭐ IMDB{(data.ShowImdbRating && data.ImdbRating.HasValue ? $" {data.ImdbRating:0.0}/10" : "")}",
                url = $"https://www.imdb.com/title/{data.ImdbId}/"
            });
        }

        if (data.ShowJellyfinButton && !string.IsNullOrEmpty(data.JellyfinUrl))
        {
            buttons.Add(new
            {
                label = "🎬 Watch on Jellyfin",
                url = data.JellyfinUrl
            });
        }

        // Build details string
        string details = data.Title ?? "Unknown Title";
        if (data.ShowYear && data.Year.HasValue)
            details += $" ({data.Year})";

        // Build state string
        string state = string.Empty;
        if (data.ShowImdbRating && data.ImdbRating.HasValue)
            state += $"⭐ {data.ImdbRating:0.0}/10";

        if (data.ShowEpisodeInfo && !string.IsNullOrEmpty(data.EpisodeInfo))
        {
            if (!string.IsNullOrEmpty(state)) state += "  •  ";
            state += data.EpisodeInfo;
        }

        if (string.IsNullOrEmpty(state) && data.ShowMediaType)
            state = data.MediaType ?? "Jellyfin";

        // Build the activity
        object activity;
        if (buttons.Count > 0)
        {
            activity = new
            {
                name = "Jellyfin",
                type = 3, // Watching
                details,
                state = string.IsNullOrEmpty(state) ? "via Kavasaki Presence" : state,
                timestamps = data.ShowTimestamp && data.StartTimestamp.HasValue ? new { start = data.StartTimestamp.Value } : (object?)null,
                assets = new
                {
                    large_image = "jellyfin_logo",
                    large_text = "Kavasaki Presence",
                    small_image = "kavasaki",
                    small_text = "Custom Rich Presence Jellyfin made by Kavasaki"
                },
                buttons
            };
        }
        else
        {
            activity = new
            {
                name = "Jellyfin",
                type = 3,
                details,
                state = string.IsNullOrEmpty(state) ? "via Kavasaki Presence" : state,
                timestamps = data.ShowTimestamp && data.StartTimestamp.HasValue ? new { start = data.StartTimestamp.Value } : (object?)null,
                assets = new
                {
                    large_image = "jellyfin_logo",
                    large_text = "Kavasaki Presence",
                    small_image = "kavasaki",
                    small_text = "Custom Rich Presence Jellyfin made by Kavasaki"
                }
            };
        }

        var payload = new
        {
            op = 3,
            d = new
            {
                since = (long?)null,
                activities = new[] { activity },
                status = "online",
                afk = false
            }
        };

        await SendRawAsync(JsonSerializer.Serialize(payload), ct).ConfigureAwait(false);
        _logger.LogInformation("[KavasakiPresence] Presence updated: {Title}", data.Title);
        _pendingPresence = null;
    }

    /// <summary>
    /// Clears the Rich Presence (when playback stops).
    /// </summary>
    public async Task ClearPresenceAsync(CancellationToken ct = default)
    {
        if (!_isConnected || _webSocket?.State != WebSocketState.Open)
            return;

        _pendingPresence = null;

        var payload = new
        {
            op = 3,
            d = new
            {
                since = (long?)null,
                activities = Array.Empty<object>(),
                status = "online",
                afk = false
            }
        };

        await SendRawAsync(JsonSerializer.Serialize(payload), ct).ConfigureAwait(false);
        _logger.LogInformation("[KavasakiPresence] Presence cleared.");
    }

    private async Task SendRawAsync(string json, CancellationToken ct)
    {
        if (_webSocket?.State != WebSocketState.Open) return;

        var bytes = Encoding.UTF8.GetBytes(json);
        await _webSocket.SendAsync(
            new ArraySegment<byte>(bytes),
            WebSocketMessageType.Text,
            true,
            ct).ConfigureAwait(false);
    }

    /// <summary>
    /// Disconnects from Discord Gateway.
    /// </summary>
    public async Task DisconnectAsync()
    {
        _heartbeatTimer?.Dispose();
        _cts?.Cancel();

        if (_webSocket?.State == WebSocketState.Open)
        {
            try
            {
                await _webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, "Goodbye", CancellationToken.None)
                    .ConfigureAwait(false);
            }
            catch { /* ignore on disconnect */ }
        }

        _isConnected = false;
        _webSocket?.Dispose();
        _webSocket = null;
    }

    /// <inheritdoc />
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _heartbeatTimer?.Dispose();
        _cts?.Cancel();
        _cts?.Dispose();
        _webSocket?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Data model for Rich Presence.
/// </summary>
public class RichPresenceData
{
    public string? Title { get; set; }
    public int? Year { get; set; }
    public string? MediaType { get; set; }
    public string? EpisodeInfo { get; set; }
    public string? ImdbId { get; set; }
    public double? ImdbRating { get; set; }
    public long? StartTimestamp { get; set; }
    public string? JellyfinUrl { get; set; }
    public bool ShowImdbButton { get; set; }
    public bool ShowImdbRating { get; set; }
    public bool ShowJellyfinButton { get; set; }
    public bool ShowTimestamp { get; set; }
    public bool ShowEpisodeInfo { get; set; }
    public bool ShowYear { get; set; }
    public bool ShowMediaType { get; set; }
}
