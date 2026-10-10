using WeatherDesk.Domain.Interfaces;

namespace WeatherDesk.Infrastructure.Time;

public sealed class SystemClock : IClock
{
    public DateTimeOffset UtcNow => DateTimeOffset.UtcNow;

    public DateOnly TodayIn(TimeSpan utcOffset) =>
        DateOnly.FromDateTime(UtcNow.ToOffset(utcOffset).DateTime);

    public DateTimeOffset NowIn(TimeSpan utcOffset) => UtcNow.ToOffset(utcOffset);
}
