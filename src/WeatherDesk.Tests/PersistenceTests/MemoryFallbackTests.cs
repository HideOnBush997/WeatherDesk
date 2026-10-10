using Microsoft.Extensions.Logging.Abstractions;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;
using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Infrastructure.Persistence;

namespace WeatherDesk.Tests.PersistenceTests;

public sealed class MemoryFallbackTests
{
    [Fact]
    public async Task Cache_falls_back_to_memory_when_sqlite_fails()
    {
        var location = new Location("1", "Test", null, null, null, 0, 0, null, null);
        var snapshot = new WeatherSnapshot(location, null, [], [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "test");
        var cache = new ResilientWeatherCache(new FailingWeatherCache(), NullLogger<ResilientWeatherCache>.Instance);

        Assert.True((await cache.SaveAsync(snapshot, CancellationToken.None)).IsSuccess);
        var result = await cache.GetLatestAsync(location, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(location, result.Value!.Location);
        Assert.True(cache.IsUsingMemory);
    }
}

file sealed class FailingWeatherCache : IWeatherCache
{
    public Task<Result<WeatherSnapshot?>> GetLatestAsync(Location location, CancellationToken ct) => Task.FromResult(Result<WeatherSnapshot?>.Fail(Failure.Storage("offline")));
    public Task<Result> SaveAsync(WeatherSnapshot snapshot, CancellationToken ct) => Task.FromResult(Result.Fail(Failure.Storage("offline")));
}
