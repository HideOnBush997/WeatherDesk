namespace WeatherDesk.Domain.Rules;

public static class CacheDefaults
{
    public static TimeSpan DefaultTtl { get; } = TimeSpan.FromMinutes(30);
}
