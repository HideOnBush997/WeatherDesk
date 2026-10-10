using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Rules;

namespace WeatherDesk.Tests.DomainTests;

public sealed class WeatherCodeCatalogTests
{
    [Theory]
    [InlineData(0, "晴", WeatherConditionGroup.Clear, "sun")]
    [InlineData(1, "大部晴朗", WeatherConditionGroup.PartlyCloudy, "partly-cloudy")]
    [InlineData(2, "局部多云", WeatherConditionGroup.PartlyCloudy, "partly-cloudy")]
    [InlineData(3, "阴", WeatherConditionGroup.Cloudy, "cloud")]
    [InlineData(45, "雾", WeatherConditionGroup.Fog, "fog")]
    [InlineData(48, "雾凇", WeatherConditionGroup.Fog, "fog")]
    [InlineData(51, "小毛毛雨", WeatherConditionGroup.Drizzle, "drizzle")]
    [InlineData(53, "中等毛毛雨", WeatherConditionGroup.Drizzle, "drizzle")]
    [InlineData(55, "强毛毛雨", WeatherConditionGroup.Drizzle, "drizzle")]
    [InlineData(56, "冻毛毛雨", WeatherConditionGroup.Drizzle, "drizzle")]
    [InlineData(57, "强冻毛毛雨", WeatherConditionGroup.Drizzle, "drizzle")]
    [InlineData(61, "小雨", WeatherConditionGroup.Rain, "rain")]
    [InlineData(63, "中雨", WeatherConditionGroup.Rain, "rain")]
    [InlineData(65, "大雨", WeatherConditionGroup.Rain, "rain")]
    [InlineData(66, "冻雨", WeatherConditionGroup.Rain, "rain")]
    [InlineData(67, "强冻雨", WeatherConditionGroup.Rain, "rain")]
    [InlineData(71, "小雪", WeatherConditionGroup.Snow, "snow")]
    [InlineData(73, "中雪", WeatherConditionGroup.Snow, "snow")]
    [InlineData(75, "大雪", WeatherConditionGroup.Snow, "snow")]
    [InlineData(77, "米雪", WeatherConditionGroup.Snow, "snow")]
    [InlineData(80, "小阵雨", WeatherConditionGroup.Rain, "rain")]
    [InlineData(81, "中等阵雨", WeatherConditionGroup.Rain, "rain")]
    [InlineData(82, "强阵雨", WeatherConditionGroup.Rain, "rain")]
    [InlineData(85, "小阵雪", WeatherConditionGroup.Snow, "snow")]
    [InlineData(86, "强阵雪", WeatherConditionGroup.Snow, "snow")]
    [InlineData(95, "雷雨", WeatherConditionGroup.Thunderstorm, "thunderstorm")]
    [InlineData(96, "雷雨伴轻冰雹", WeatherConditionGroup.Thunderstorm, "thunderstorm")]
    [InlineData(99, "雷雨伴强冰雹", WeatherConditionGroup.Thunderstorm, "thunderstorm")]
    public void Known_open_meteo_codes_have_description_group_and_icon(
        int code,
        string description,
        WeatherConditionGroup group,
        string iconKey)
    {
        Assert.Equal(description, WeatherCodeCatalog.Describe(code));
        Assert.Equal(group, WeatherCodeCatalog.Group(code));
        Assert.Equal(iconKey, WeatherCodeCatalog.IconKey(code));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(4)]
    [InlineData(100)]
    public void Unknown_codes_fall_back_without_throwing(int code)
    {
        Assert.Equal("未知天气", WeatherCodeCatalog.Describe(code));
        Assert.Equal(WeatherConditionGroup.Unknown, WeatherCodeCatalog.Group(code));
        Assert.Equal("unknown", WeatherCodeCatalog.IconKey(code));
    }
}
