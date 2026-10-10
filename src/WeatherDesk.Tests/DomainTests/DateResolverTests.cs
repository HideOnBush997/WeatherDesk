using WeatherDesk.Domain.Rules;

namespace WeatherDesk.Tests.DomainTests;

public sealed class DateResolverTests
{
    private static readonly DateOnly Today = new(2026, 10, 2);

    [Theory]
    [InlineData("今天", 2026, 10, 2)]
    [InlineData("明天", 2026, 10, 3)]
    [InlineData("后天", 2026, 10, 4)]
    [InlineData("大后天", 2026, 10, 5)]
    [InlineData("周一", 2026, 10, 5)]
    [InlineData("星期五", 2026, 10, 2)]
    [InlineData("礼拜日", 2026, 10, 4)]
    [InlineData("2026-10-05", 2026, 10, 5)]
    [InlineData("明天上午", 2026, 10, 3)]
    public void Resolves_supported_relative_weekday_and_iso_dates(
        string text,
        int year,
        int month,
        int day)
    {
        Assert.True(DateResolver.TryResolve(text, Today, out var resolved));
        Assert.Equal(new DateOnly(year, month, day), resolved);
    }

    [Theory]
    [InlineData("")]
    [InlineData("下周")]
    [InlineData("2026-02-30")]
    [InlineData("昨天")]
    public void Returns_false_for_unrecognized_or_invalid_dates(string text)
    {
        Assert.False(DateResolver.TryResolve(text, Today, out _));
    }

    [Fact]
    public void Returns_false_instead_of_overflowing_when_relative_date_cannot_be_represented()
    {
        Assert.False(DateResolver.TryResolve("明天", DateOnly.MaxValue, out _));
    }
}
