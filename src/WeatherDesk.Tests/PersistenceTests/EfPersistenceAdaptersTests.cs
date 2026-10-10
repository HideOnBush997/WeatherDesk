using WeatherDesk.Domain.Models;
using WeatherDesk.Infrastructure.Persistence;
using Microsoft.Extensions.Logging.Abstractions;

namespace WeatherDesk.Tests.PersistenceTests;

public sealed class EfPersistenceAdaptersTests
{
    [Fact]
    public async Task WeatherCache_RoundTripsAndMarksCache()
    {
        using var temp = new TempDb();
        await temp.Factory.InitializeAsync(CancellationToken.None);
        var cache = EfWeatherCache.FromDbContextFactory(temp.Factory, NullLogger<EfWeatherCache>.Instance);
        var snapshot = Snapshot(temp.Location);
        Assert.True((await cache.SaveAsync(snapshot, CancellationToken.None)).IsSuccess);
        var result = await cache.GetLatestAsync(temp.Location, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(WeatherProvenance.Cache, result.Value!.Provenance);
        Assert.Equal(snapshot.Location, result.Value.Location);
    }

    [Fact]
    public async Task CityStore_UpsertsRecentAndKeepsTen()
    {
        using var temp = new TempDb();
        await temp.Factory.InitializeAsync(CancellationToken.None);
        var store = new EfCityStore(temp.Factory);
        for (var i = 0; i < 12; i++)
            Assert.True((await store.UpsertRecentAsync(temp.Location with { Id = $"{i}" }, DateTimeOffset.UtcNow.AddMinutes(i), CancellationToken.None)).IsSuccess);
        var result = await store.GetRecentAsync(CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(10, result.Value!.Count);
        Assert.Equal("11", result.Value[0].Id);
    }

    [Fact]
    public async Task AppState_RestoresFromFavoriteSnapshot()
    {
        using var temp = new TempDb();
        await temp.Factory.InitializeAsync(CancellationToken.None);
        var city = new EfCityStore(temp.Factory);
        var state = new EfAppStateStore(temp.Factory);
        Assert.True((await city.AddFavoriteAsync(temp.Location, CancellationToken.None)).IsSuccess);
        Assert.True((await state.SetLastCityAsync(temp.Location, CancellationToken.None)).IsSuccess);
        var result = await state.GetLastCityAsync(CancellationToken.None);
        Assert.Equal(temp.Location, result.Value);
    }

    private static WeatherSnapshot Snapshot(Location location) => new(location, null, [], [], DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "test");

    private sealed class TempDb : IDisposable
    {
        private readonly string _directory = Path.Combine(Path.GetTempPath(), "WeatherDeskTests", Guid.NewGuid().ToString("N"));
        public TempDb() { Factory = new AppDbContextFactory(Path.Combine(_directory, "test.db")); Location = new("tokyo", "Tokyo", "", "Japan", "JP", 35.6, 139.7, "Asia/Tokyo", 32400); }
        public AppDbContextFactory Factory { get; }
        public Location Location { get; }
        public void Dispose() { try { Directory.Delete(_directory, true); } catch { } }
    }
}
