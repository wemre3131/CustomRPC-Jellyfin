using System;
using System.Net.Http;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.KavasakiPresence.Services;

/// <summary>
/// Fetches IMDB data without an API key by scraping the IMDB suggest API.
/// </summary>
public class ImdbService
{
    private readonly ILogger<ImdbService> _logger;
    private readonly HttpClient _httpClient;

    /// <summary>
    /// Initializes a new instance of the <see cref="ImdbService"/> class.
    /// </summary>
    public ImdbService(ILogger<ImdbService> logger, IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _httpClient = httpClientFactory.CreateClient("KavasakiPresence");
        _httpClient.DefaultRequestHeaders.Add("User-Agent",
            "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 Chrome/120 Safari/537.36");
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
    }

    /// <summary>
    /// Searches IMDB for a title and returns the IMDB ID and rating.
    /// No API key required - uses IMDB's public suggestion endpoint.
    /// </summary>
    public async Task<ImdbResult?> SearchAsync(string title, int? year, CancellationToken ct = default)
    {
        try
        {
            // IMDB's public suggest API (no key needed)
            string query = Uri.EscapeDataString(title.ToLowerInvariant());
            string firstChar = query.Length > 0 ? query[0].ToString() : "a";
            string url = $"https://v3.sg.media-imdb.com/suggestion/{firstChar}/{query}.json";

            var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) return null;

            string json = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            var node = JsonNode.Parse(json);
            var results = node?["d"]?.AsArray();

            if (results is null || results.Count == 0) return null;

            // Find the best match by title and year
            foreach (var item in results)
            {
                string? itemTitle = item?["l"]?.GetValue<string>();
                string? imdbId = item?["id"]?.GetValue<string>();
                int? itemYear = item?["y"]?.GetValue<int?>();
                string? qType = item?["qid"]?.GetValue<string>(); // "movie", "tvSeries", etc.

                if (string.IsNullOrEmpty(imdbId) || !imdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
                    continue;

                bool titleMatch = string.Equals(itemTitle, title, StringComparison.OrdinalIgnoreCase);
                bool yearMatch = !year.HasValue || !itemYear.HasValue || Math.Abs(itemYear.Value - year.Value) <= 1;

                if (titleMatch && yearMatch)
                {
                    // Fetch rating from IMDB ratings endpoint
                    double? rating = await FetchRatingAsync(imdbId, ct).ConfigureAwait(false);
                    return new ImdbResult { ImdbId = imdbId, Rating = rating, Title = itemTitle };
                }
            }

            // Fallback: use first result with a tt ID
            foreach (var item in results)
            {
                string? imdbId = item?["id"]?.GetValue<string>();
                string? itemTitle = item?["l"]?.GetValue<string>();
                if (string.IsNullOrEmpty(imdbId) || !imdbId.StartsWith("tt", StringComparison.OrdinalIgnoreCase))
                    continue;

                double? rating = await FetchRatingAsync(imdbId, ct).ConfigureAwait(false);
                return new ImdbResult { ImdbId = imdbId, Rating = rating, Title = itemTitle };
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[KavasakiPresence] IMDB search failed for: {Title}", title);
        }

        return null;
    }

    /// <summary>
    /// Fetches IMDB rating using the public ratings endpoint.
    /// </summary>
    private async Task<double?> FetchRatingAsync(string imdbId, CancellationToken ct)
    {
        try
        {
            // IMDB public ratings JSON - no key needed
            string url = $"https://www.imdb.com/title/{imdbId}/ratings/?ref_=tt_ov_rt";
            string html = await _httpClient.GetStringAsync(url, ct).ConfigureAwait(false);

            // Extract rating from JSON-LD or meta tag
            int ratingIdx = html.IndexOf("\"ratingValue\":", StringComparison.OrdinalIgnoreCase);
            if (ratingIdx >= 0)
            {
                int start = ratingIdx + 14;
                int end = html.IndexOf(',', start);
                if (end < 0) end = html.IndexOf('}', start);
                if (end > start)
                {
                    string ratingStr = html.Substring(start, end - start).Trim().Trim('"');
                    if (double.TryParse(ratingStr, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double rating))
                    {
                        return rating;
                    }
                }
            }

            // Try the aggregateRating JSON-LD format
            int aggIdx = html.IndexOf("\"aggregateRating\"", StringComparison.OrdinalIgnoreCase);
            if (aggIdx >= 0)
            {
                int rvIdx = html.IndexOf("\"ratingValue\"", aggIdx, StringComparison.OrdinalIgnoreCase);
                if (rvIdx >= 0)
                {
                    int start = rvIdx + 13;
                    // skip : and whitespace
                    while (start < html.Length && (html[start] == ':' || html[start] == ' ')) start++;
                    int end = start;
                    while (end < html.Length && (char.IsDigit(html[end]) || html[end] == '.')) end++;
                    string ratingStr = html.Substring(start, end - start);
                    if (double.TryParse(ratingStr, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out double rating))
                    {
                        return rating;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "[KavasakiPresence] Could not fetch IMDB rating for {Id}", imdbId);
        }

        return null;
    }
}

/// <summary>
/// IMDB search result.
/// </summary>
public class ImdbResult
{
    public string? ImdbId { get; set; }
    public double? Rating { get; set; }
    public string? Title { get; set; }
}
