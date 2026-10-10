using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Rules;

namespace WeatherDesk.Tests.DomainTests;

public sealed class UnitConverterTests
{
    [Theory]
    [InlineData(0, 32)]
    [InlineData(20, 68)]
    [InlineData(100, 212)]
    [InlineData(-40, -40)]
    public void Converts_celsius_to_fahrenheit(double celsius, double expected)
    {
        Assert.Equal(expected, UnitConverter.ToFahrenheit(celsius), precision: 10);
    }

    [Theory]
    [InlineData(20, TemperatureUnit.Celsius, "20.0 °C")]
    [InlineData(20, TemperatureUnit.Fahrenheit, "68.0 °F")]
    [InlineData(-5, TemperatureUnit.Fahrenheit, "23.0 °F")]
    public void Formats_temperature_with_one_decimal_place(
        double celsius,
        TemperatureUnit unit,
        string expected)
    {
        Assert.Equal(expected, UnitConverter.FormatTemperature(celsius, unit));
    }
}
