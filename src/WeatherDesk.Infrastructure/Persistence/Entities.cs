namespace WeatherDesk.Infrastructure.Persistence;

public sealed class FavoriteCityEntity
{
    public int Id { get; set; }

    public string LocationId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Admin1 { get; set; }

    public string? Country { get; set; }

    public string? CountryCode { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public string? Timezone { get; set; }

    public int? UtcOffsetSeconds { get; set; }

    public DateTimeOffset AddedAtUtc { get; set; }
}

public sealed class RecentCityEntity
{
    public int Id { get; set; }

    public string LocationId { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public string? Admin1 { get; set; }

    public string? Country { get; set; }

    public string? CountryCode { get; set; }

    public double Latitude { get; set; }

    public double Longitude { get; set; }

    public string? Timezone { get; set; }

    public int? UtcOffsetSeconds { get; set; }

    public DateTimeOffset VisitedAtUtc { get; set; }
}

public sealed class WeatherCacheEntity
{
    public string LocationId { get; set; } = string.Empty;

    public string PayloadJson { get; set; } = string.Empty;

    public int DataVersion { get; set; }

    public DateTimeOffset RetrievedAtUtc { get; set; }

    public DateTimeOffset DataUpdatedAtUtc { get; set; }

    public string LocationNameSnapshot { get; set; } = string.Empty;
}

public sealed class AppStateEntity
{
    public string Key { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;
}
