using WeatherDesk.Domain.Models;

namespace WeatherDesk.Domain.Rules;

public static class AdviceRuleSet
{
    private const double HighPrecipitationProbability = 40;
    private const double StrongWindKph = 30;
    private const double ColdTemperatureCelsius = 5;

    public static IReadOnlyList<string> Build(
        WeatherSnapshot snapshot,
        DateOnly? date,
        AgentIntent intent)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        var offset = TimeSpan.FromSeconds(snapshot.Location.UtcOffsetSeconds ?? 0);
        var currentDate = snapshot.Current is { } current
            ? LocalDate(current.ObservedAtUtc, offset)
            : LocalDate(snapshot.DataUpdatedAtUtc, offset);
        var targetDate = date ?? currentDate;
        var daily = snapshot.Daily.FirstOrDefault(forecast => forecast.Date == targetDate);
        var hourly = snapshot.Hourly
            .Where(forecast => LocalDate(forecast.HourUtc, offset) == targetDate)
            .ToArray();

        var precipitationProbability = daily?.PrecipitationProbabilityMaxPercent
            ?? MaxOrNull(hourly.Select(forecast => (double?)forecast.PrecipitationProbabilityPercent));
        var windSpeed = daily?.WindSpeedMaxKph
            ?? MaxOrNull(hourly.Select(forecast => (double?)forecast.WindSpeedKph));
        var lowTemperature = daily?.MinTemperatureC
            ?? MinOrNull(hourly.Select(forecast => (double?)forecast.TemperatureC));
        var weatherCode = daily?.WeatherCode
            ?? hourly.FirstOrDefault()?.WeatherCode;

        if (date is null && snapshot.Current is { } currentWeather)
        {
            windSpeed = Math.Max(windSpeed ?? currentWeather.WindSpeedKph, currentWeather.WindSpeedKph);
            lowTemperature = Math.Min(lowTemperature ?? currentWeather.TemperatureC, currentWeather.TemperatureC);
            weatherCode ??= currentWeather.WeatherCode;
        }

        var group = weatherCode is { } code ? WeatherCodeCatalog.Group(code) : WeatherConditionGroup.Unknown;
        var advice = new List<string>();

        if ((precipitationProbability ?? 0) >= HighPrecipitationProbability
            || group is WeatherConditionGroup.Drizzle
                or WeatherConditionGroup.Rain
                or WeatherConditionGroup.Thunderstorm)
        {
            advice.Add("降水概率较高，建议带伞。");
        }

        if ((windSpeed ?? 0) >= StrongWindKph)
        {
            advice.Add("风速较大，出行注意防风。");
        }

        if ((lowTemperature ?? double.PositiveInfinity) <= ColdTemperatureCelsius)
        {
            advice.Add("气温较低，建议注意保暖。");
        }

        if (intent == AgentIntent.Running
            && (group == WeatherConditionGroup.Thunderstorm || (windSpeed ?? 0) >= 50))
        {
            advice.Add("天气条件较差，建议暂缓户外跑步。");
        }
        else if (intent == AgentIntent.Running
                 && (group is WeatherConditionGroup.Drizzle
                     or WeatherConditionGroup.Rain
                     or WeatherConditionGroup.Snow
                     or WeatherConditionGroup.Thunderstorm
                     || (precipitationProbability ?? 0) >= HighPrecipitationProbability))
        {
            advice.Add("降水可能影响户外跑步，建议选择室内运动。");
        }

        return advice.AsReadOnly();
    }

    private static DateOnly LocalDate(DateTimeOffset utcTimestamp, TimeSpan offset) =>
        DateOnly.FromDateTime(utcTimestamp.UtcDateTime.Add(offset));

    private static double? MaxOrNull(IEnumerable<double?> values)
    {
        var materialized = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return materialized.Length == 0 ? null : materialized.Max();
    }

    private static double? MinOrNull(IEnumerable<double?> values)
    {
        var materialized = values.Where(value => value.HasValue).Select(value => value!.Value).ToArray();
        return materialized.Length == 0 ? null : materialized.Min();
    }
}
