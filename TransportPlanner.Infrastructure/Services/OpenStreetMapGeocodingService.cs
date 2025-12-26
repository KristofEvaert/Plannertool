using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using TransportPlanner.Infrastructure.Options;

namespace TransportPlanner.Infrastructure.Services;

public class OpenStreetMapGeocodingService : IGeocodingService
{
    private readonly HttpClient _httpClient;
    private readonly OpenStreetMapOptions _options;

    public OpenStreetMapGeocodingService(HttpClient httpClient, IOptions<OpenStreetMapOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
        var userAgent = _options.UserAgent?.Trim();
        if (!string.IsNullOrWhiteSpace(userAgent))
        {
            _httpClient.DefaultRequestHeaders.UserAgent.Clear();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd(userAgent);
        }
    }

    public async Task<GeocodeResult?> GeocodeAddressAsync(string address, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(address))
        {
            return null;
        }

        var url = $"search?format=jsonv2&limit=1&q={Uri.EscapeDataString(address)}";
        if (!string.IsNullOrWhiteSpace(_options.Email))
        {
            url += $"&email={Uri.EscapeDataString(_options.Email)}";
        }
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        var results = await response.Content.ReadFromJsonAsync<List<OsmSearchResult>>(cancellationToken: cancellationToken);
        var match = results?.FirstOrDefault();
        if (match == null)
        {
            return null;
        }

        if (!double.TryParse(match.Lat, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lat)
            || !double.TryParse(match.Lon, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var lon))
        {
            return null;
        }

        return new GeocodeResult(lat, lon);
    }

    public async Task<string?> ReverseGeocodeAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        var lat = latitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var lon = longitude.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var url = $"reverse?format=jsonv2&zoom=18&addressdetails=0&lat={lat}&lon={lon}";
        if (!string.IsNullOrWhiteSpace(_options.Email))
        {
            url += $"&email={Uri.EscapeDataString(_options.Email)}";
        }
        using var response = await _httpClient.GetAsync(url, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            return null;
        }
        var result = await response.Content.ReadFromJsonAsync<OsmReverseResult>(cancellationToken: cancellationToken);
        return string.IsNullOrWhiteSpace(result?.DisplayName) ? null : result.DisplayName;
    }

    private sealed class OsmSearchResult
    {
        [JsonPropertyName("lat")]
        public string Lat { get; set; } = string.Empty;

        [JsonPropertyName("lon")]
        public string Lon { get; set; } = string.Empty;
    }

    private sealed class OsmReverseResult
    {
        [JsonPropertyName("display_name")]
        public string? DisplayName { get; set; }
    }
}
