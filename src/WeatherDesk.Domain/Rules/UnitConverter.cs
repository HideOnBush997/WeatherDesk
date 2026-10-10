using System.Globalization;
using WeatherDesk.Domain.Models;

namespace WeatherDesk.Domain.Rules;

public static class UnitConverter
{
    public static double ToFahrenheit(double celsius) => (celsius * 9 / 5) + 32;

    public static string FormatTemperature(double celsius, TemperatureUnit unit)
    {
        var (value, suffix) = unit switch
        {
            TemperatureUnit.Celsius => (celsius, "°C"),
            TemperatureUnit.Fahrenheit => (ToFahrenheit(celsius), "°F"),
            _ => throw new ArgumentOutOfRangeException(nameof(unit), unit, "Unknown temperature unit.")
        };

        return value.ToString("0.0", CultureInfo.InvariantCulture) + " " + suffix;
    }
}
