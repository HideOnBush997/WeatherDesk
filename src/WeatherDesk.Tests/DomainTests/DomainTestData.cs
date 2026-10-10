using WeatherDesk.Domain.Models;

namespace WeatherDesk.Tests.DomainTests;

internal static class DomainTestData
{
    public static Location CreateLocation(int utcOffsetSeconds = 0) =>
        new(
            "1",
            "Shanghai",
            null,
            "China",
            "CN",
            31.23,
            121.47,
            "Asia/Shanghai",
            utcOffsetSeconds);

    public static WeatherSnapshot CreateSnapshot(
        DateTimeOffset retrievedAtUtc,
        CurrentWeather? current = null,
        IReadOnlyList<HourlyForecast>? hourly = null,
        IReadOnlyList<DailyForecast>? daily = null) =>
        new(
            CreateLocation(),
            current,
            hourly ?? Array.Empty<HourlyForecast>(),
            daily ?? Array.Empty<DailyForecast>(),
            retrievedAtUtc,
            retrievedAtUtc,
            "Open-Meteo",
            1,
            WeatherProvenance.Live);
}
