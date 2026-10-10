using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Rules;

namespace WeatherDesk.Tests.DomainTests;

public sealed class AgentParameterValidatorTests
{
    private static readonly DateTimeOffset NowUtc = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Reports_missing_city_and_date()
    {
        var result = AgentParameterValidator.Validate(new AgentParameterInput(" ", null), NowUtc);

        Assert.False(result.IsValid);
        Assert.Equal(new[] { "城市", "日期" }, result.MissingParameters);
        Assert.Empty(result.InvalidParameters);
    }

    [Theory]
    [InlineData(2026, 10, 2)]
    [InlineData(2026, 10, 8)]
    public void Accepts_dates_in_the_seven_day_forecast_window(int year, int month, int day)
    {
        var result = AgentParameterValidator.Validate(
            new AgentParameterInput("Shanghai", new DateOnly(year, month, day)),
            NowUtc);

        Assert.True(result.IsValid);
        Assert.Empty(result.MissingParameters);
        Assert.Empty(result.InvalidParameters);
    }

    [Theory]
    [InlineData(2026, 10, 1)]
    [InlineData(2026, 10, 9)]
    public void Rejects_dates_outside_the_seven_day_forecast_window(int year, int month, int day)
    {
        var result = AgentParameterValidator.Validate(
            new AgentParameterInput("Shanghai", new DateOnly(year, month, day)),
            NowUtc);

        Assert.Contains(result.InvalidParameters, item => item.Contains("7 天", StringComparison.Ordinal));
    }

    [Fact]
    public void Rejects_unknown_range_and_accepts_case_insensitive_known_range()
    {
        var invalid = AgentParameterValidator.Validate(
            new AgentParameterInput("Shanghai", new DateOnly(2026, 10, 2), "Week"),
            NowUtc);
        var valid = AgentParameterValidator.Validate(
            new AgentParameterInput("Shanghai", new DateOnly(2026, 10, 2), "hour"),
            NowUtc);

        Assert.Contains(invalid.InvalidParameters, item => item.Contains("范围", StringComparison.Ordinal));
        Assert.True(valid.IsValid);
    }

    [Fact]
    public void Uses_the_supplied_city_utc_offset_when_computing_today()
    {
        var nowUtc = new DateTimeOffset(2026, 10, 1, 18, 0, 0, TimeSpan.Zero);
        var result = AgentParameterValidator.Validate(
            new AgentParameterInput(
                "Shanghai",
                new DateOnly(2026, 10, 2),
                UtcOffset: TimeSpan.FromHours(8)),
            nowUtc);

        Assert.True(result.IsValid);
    }

    [Fact]
    public void Forecast_window_does_not_overflow_at_the_last_representable_date()
    {
        var result = AgentParameterValidator.Validate(
            new AgentParameterInput("Shanghai", DateOnly.MaxValue),
            DateOnly.MaxValue);

        Assert.True(result.IsValid);
    }
}
