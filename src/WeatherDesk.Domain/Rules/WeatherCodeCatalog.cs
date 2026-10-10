using WeatherDesk.Domain.Models;

namespace WeatherDesk.Domain.Rules;

public static class WeatherCodeCatalog
{
    public static string Describe(int code) => GetInfo(code).Description;

    public static WeatherConditionGroup Group(int code) => GetInfo(code).Group;

    public static string IconKey(int code) => GetInfo(code).IconKey;

    private static (string Description, WeatherConditionGroup Group, string IconKey) GetInfo(int code) =>
        code switch
        {
            0 => ("晴", WeatherConditionGroup.Clear, "sun"),
            1 => ("大部晴朗", WeatherConditionGroup.PartlyCloudy, "partly-cloudy"),
            2 => ("局部多云", WeatherConditionGroup.PartlyCloudy, "partly-cloudy"),
            3 => ("阴", WeatherConditionGroup.Cloudy, "cloud"),
            45 => ("雾", WeatherConditionGroup.Fog, "fog"),
            48 => ("雾凇", WeatherConditionGroup.Fog, "fog"),
            51 => ("小毛毛雨", WeatherConditionGroup.Drizzle, "drizzle"),
            53 => ("中等毛毛雨", WeatherConditionGroup.Drizzle, "drizzle"),
            55 => ("强毛毛雨", WeatherConditionGroup.Drizzle, "drizzle"),
            56 => ("冻毛毛雨", WeatherConditionGroup.Drizzle, "drizzle"),
            57 => ("强冻毛毛雨", WeatherConditionGroup.Drizzle, "drizzle"),
            61 => ("小雨", WeatherConditionGroup.Rain, "rain"),
            63 => ("中雨", WeatherConditionGroup.Rain, "rain"),
            65 => ("大雨", WeatherConditionGroup.Rain, "rain"),
            66 => ("冻雨", WeatherConditionGroup.Rain, "rain"),
            67 => ("强冻雨", WeatherConditionGroup.Rain, "rain"),
            71 => ("小雪", WeatherConditionGroup.Snow, "snow"),
            73 => ("中雪", WeatherConditionGroup.Snow, "snow"),
            75 => ("大雪", WeatherConditionGroup.Snow, "snow"),
            77 => ("米雪", WeatherConditionGroup.Snow, "snow"),
            80 => ("小阵雨", WeatherConditionGroup.Rain, "rain"),
            81 => ("中等阵雨", WeatherConditionGroup.Rain, "rain"),
            82 => ("强阵雨", WeatherConditionGroup.Rain, "rain"),
            85 => ("小阵雪", WeatherConditionGroup.Snow, "snow"),
            86 => ("强阵雪", WeatherConditionGroup.Snow, "snow"),
            95 => ("雷雨", WeatherConditionGroup.Thunderstorm, "thunderstorm"),
            96 => ("雷雨伴轻冰雹", WeatherConditionGroup.Thunderstorm, "thunderstorm"),
            99 => ("雷雨伴强冰雹", WeatherConditionGroup.Thunderstorm, "thunderstorm"),
            _ => ("未知天气", WeatherConditionGroup.Unknown, "unknown")
        };
}
