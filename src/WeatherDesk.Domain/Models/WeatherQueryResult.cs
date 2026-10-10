using WeatherDesk.Domain.Results;

namespace WeatherDesk.Domain.Models;

public sealed record WeatherQueryResult(
    WeatherSnapshot Snapshot,
    bool IsStale,
    DateTimeOffset? LastUpdatedAtUtc,
    ResultError? DegradedReason);
