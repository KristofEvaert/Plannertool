using System.Net.Http.Json;
using Microsoft.Extensions.Logging;
using TransportPlanner.Application.Interfaces;

namespace TransportPlanner.Infrastructure.Services;

public class GeocodingService : IGeocodingService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<GeocodingService> _logger;

    public GeocodingService(HttpClient httpClient, ILogger<GeocodingService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
        
        // Set user agent as required by Nominatim usage policy
        _httpClient.DefaultRequestHeaders.Add("User-Agent", "TransportPlanner/1.0");
    }

    public async Task<string?> GetAddressAsync(double latitude, double longitude, CancellationToken cancellationToken = default)
    {
        try
        {
            // Use OpenStreetMap Nominatim for reverse geocoding
            var url = $"https://nominatim.openstreetmap.org/reverse?format=json&lat={latitude}&lon={longitude}&zoom=18&addressdetails=1";
            
            var response = await _httpClient.GetAsync(url, cancellationToken);
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Geocoding failed with status {StatusCode} for coordinates {Lat}, {Lon}", 
                    response.StatusCode, latitude, longitude);
                return null;
            }

            var result = await response.Content.ReadFromJsonAsync<NominatimResponse>(cancellationToken: cancellationToken);
            
            if (result?.Address == null)
            {
                return null;
            }

            // Build address string from components
            var addressParts = new List<string>();
            
            if (!string.IsNullOrEmpty(result.Address.Road))
            {
                addressParts.Add(result.Address.Road);
            }
            
            if (!string.IsNullOrEmpty(result.Address.HouseNumber))
            {
                addressParts[addressParts.Count - 1] = $"{result.Address.HouseNumber} {addressParts.Last()}";
            }
            
            if (!string.IsNullOrEmpty(result.Address.Postcode))
            {
                addressParts.Add(result.Address.Postcode);
            }
            
            if (!string.IsNullOrEmpty(result.Address.City))
            {
                addressParts.Add(result.Address.City);
            }
            else if (!string.IsNullOrEmpty(result.Address.Town))
            {
                addressParts.Add(result.Address.Town);
            }
            else if (!string.IsNullOrEmpty(result.Address.Village))
            {
                addressParts.Add(result.Address.Village);
            }

            return addressParts.Count > 0 ? string.Join(", ", addressParts) : result.DisplayName;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting address for coordinates {Lat}, {Lon}", latitude, longitude);
            return null;
        }
    }

    private class NominatimResponse
    {
        public string? DisplayName { get; set; }
        public AddressDetails? Address { get; set; }
    }

    private class AddressDetails
    {
        [System.Text.Json.Serialization.JsonPropertyName("road")]
        public string? Road { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("house_number")]
        public string? HouseNumber { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("postcode")]
        public string? Postcode { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("city")]
        public string? City { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("town")]
        public string? Town { get; set; }
        
        [System.Text.Json.Serialization.JsonPropertyName("village")]
        public string? Village { get; set; }
    }
}

