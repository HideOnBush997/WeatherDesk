using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Rules;

namespace WeatherDesk.Tests.DomainTests;

public sealed class AdviceRuleSetTests
{
    private static readonly DateOnly TargetDate = new(2026, 10, 3);
    private static readonly DateTimeOffset RetrievedAt = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Warns_about_rain_wind_and_cold_for_the_selected_day()
    {
        var snapshot = DomainTestData.CreateSnapshot(
            RetrievedAt,
            daily:
            [
                new DailyForecast(
                    TargetDate,
                    -5,
                    2,
                    61,
                    8,
                    80,
                    40,
                    1,
                    null,
                    null)
            ]);

        var advice = AdviceRuleSet.Build(snapshot, TargetDate, AgentIntent.Running);

        Assert.Contains(advice, item => item.Contains("带伞", StringComparison.Ordinal));
        Assert.Contains(advice, item => item.Contains("风", StringComparison.Ordinal));
        Assert.Contains(advice, item => item.Contains("保暖", StringComparison.Ordinal));
    }

    [Fact]
    public void Uses_current_weather_when_no_forecast_is_available_for_today()
    {
        var snapshot = DomainTestData.CreateSnapshot(
            RetrievedAt,
            current: new CurrentWeather(
                RetrievedAt,
                -5,
                -8,
                3,
                60,
                40,
                55,
                1000,
                null,
                false));

        var advice = AdviceRuleSet.Build(snapshot, null, AgentIntent.Commute);

        Assert.Contains(advice, item => item.Contains("风", StringComparison.Ordinal));
        Assert.Contains(advice, item => item.Contains("保暖", StringComparison.Ordinal));
    }

    [Fact]
    public void Does_not_apply_weather_advice_from_a_different_day()
    {
        var otherDate = TargetDate.AddDays(1);
        var snapshot = DomainTestData.CreateSnapshot(
            RetrievedAt,
            daily:
            [
                new DailyForecast(
                    otherDate,
                    -10,
                    -2,
                    95,
                    20,
                    100,
                    60,
                    1,
                    null,
                    null)
            ]);

        Assert.Empty(AdviceRuleSet.Build(snapshot, TargetDate, AgentIntent.General));
    }
}
