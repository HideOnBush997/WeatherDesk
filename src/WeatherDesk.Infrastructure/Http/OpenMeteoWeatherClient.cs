using System.Globalization;
using System.Net;
using Microsoft.Extensions.Logging;
using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;

namespace WeatherDesk.Infrastructure.Http;

public sealed class OpenMeteoWeatherClient : IWeatherService
{
    private const string Endpoint = "https://api.open-meteo.com/v1/forecast";
    private const string CurrentFields =
        "temperature_2m,apparent_temperature,relative_humidity_2m,is_day,weather_code,surface_pressure,wind_speed_10m,wind_gusts_10m";
    private const string HourlyFields =
        "temperature_2m,apparent_temperature,weather_code,precipitation,precipitation_probability,wind_speed_10m,relative_humidity_2m,visibility";
    private const string DailyFields =
        "weather_code,temperature_2m_min,temperature_2m_max,precipitation_sum,precipitation_probability_max,wind_speed_10m_max,uv_index_max,sunrise,sunset";

    private readonly HttpClient _http;
    private readonly IClock _clock;
    private readonly ILogger<OpenMeteoWeatherClient> _logger;
    private readonly TimeSpan _requestTimeout;

    public OpenMeteoWeatherClient(
        HttpClient http,
        IClock clock,
        ILogger<OpenMeteoWeatherClient> logger,
        TimeSpan? requestTimeout = null)
    {
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _requestTimeout = requestTimeout ?? OpenMeteoDefaults.RequestTimeout;
    }

    public async Task<Result<WeatherSnapshot>> GetForecastAsync(
        Location location,
        DateOnly? date,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(location);
        if (!double.IsFinite(location.Latitude)
            || location.Latitude is < -90 or > 90
            || !double.IsFinite(location.Longitude)
            || location.Longitude is < -180 or > 180)
        {
            return Result<WeatherSnapshot>.Fail(Failure.Input("城市坐标无效。"));
        }

        var requestUri = BuildRequestUri(location);
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(_requestTimeout);

        try
        {
            using var response = await _http.GetAsync(
                requestUri,
                HttpCompletionOption.ResponseHeadersRead,
                timeout.Token);
            if (!response.IsSuccessStatusCode)
            {
                return Result<WeatherSnapshot>.Fail(HttpFailureMapper.FromStatusCode(response.StatusCode));
            }

            var json = await response.Content.ReadAsStringAsync(timeout.Token);
            var result = OpenMeteoResponseParser.Parse(location, json, _clock.UtcNow, date);
            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "Open-Meteo forecast response could not be parsed: {FailureKind}.",
                    result.Failure!.Kind);
            }

            return result;
        }
        catch (Exception exception) when (
            exception is OperationCanceledException or HttpRequestException or IOException)
        {
            var failure = HttpFailureMapper.FromException(exception, ct);
            _logger.LogWarning(
                "Open-Meteo forecast request failed with {FailureKind}.",
                failure.Kind);
            return Result<WeatherSnapshot>.Fail(failure);
        }
    }

    private static string BuildRequestUri(Location location)
    {
        var values = new (string Name, string Value)[]
        {
            ("latitude", location.Latitude.ToString("R", CultureInfo.InvariantCulture)),
            ("longitude", location.Longitude.ToString("R", CultureInfo.InvariantCulture)),
            ("current", CurrentFields),
            ("hourly", HourlyFields),
            ("daily", DailyFields),
            ("timezone", "auto"),
            ("forecast_days", "7")
        };

        var query = string.Join("&", values.Select(pair =>
            $"{Uri.EscapeDataString(pair.Name)}={Uri.EscapeDataString(pair.Value)}"));
        return $"{Endpoint}?{query}";
    }
}
