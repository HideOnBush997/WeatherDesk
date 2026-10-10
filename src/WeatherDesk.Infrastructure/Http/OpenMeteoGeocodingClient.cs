using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;

namespace WeatherDesk.Infrastructure.Http;

public sealed class OpenMeteoGeocodingClient(
    HttpClient http,
    ILogger<OpenMeteoGeocodingClient> logger) : IGeocodingService
{
    private const string Endpoint = "https://geocoding-api.open-meteo.com/v1/search";

    public async Task<Result<IReadOnlyList<Location>>> SearchAsync(
        string keyword,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(keyword))
        {
            return Result<IReadOnlyList<Location>>.Fail(Failure.Input("请输入城市名称。"));
        }

        var uri = $"{Endpoint}?name={Uri.EscapeDataString(keyword.Trim())}&count=100&language=zh&format=json";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(OpenMeteoDefaults.RequestTimeout);

        try
        {
            using var response = await http.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (response.StatusCode == HttpStatusCode.RequestTimeout
                || (int)response.StatusCode == 429
                || (int)response.StatusCode >= 500)
            {
                return Result<IReadOnlyList<Location>>.Fail(
                    HttpFailureMapper.FromStatusCode(response.StatusCode));
            }

            if (!response.IsSuccessStatusCode)
            {
                return Result<IReadOnlyList<Location>>.Fail(
                    HttpFailureMapper.FromStatusCode(response.StatusCode));
            }

            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            using var document = JsonDocument.Parse(json);
            var locations = ParseResults(document.RootElement);
            if (locations.Count == 0 && ContainsChinese(keyword))
            {
                var fallback = await SearchNominatimAsync(keyword, timeout.Token);
                if (fallback.Count > 0) return Result<IReadOnlyList<Location>>.Ok(fallback);
            }
            return Result<IReadOnlyList<Location>>.Ok(locations);
        }
        catch (JsonException)
        {
            return Result<IReadOnlyList<Location>>.Fail(
                Failure.DataFormat("城市服务返回了无法识别的数据。"));
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or HttpRequestException)
        {
            var failure = HttpFailureMapper.FromException(exception, ct);
            logger.LogWarning(
                "Open-Meteo geocoding request failed with {FailureKind}.",
                failure.Kind);
            return Result<IReadOnlyList<Location>>.Fail(failure);
        }
    }

    private static IReadOnlyList<Location> ParseResults(JsonElement root)
    {
        if (!TryGetProperty(root, "results", out var results)
            || results.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return Array.Empty<Location>();
        }

        if (results.ValueKind != JsonValueKind.Array)
        {
            throw new JsonException("The results field must be an array.");
        }

        var locations = new List<Location>();
        foreach (var item in results.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object
                || !TryReadId(item, "id", out var id)
                || !TryReadString(item, "name", out var name)
                || !TryReadDouble(item, "latitude", out var latitude)
                || !TryReadDouble(item, "longitude", out var longitude)
                || latitude is < -90 or > 90
                || longitude is < -180 or > 180)
            {
                continue;
            }

            var location = new Location(
                id,
                name,
                ReadOptionalString(item, "admin1"),
                ReadOptionalString(item, "country"),
                ReadOptionalString(item, "country_code"),
                latitude,
                longitude,
                ReadOptionalString(item, "timezone"),
                ReadOptionalInt32(item, "utc_offset_seconds"));
            locations.Add(location with { Admin2 = ReadOptionalString(item, "admin2") });
        }

        return locations.AsReadOnly();
    }

    private async Task<IReadOnlyList<Location>> SearchNominatimAsync(string keyword, CancellationToken ct)
    {
        var uri = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(keyword.Trim())}&format=jsonv2&addressdetails=1&limit=20&countrycodes=cn";
        using var request = new HttpRequestMessage(HttpMethod.Get, uri);
        request.Headers.UserAgent.ParseAdd("WeatherDesk/1.0 (weather desktop app)");
        using var response = await http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);
        if (!response.IsSuccessStatusCode) return Array.Empty<Location>();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        if (document.RootElement.ValueKind != JsonValueKind.Array) return Array.Empty<Location>();
        var locations = new List<Location>();
        foreach (var item in document.RootElement.EnumerateArray())
        {
            if (!TryReadString(item, "lat", out var latitudeText)
                || !TryReadString(item, "lon", out var longitudeText)
                || !double.TryParse(latitudeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var latitude)
                || !double.TryParse(longitudeText, NumberStyles.Float, CultureInfo.InvariantCulture, out var longitude)
                || !TryReadString(item, "name", out var name)) continue;
            var address = item.TryGetProperty("address", out var addressValue) ? addressValue : default;
            var province = ReadAddress(address, "state") ?? ReadAddress(address, "province");
            var countryCode = ReadAddress(address, "country_code") ?? "CN";
            if (!string.Equals(countryCode, "cn", StringComparison.OrdinalIgnoreCase)) continue;
            locations.Add(new Location($"nominatim-{latitude.ToString(CultureInfo.InvariantCulture)}-{longitude.ToString(CultureInfo.InvariantCulture)}", name, province, "中国", "CN", latitude, longitude, "Asia/Shanghai", 8 * 3600));
        }
        return locations.AsReadOnly();
    }

    private static bool ContainsChinese(string value) => value.Any(ch => ch is >= '\u4e00' and <= '\u9fff');

    private static string? ReadAddress(JsonElement address, string name) =>
        address.ValueKind == JsonValueKind.Object && address.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryReadId(JsonElement item, string name, out string id)
    {
        id = string.Empty;
        if (!TryGetProperty(item, name, out var value))
        {
            return false;
        }

        if (value.ValueKind == JsonValueKind.Number && value.TryGetInt64(out var numericId))
        {
            id = numericId.ToString(CultureInfo.InvariantCulture);
            return true;
        }

        if (value.ValueKind == JsonValueKind.String
            && !string.IsNullOrWhiteSpace(value.GetString()))
        {
            id = value.GetString()!;
            return true;
        }

        return false;
    }

    private static bool TryReadString(JsonElement item, string name, out string value)
    {
        value = string.Empty;
        if (!TryGetProperty(item, name, out var property)
            || property.ValueKind != JsonValueKind.String
            || string.IsNullOrWhiteSpace(property.GetString()))
        {
            return false;
        }

        value = property.GetString()!;
        return true;
    }

    private static bool TryReadDouble(JsonElement item, string name, out double value)
    {
        value = default;
        return TryGetProperty(item, name, out var property)
            && property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out value)
            && double.IsFinite(value);
    }

    private static string? ReadOptionalString(JsonElement item, string name) =>
        TryGetProperty(item, name, out var property)
        && property.ValueKind == JsonValueKind.String
            ? property.GetString()
            : null;

    private static int? ReadOptionalInt32(JsonElement item, string name) =>
        TryGetProperty(item, name, out var property)
        && property.ValueKind == JsonValueKind.Number
        && property.TryGetInt32(out var value)
            ? value
            : null;

    private static bool TryGetProperty(JsonElement item, string name, out JsonElement value)
    {
        if (item.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in item.EnumerateObject())
            {
                if (property.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
