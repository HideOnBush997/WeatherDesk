namespace WeatherDesk.Domain.Models;

public enum WeatherConditionGroup
{
    Clear,
    PartlyCloudy,
    Cloudy,
    Fog,
    Drizzle,
    Rain,
    Snow,
    Thunderstorm,
    Unknown
}

public sealed record CurrentWeather(
    DateTimeOffset ObservedAtUtc,
    double TemperatureC,
    double ApparentTemperatureC,
    int WeatherCode,
    double HumidityPercent,
    double WindSpeedKph,
    double WindGustKph,
    double PressureHpa,
    double? VisibilityKm,
    bool IsDay);

public sealed record HourlyForecast(
    DateTimeOffset HourUtc,
    double TemperatureC,
    double ApparentTemperatureC,
    int WeatherCode,
    double PrecipitationMm,
    double PrecipitationProbabilityPercent,
    double WindSpeedKph,
    double HumidityPercent);

public sealed record DailyForecast(
    DateOnly Date,
    double MinTemperatureC,
    double MaxTemperatureC,
    int WeatherCode,
    double PrecipitationSumMm,
    double PrecipitationProbabilityMaxPercent,
    double WindSpeedMaxKph,
    double? UvIndexMax,
    DateTimeOffset? SunriseUtc,
    DateTimeOffset? SunsetUtc);

public sealed record WeatherSnapshot(
    Location Location,
    CurrentWeather? Current,
    IReadOnlyList<HourlyForecast> Hourly,
    IReadOnlyList<DailyForecast> Daily,
    DateTimeOffset RetrievedAtUtc,
    DateTimeOffset DataUpdatedAtUtc,
    string SourceName,
    int DataVersion = 1,
    WeatherProvenance Provenance = WeatherProvenance.Live,
    DateOnly? QueryDate = null);

public enum WeatherProvenance
{
    Live,
    Cache
}
