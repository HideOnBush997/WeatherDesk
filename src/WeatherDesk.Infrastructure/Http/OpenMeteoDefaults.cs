namespace WeatherDesk.Infrastructure.Http;

public static class OpenMeteoDefaults
{
    public static TimeSpan RequestTimeout { get; } = TimeSpan.FromSeconds(10);
}
