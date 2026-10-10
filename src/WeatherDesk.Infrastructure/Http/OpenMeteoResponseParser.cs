using System.Globalization;
using System.Text.Json;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;

namespace WeatherDesk.Infrastructure.Http;

public static class OpenMeteoResponseParser
{
    private const string DataFormatMessage = "天气服务返回了无法识别的数据。";

    public static Result<WeatherSnapshot> Parse(
        Location location,
        string json,
        DateTimeOffset retrievedAtUtc,
        DateOnly? date = null)
    {
        ArgumentNullException.ThrowIfNull(location);

        try
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object
                || !TryGetProperty(root, "current", out var currentElement)
                || currentElement.ValueKind != JsonValueKind.Object
                || !TryReadDouble(currentElement, "temperature_2m", out var temperature)
                || !TryReadDouble(currentElement, "apparent_temperature", out var apparentTemperature)
                || !TryReadDouble(currentElement, "relative_humidity_2m", out var humidity)
                || !TryReadInt32(currentElement, "is_day", out var isDay)
                || !TryReadInt32(currentElement, "weather_code", out var weatherCode)
                || !TryReadDouble(currentElement, "surface_pressure", out var pressure)
                || !TryReadDouble(currentElement, "wind_speed_10m", out var windSpeed))
            {
                return Result<WeatherSnapshot>.Fail(Failure.DataFormat(DataFormatMessage));
            }

            var utcOffsetSeconds = TryReadInt32(root, "utc_offset_seconds", out var responseOffset)
                ? responseOffset
                : location.UtcOffsetSeconds ?? 0;
            var utcOffset = GetValidOffset(utcOffsetSeconds);
            var resolvedLocation = location with
            {
                Timezone = ReadString(root, "timezone") ?? location.Timezone,
                UtcOffsetSeconds = (int)utcOffset.TotalSeconds
            };

            var currentTime = TryReadLocalDateTime(currentElement, "time", out var parsedCurrentTime)
                ? parsedCurrentTime
                : retrievedAtUtc.ToOffset(utcOffset).DateTime;
            var observedAtUtc = ToUtc(currentTime, utcOffset);
            var hourly = ParseHourly(root, utcOffset, date);
            var daily = ParseDaily(root, utcOffset);
            var visibilityKm = FindCurrentVisibility(root, currentTime);
            var windGust = TryReadDouble(currentElement, "wind_gusts_10m", out var parsedWindGust)
                ? parsedWindGust
                : 0d;

            var snapshot = new WeatherSnapshot(
                resolvedLocation,
                new CurrentWeather(
                    observedAtUtc,
                    temperature,
                    apparentTemperature,
                    weatherCode,
                    humidity,
                    windSpeed,
                    windGust,
                    pressure,
                    visibilityKm,
                    isDay != 0),
                hourly,
                daily,
                retrievedAtUtc,
                observedAtUtc,
                "Open-Meteo",
                QueryDate: date);

