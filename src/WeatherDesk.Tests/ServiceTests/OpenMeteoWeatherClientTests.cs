using System.Net;
using System.Text;
using Microsoft.Extensions.Logging.Abstractions;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;
using WeatherDesk.Infrastructure.Http;

namespace WeatherDesk.Tests.ServiceTests;

public sealed class OpenMeteoWeatherClientTests
{
    [Fact]
    public async Task GetForecast_requests_expected_fields_and_maps_the_response()
    {
        Uri? requestedUri = null;
        var handler = new StubHttpMessageHandler((request, _) =>
        {
            requestedUri = request.RequestUri;
            return Task.FromResult(JsonResponse(WeatherJson));
        });
        var client = CreateClient(handler);
        var location = Shanghai;
        var retrievedAt = new DateTimeOffset(2026, 10, 2, 4, 0, 0, TimeSpan.Zero);

        var result = await client.GetForecastAsync(location, new DateOnly(2026, 10, 2), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var snapshot = result.Value!;
        Assert.Equal("Shanghai", snapshot.Location.Name);
        Assert.Equal(new DateOnly(2026, 10, 2), snapshot.QueryDate);
        Assert.Equal(28800, snapshot.Location.UtcOffsetSeconds);
        Assert.Equal("Asia/Shanghai", snapshot.Location.Timezone);
        Assert.Equal(18.5, snapshot.Current!.TemperatureC);
        Assert.Equal(1.2, snapshot.Current.VisibilityKm);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 4, 0, 0, TimeSpan.Zero), snapshot.DataUpdatedAtUtc);
        Assert.Equal(new DateOnly(2026, 10, 2), DateOnly.FromDateTime(
            snapshot.Hourly[0].HourUtc.ToOffset(TimeSpan.FromHours(8)).DateTime));
        Assert.NotNull(requestedUri);
        Assert.Equal("api.open-meteo.com", requestedUri!.Host);
        Assert.Equal("/v1/forecast", requestedUri.AbsolutePath);
        Assert.Equal("31.22", Query(requestedUri, "latitude"));
        Assert.Equal("121.46", Query(requestedUri, "longitude"));
        Assert.Contains("temperature_2m", Query(requestedUri, "current"));
        Assert.Contains("visibility", Query(requestedUri, "hourly"));
        Assert.Contains("uv_index_max", Query(requestedUri, "daily"));
        Assert.Equal("auto", Query(requestedUri, "timezone"));
        Assert.Equal("7", Query(requestedUri, "forecast_days"));
        Assert.Equal(1, handler.RequestCount);
    }

    [Fact]
    public async Task GetForecast_preserves_seven_daily_rows_and_clips_hourly_rows_to_requested_date()
    {
        var client = CreateClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse(WeatherJson))));

        var result = await client.GetForecastAsync(Shanghai, new DateOnly(2026, 10, 3), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(7, result.Value!.Daily.Count);
        Assert.Equal(24, result.Value.Hourly.Count);
        Assert.All(result.Value.Hourly, row =>
            Assert.Equal(new DateOnly(2026, 10, 3), DateOnly.FromDateTime(row.HourUtc.ToOffset(TimeSpan.FromHours(8)).DateTime)));
    }

    [Fact]
    public async Task GetForecast_maps_http_errors_to_upstream_failures()
    {
        var client = CreateClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.ServiceUnavailable))));

        var result = await client.GetForecastAsync(Shanghai, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Upstream, result.Failure!.Kind);
        Assert.True(result.Failure.Retryable);
        Assert.DoesNotContain("Shanghai", result.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetForecast_maps_timeout_and_caller_cancellation_separately()
    {
        var timeoutHandler = new StubHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var timeoutClient = new HttpClient(timeoutHandler) { Timeout = TimeSpan.FromMilliseconds(30) };
        var timeoutResult = await new OpenMeteoWeatherClient(
            timeoutClient,
            new FakeClock(RetrievedAt),
            NullLogger<OpenMeteoWeatherClient>.Instance)
            .GetForecastAsync(Shanghai, null, CancellationToken.None);

        using var cancellation = new CancellationTokenSource(TimeSpan.FromMilliseconds(30));
        var cancelHandler = new StubHttpMessageHandler(async (_, token) =>
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });
        using var callerClient = new HttpClient(cancelHandler) { Timeout = Timeout.InfiniteTimeSpan };
        var canceledResult = await new OpenMeteoWeatherClient(
            callerClient,
            new FakeClock(RetrievedAt),
            NullLogger<OpenMeteoWeatherClient>.Instance)
            .GetForecastAsync(Shanghai, null, cancellation.Token);

        Assert.Equal(FailureKind.Timeout, timeoutResult.Failure!.Kind);
        Assert.Equal(FailureKind.Canceled, canceledResult.Failure!.Kind);
    }

    [Fact]
    public async Task GetForecast_maps_malformed_json_and_response_read_errors()
    {
        var malformed = CreateClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(JsonResponse("{ malformed"))));
        var malformedResult = await malformed.GetForecastAsync(Shanghai, null, CancellationToken.None);

        var readFailure = CreateClient(new StubHttpMessageHandler((_, _) =>
            Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new FailingReadStreamContent()
            })));
        var readFailureResult = await readFailure.GetForecastAsync(Shanghai, null, CancellationToken.None);

        Assert.Equal(FailureKind.DataFormat, malformedResult.Failure!.Kind);
        Assert.Equal(FailureKind.Network, readFailureResult.Failure!.Kind);
    }

    [Fact]
    public void Parser_truncates_misaligned_arrays_to_the_shortest_length()
    {
        var json = """
            {
              "utc_offset_seconds": 0,
              "current": {
                "time": "2026-10-02T12:00",
                "temperature_2m": 10,
                "apparent_temperature": 9,
                "relative_humidity_2m": 50,
                "is_day": 1,
                "weather_code": 0,
                "surface_pressure": 1000,
                "wind_speed_10m": 5,
                "wind_gusts_10m": 7
              },
              "hourly": {
                "time": ["2026-10-02T00:00", "2026-10-02T01:00"],
                "temperature_2m": [1, 2, 3],
                "apparent_temperature": [1],
                "weather_code": [0, 1],
                "precipitation": [0, 0],
                "precipitation_probability": [0, 0],
                "wind_speed_10m": [1, 2],
                "relative_humidity_2m": [50, 60]
              },
              "daily": { "time": ["2026-10-02", "2026-10-03"] }
            }
            """;

        var result = OpenMeteoResponseParser.Parse(Shanghai, json, RetrievedAt);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value!.Hourly);
        Assert.Empty(result.Value.Daily);
    }

    [Fact]
    public void Parser_requires_current_data_and_uses_retrieval_time_when_observation_time_is_missing()
    {
        var noCurrent = OpenMeteoResponseParser.Parse(Shanghai, "{}", RetrievedAt);
        var noCurrentTime = OpenMeteoResponseParser.Parse(Shanghai, WeatherJson.Replace(
            "\"time\": \"2026-10-02T12:00\",", "", StringComparison.Ordinal), RetrievedAt);

        Assert.Equal(FailureKind.DataFormat, noCurrent.Failure!.Kind);
        Assert.True(noCurrentTime.IsSuccess);
        Assert.Equal(RetrievedAt, noCurrentTime.Value!.DataUpdatedAtUtc);
    }

    [Fact]
    public void Parser_treats_missing_visibility_as_null_and_converts_local_observation_time_to_utc()
    {
        var visibilityArray = string.Join(',', Enumerable.Repeat("1200", 168));
        var json = WeatherJson.Replace(
            $"\"visibility\": [{visibilityArray}]",
            "\"visibility\": [null]",
            StringComparison.Ordinal);

        var result = OpenMeteoResponseParser.Parse(Shanghai, json, RetrievedAt);

        Assert.True(result.IsSuccess);
        Assert.Equal(new DateTimeOffset(2026, 10, 2, 4, 0, 0, TimeSpan.Zero), result.Value!.Current!.ObservedAtUtc);
        Assert.Equal(result.Value.Current.ObservedAtUtc, result.Value.DataUpdatedAtUtc);
        Assert.Equal(19.4, result.Value.Current.WindGustKph);
        Assert.Null(result.Value.Current.VisibilityKm);
    }

    private static readonly DateTimeOffset RetrievedAt = new(2026, 10, 2, 4, 10, 0, TimeSpan.Zero);

    private static readonly Location Shanghai = new(
        "1796236", "Shanghai", "Shanghai", "China", "CN", 31.22, 121.46, "Asia/Shanghai", 28800);

    private static readonly string WeatherJson = BuildWeatherJson();

    private static OpenMeteoWeatherClient CreateClient(HttpMessageHandler handler) =>
        new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            new FakeClock(RetrievedAt),
            NullLogger<OpenMeteoWeatherClient>.Instance);

    private static string BuildWeatherJson()
    {
        var hourlyTime = Enumerable.Range(0, 168)
            .Select(hour => $"2026-10-{2 + (hour / 24):00}T{hour % 24:00}:00")
            .ToArray();
        var hourlyNumbers = string.Join(',', Enumerable.Range(0, 168));
        var visibilityNumbers = string.Join(',', Enumerable.Repeat("1200", 168));
        var dailyTime = string.Join(',', Enumerable.Range(2, 7).Select(day => $"\"2026-10-{day:00}\""));
        var values = string.Join(',', Enumerable.Range(0, 7).Select(value => value + 10));

        return $$"""
            {
              "latitude": 31.22,
              "longitude": 121.46,
              "timezone": "Asia/Shanghai",
              "utc_offset_seconds": 28800,
              "current": {
                "time": "2026-10-02T12:00",
                "temperature_2m": 18.5,
                "apparent_temperature": 17.0,
                "relative_humidity_2m": 70,
                "is_day": 1,
                "weather_code": 3,
                "surface_pressure": 1012.5,
                "wind_speed_10m": 12.3,
                "wind_gusts_10m": 19.4
              },
              "hourly": {
                "time": [{{string.Join(',', hourlyTime.Select(value => $"\"{value}\""))}}],
                "temperature_2m": [{{hourlyNumbers}}],
                "apparent_temperature": [{{hourlyNumbers}}],
                "weather_code": [{{hourlyNumbers}}],
                "precipitation": [{{hourlyNumbers}}],
                "precipitation_probability": [{{hourlyNumbers}}],
                "wind_speed_10m": [{{hourlyNumbers}}],
                "relative_humidity_2m": [{{hourlyNumbers}}],
                "visibility": [{{visibilityNumbers}}]
              },
              "daily": {
                "time": [{{dailyTime}}],
                "weather_code": [{{values}}],
                "temperature_2m_min": [{{values}}],
                "temperature_2m_max": [{{values}}],
                "precipitation_sum": [{{values}}],
                "precipitation_probability_max": [{{values}}],
                "wind_speed_10m_max": [{{values}}],
                "uv_index_max": [{{values}}],
                "sunrise": [{{string.Join(',', Enumerable.Range(2, 7).Select(day => $"\"2026-10-{day:00}T06:00\""))}}],
                "sunset": [{{string.Join(',', Enumerable.Range(2, 7).Select(day => $"\"2026-10-{day:00}T18:00\""))}}]
              }
            }
            """;
    }

    private static HttpResponseMessage JsonResponse(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static string? Query(Uri uri, string name) => uri.Query.TrimStart('?')
        .Split('&', StringSplitOptions.RemoveEmptyEntries)
        .Select(part => part.Split('=', 2))
        .Where(pair => pair.Length == 2)
        .Where(pair => Uri.UnescapeDataString(pair[0]) == name)
        .Select(pair => Uri.UnescapeDataString(pair[1].Replace('+', ' ')))
        .FirstOrDefault();

    private sealed class StubHttpMessageHandler(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> responder)
        : HttpMessageHandler
    {
        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return responder(request, cancellationToken);
        }
    }

    private sealed class FakeClock(DateTimeOffset utcNow) : WeatherDesk.Domain.Interfaces.IClock
    {
        public DateTimeOffset UtcNow => utcNow;
        public DateOnly TodayIn(TimeSpan utcOffset) => DateOnly.FromDateTime(utcNow.ToOffset(utcOffset).DateTime);
        public DateTimeOffset NowIn(TimeSpan utcOffset) => utcNow.ToOffset(utcOffset);
    }

    private sealed class FailingReadStreamContent : HttpContent
    {
        protected override Task SerializeToStreamAsync(Stream stream, System.Net.TransportContext? context) =>
            Task.FromException(new IOException("Simulated response stream failure."));

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
