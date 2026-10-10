using WeatherDesk.Domain.Models;

namespace WeatherDesk.Domain.Rules;

public static class CacheFreshnessPolicy
{
    public static bool IsFresh(WeatherSnapshot snapshot, DateTimeOffset nowUtc, TimeSpan ttl)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        return ttl >= TimeSpan.Zero && AgeOf(snapshot, nowUtc) <= ttl;
    }

    public static TimeSpan AgeOf(WeatherSnapshot snapshot, DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        var age = nowUtc - snapshot.RetrievedAtUtc;
        return age < TimeSpan.Zero ? TimeSpan.Zero : age;
    }
}