            return Result<WeatherSnapshot>.Ok(snapshot);
        }
        catch (JsonException)
        {
            return Result<WeatherSnapshot>.Fail(Failure.DataFormat(DataFormatMessage));
        }
    }

    private static IReadOnlyList<HourlyForecast> ParseHourly(
        JsonElement root,
        TimeSpan utcOffset,
        DateOnly? requestedDate)
    {
        if (!TryGetProperty(root, "hourly", out var hourly)
            || hourly.ValueKind != JsonValueKind.Object
            || !TryGetArray(hourly, "time", out var times)
            || !TryGetArray(hourly, "temperature_2m", out var temperatures)
            || !TryGetArray(hourly, "apparent_temperature", out var apparentTemperatures)
            || !TryGetArray(hourly, "weather_code", out var weatherCodes)
            || !TryGetArray(hourly, "precipitation", out var precipitation)
            || !TryGetArray(hourly, "precipitation_probability", out var precipitationProbability)
            || !TryGetArray(hourly, "wind_speed_10m", out var windSpeeds)
            || !TryGetArray(hourly, "relative_humidity_2m", out var humidities))
        {
            return Array.Empty<HourlyForecast>();
        }

        var length = new[]
        {
            times.Length,
            temperatures.Length,
            apparentTemperatures.Length,
            weatherCodes.Length,
            precipitation.Length,
            precipitationProbability.Length,
            windSpeeds.Length,
            humidities.Length
        }.Min();
        var forecasts = new List<HourlyForecast>(length);

        for (var index = 0; index < length; index++)
        {
            if (!TryParseLocalDateTime(times[index], out var localTime))
            {
                continue;
            }

            if (requestedDate is { } date
                && DateOnly.FromDateTime(localTime) != date)
            {
                continue;
            }

            forecasts.Add(new HourlyForecast(
                ToUtc(localTime, utcOffset),
                ReadDoubleOrDefault(temperatures[index]),
                ReadDoubleOrDefault(apparentTemperatures[index]),
                ReadInt32OrDefault(weatherCodes[index]),
                ReadDoubleOrDefault(precipitation[index]),
                ReadDoubleOrDefault(precipitationProbability[index]),
                ReadDoubleOrDefault(windSpeeds[index]),
                ReadDoubleOrDefault(humidities[index])));
        }

        return forecasts.AsReadOnly();
    }

    private static IReadOnlyList<DailyForecast> ParseDaily(JsonElement root, TimeSpan utcOffset)
    {
        if (!TryGetProperty(root, "daily", out var daily)
            || daily.ValueKind != JsonValueKind.Object
            || !TryGetArray(daily, "time", out var times)
            || !TryGetArray(daily, "weather_code", out var weatherCodes)
            || !TryGetArray(daily, "temperature_2m_min", out var minimums)
            || !TryGetArray(daily, "temperature_2m_max", out var maximums)
            || !TryGetArray(daily, "precipitation_sum", out var precipitation)
            || !TryGetArray(daily, "precipitation_probability_max", out var precipitationProbability)
            || !TryGetArray(daily, "wind_speed_10m_max", out var windSpeeds))
        {
            return Array.Empty<DailyForecast>();
        }

        TryGetArray(daily, "uv_index_max", out var uvIndexes);
        TryGetArray(daily, "sunrise", out var sunrises);
        TryGetArray(daily, "sunset", out var sunsets);
        var length = new[]
        {
            times.Length,
            weatherCodes.Length,
            minimums.Length,
            maximums.Length,
            precipitation.Length,
            precipitationProbability.Length,
            windSpeeds.Length
        }.Min();
        var forecasts = new List<DailyForecast>(length);

        for (var index = 0; index < length; index++)
        {
            if (!TryParseDate(times[index], out var date))
            {
                continue;
            }

            forecasts.Add(new DailyForecast(
                date,
                ReadDoubleOrDefault(minimums[index]),
                ReadDoubleOrDefault(maximums[index]),
                ReadInt32OrDefault(weatherCodes[index]),
                ReadDoubleOrDefault(precipitation[index]),
                ReadDoubleOrDefault(precipitationProbability[index]),
                ReadDoubleOrDefault(windSpeeds[index]),
                index < uvIndexes.Length ? ReadNullableDouble(uvIndexes[index]) : null,
                index < sunrises.Length ? ReadUtc(sunrises[index], utcOffset) : null,
                index < sunsets.Length ? ReadUtc(sunsets[index], utcOffset) : null));
        }

        return forecasts.AsReadOnly();
    }

    private static double? FindCurrentVisibility(JsonElement root, DateTime localCurrentTime)
    {
        if (!TryGetProperty(root, "hourly", out var hourly)
            || hourly.ValueKind != JsonValueKind.Object
            || !TryGetArray(hourly, "time", out var times)
            || !TryGetArray(hourly, "visibility", out var visibility))
        {
            return null;
        }

        var length = Math.Min(times.Length, visibility.Length);
        var bestDifference = long.MaxValue;
        double? bestVisibility = null;
        for (var index = 0; index < length; index++)
        {
            if (!TryParseLocalDateTime(times[index], out var localTime))
            {
                continue;
            }

            var difference = Math.Abs((localTime - localCurrentTime).Ticks);
            if (difference >= bestDifference || !TryReadDouble(visibility[index], out var meters))
            {
                continue;
            }

            bestDifference = difference;
            bestVisibility = meters / 1000d;
        }

        return bestVisibility;
    }

    private static DateTimeOffset? ReadUtc(JsonElement value, TimeSpan utcOffset) =>
        TryParseLocalDateTime(value, out var localTime)
            ? ToUtc(localTime, utcOffset)
            : null;

    private static DateTimeOffset ToUtc(DateTime localTime, TimeSpan utcOffset)
    {
        if (localTime.Kind == DateTimeKind.Utc)
        {
            return new DateTimeOffset(localTime, TimeSpan.Zero);
        }

        return new DateTimeOffset(
            DateTime.SpecifyKind(localTime, DateTimeKind.Unspecified),
            utcOffset).ToUniversalTime();
    }

    private static TimeSpan GetValidOffset(int seconds)
    {
        var offset = TimeSpan.FromSeconds(seconds);
        return offset >= TimeSpan.FromHours(-14) && offset <= TimeSpan.FromHours(14)
            ? offset
            : TimeSpan.Zero;
    }

    private static bool TryGetArray(JsonElement parent, string propertyName, out JsonElement[] values)
    {
        if (TryGetProperty(parent, propertyName, out var value) && value.ValueKind == JsonValueKind.Array)
        {
            values = value.EnumerateArray().ToArray();
            return true;
        }

        values = Array.Empty<JsonElement>();
        return false;
    }

    private static bool TryGetProperty(JsonElement parent, string propertyName, out JsonElement value)
    {
        if (parent.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in parent.EnumerateObject())
            {
                if (property.Name.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }

    private static bool TryReadDouble(JsonElement parent, string propertyName, out double value)
    {
        value = default;
        return TryGetProperty(parent, propertyName, out var property)
            && TryReadDouble(property, out value);
    }

    private static bool TryReadDouble(JsonElement property, out double value)
    {
        value = default;
        return property.ValueKind == JsonValueKind.Number
            && property.TryGetDouble(out value)
            && double.IsFinite(value);
    }

    private static bool TryReadInt32(JsonElement parent, string propertyName, out int value)
    {
        value = default;
        return TryGetProperty(parent, propertyName, out var property)
            && TryReadInt32(property, out value);
    }

    private static bool TryReadInt32(JsonElement property, out int value)
    {
        value = default;
        return property.ValueKind == JsonValueKind.Number && property.TryGetInt32(out value);
    }

    private static double ReadDoubleOrDefault(JsonElement value) =>
        TryReadDouble(value, out var number) ? number : 0d;

    private static int ReadInt32OrDefault(JsonElement value) =>
        TryReadInt32(value, out var number) ? number : 0;

    private static double? ReadNullableDouble(JsonElement value) =>
        TryReadDouble(value, out var number) ? number : null;

    private static string? ReadString(JsonElement parent, string propertyName) =>
        TryGetProperty(parent, propertyName, out var value)
        && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static bool TryReadLocalDateTime(JsonElement parent, string propertyName, out DateTime value)
    {
        value = default;
        return TryGetProperty(parent, propertyName, out var property)
            && TryParseLocalDateTime(property, out value);
    }

    private static bool TryParseLocalDateTime(JsonElement value, out DateTime localTime)
    {
        localTime = default;
        return value.ValueKind == JsonValueKind.String
            && DateTime.TryParse(
                value.GetString(),
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out localTime);
    }

    private static bool TryParseDate(JsonElement value, out DateOnly date)
    {
        date = default;
        return value.ValueKind == JsonValueKind.String
            && DateOnly.TryParseExact(
                value.GetString(),
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out date);
    }
}
