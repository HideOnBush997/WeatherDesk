using Microsoft.Extensions.Logging.Abstractions;
using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;
using WeatherDesk.Domain.Services;

namespace WeatherDesk.Tests.ServiceTests;

public sealed class WeatherQueryServiceTests
{
    [Fact]
    public async Task Load_returns_live_snapshot_and_persists_it_and_the_selected_city()
    {
        var snapshot = Snapshot(Now);
        var weather = new FakeWeatherService(Result<WeatherSnapshot>.Ok(snapshot));
        var cache = new FakeWeatherCache(Result<WeatherSnapshot?>.Ok(null));
        var cityStore = new FakeCityStore(Result.Ok());
        var appState = new FakeAppStateStore(Result.Ok());
        var service = CreateService(weather, cache, cityStore, appState);

        var result = await service.LoadAsync(Shanghai, Date, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WeatherProvenance.Live, result.Value!.Snapshot.Provenance);
        Assert.False(result.Value.IsStale);
        Assert.Equal(snapshot.DataUpdatedAtUtc, result.Value.LastUpdatedAtUtc);
        Assert.Null(result.Value.DegradedReason);
        Assert.Equal(1, weather.Calls);
        Assert.Equal(Date, weather.LastDate);
        Assert.Equal(1, cache.SaveCalls);
        Assert.Same(result.Value.Snapshot, cache.SavedSnapshot);
        Assert.Equal(1, cityStore.UpsertCalls);
        Assert.Equal(Now, cityStore.LastVisitedAtUtc);
        Assert.Equal(1, appState.SetCalls);
    }

    [Fact]
    public async Task Load_returns_fresh_cache_with_the_upstream_failure_as_degraded_reason()
    {
        var upstreamFailure = Failure.Network("Network unavailable.");
        var cached = Snapshot(Now.AddMinutes(-29));
        var service = CreateService(
            new FakeWeatherService(Result<WeatherSnapshot>.Fail(upstreamFailure)),
            new FakeWeatherCache(Result<WeatherSnapshot?>.Ok(cached)),
            new FakeCityStore(Result.Ok()),
            new FakeAppStateStore(Result.Ok()));

        var result = await service.LoadAsync(Shanghai, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(cached with { Provenance = WeatherProvenance.Cache }, result.Value!.Snapshot);
        Assert.Equal(WeatherProvenance.Cache, result.Value.Snapshot.Provenance);
        Assert.False(result.Value.IsStale);
        Assert.Equal(upstreamFailure.Kind, result.Value.DegradedReason!.Kind);
    }

    [Fact]
    public async Task Load_returns_expired_cache_marked_stale()
    {
        var cached = Snapshot(Now.AddMinutes(-31));
        var service = CreateService(
            new FakeWeatherService(Result<WeatherSnapshot>.Fail(Failure.Network("Offline."))),
            new FakeWeatherCache(Result<WeatherSnapshot?>.Ok(cached)),
            new FakeCityStore(Result.Ok()),
            new FakeAppStateStore(Result.Ok()));

        var result = await service.LoadAsync(Shanghai, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value!.IsStale);
        Assert.Equal(WeatherProvenance.Cache, result.Value.Snapshot.Provenance);
        Assert.NotNull(result.Value.DegradedReason);
    }

    [Fact]
    public async Task Load_preserves_upstream_failure_when_there_is_no_cache()
    {
        var upstreamFailure = Failure.Upstream("Forecast unavailable.");
        var cache = new FakeWeatherCache(Result<WeatherSnapshot?>.Ok(null));
        var service = CreateService(
            new FakeWeatherService(Result<WeatherSnapshot>.Fail(upstreamFailure)),
            cache,
            new FakeCityStore(Result.Ok()),
            new FakeAppStateStore(Result.Ok()));

        var result = await service.LoadAsync(Shanghai, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Same(upstreamFailure, result.Failure);
        Assert.Equal(1, cache.GetCalls);
        Assert.Equal(0, cache.SaveCalls);
    }

    [Fact]
    public async Task Load_keeps_live_success_when_cache_city_or_last_city_writes_fail()
    {
        var snapshot = Snapshot(Now);
        var service = CreateService(
            new FakeWeatherService(Result<WeatherSnapshot>.Ok(snapshot)),
            new FakeWeatherCache(Result<WeatherSnapshot?>.Ok(null), Result.Fail(Failure.Storage("Cache unavailable."))),
            new FakeCityStore(Result.Fail(Failure.Storage("City store unavailable."))),
            new FakeAppStateStore(Result.Fail(Failure.Storage("App state unavailable."))));

        var result = await service.LoadAsync(Shanghai, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsStale);
        Assert.Equal(WeatherProvenance.Live, result.Value.Snapshot.Provenance);
        Assert.Null(result.Value.DegradedReason);
    }

    [Fact]
    public async Task Load_propagates_cancellation_without_reading_or_writing_cache()
    {
        var cache = new FakeWeatherCache(Result<WeatherSnapshot?>.Ok(null));
        var service = CreateService(
            new FakeWeatherService(Result<WeatherSnapshot>.Fail(Failure.Canceled("Canceled."))),
            cache,
            new FakeCityStore(Result.Ok()),
            new FakeAppStateStore(Result.Ok()));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var result = await service.LoadAsync(Shanghai, null, cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Canceled, result.Failure!.Kind);
        Assert.Equal(0, cache.GetCalls);
        Assert.Equal(0, cache.SaveCalls);
    }

    private static readonly DateOnly Date = new(2026, 10, 2);
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 4, 0, 0, TimeSpan.Zero);
    private static readonly Location Shanghai = new(
        "1796236", "Shanghai", "Shanghai", "China", "CN", 31.22, 121.46, "Asia/Shanghai", 28800);

    private static WeatherQueryService CreateService(
        IWeatherService weather,
        FakeWeatherCache cache,
        FakeCityStore cityStore,
        FakeAppStateStore appState) =>
        new(weather, cache, cityStore, appState, new FakeClock(Now), NullLogger<WeatherQueryService>.Instance);

    private static WeatherSnapshot Snapshot(DateTimeOffset retrievedAt) => new(
        Shanghai,
        new CurrentWeather(retrievedAt, 18, 18, 3, 60, 10, 12, 1010, null, true),
        Array.Empty<HourlyForecast>(),
        Array.Empty<DailyForecast>(),
        retrievedAt,
        retrievedAt,
        "Open-Meteo");

    private sealed class FakeClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
        public DateOnly TodayIn(TimeSpan utcOffset) => DateOnly.FromDateTime(utcNow.ToOffset(utcOffset).DateTime);
        public DateTimeOffset NowIn(TimeSpan utcOffset) => utcNow.ToOffset(utcOffset);
    }

    private sealed class FakeWeatherService(Result<WeatherSnapshot> result) : IWeatherService
    {
        public int Calls { get; private set; }
        public DateOnly? LastDate { get; private set; }

        public Task<Result<WeatherSnapshot>> GetForecastAsync(Location location, DateOnly? date, CancellationToken ct)
        {
            Calls++;
            LastDate = date;
            return Task.FromResult(result);
        }
    }

    private sealed class FakeWeatherCache(
        Result<WeatherSnapshot?> latest,
        Result? saveResult = null) : IWeatherCache
    {
        public int GetCalls { get; private set; }
        public int SaveCalls { get; private set; }
        public WeatherSnapshot? SavedSnapshot { get; private set; }

        public Task<Result<WeatherSnapshot?>> GetLatestAsync(Location location, CancellationToken ct)
        {
            GetCalls++;
            return Task.FromResult(latest);
        }

        public Task<Result> SaveAsync(WeatherSnapshot snapshot, CancellationToken ct)
        {
            SaveCalls++;
            SavedSnapshot = snapshot;
            return Task.FromResult(saveResult ?? Result.Ok());
        }
    }

    private sealed class FakeCityStore(Result upsertResult) : ICityStore
    {
        public int UpsertCalls { get; private set; }
        public DateTimeOffset? LastVisitedAtUtc { get; private set; }

        public Task<Result<IReadOnlyList<Location>>> GetFavoritesAsync(CancellationToken ct) =>
            Task.FromResult(Result<IReadOnlyList<Location>>.Ok(Array.Empty<Location>()));
        public Task<Result> AddFavoriteAsync(Location location, CancellationToken ct) => Task.FromResult(Result.Ok());
        public Task<Result> RemoveFavoriteAsync(string locationId, CancellationToken ct) => Task.FromResult(Result.Ok());
        public Task<Result<IReadOnlyList<Location>>> GetRecentAsync(CancellationToken ct) =>
            Task.FromResult(Result<IReadOnlyList<Location>>.Ok(Array.Empty<Location>()));
        public Task<Result> UpsertRecentAsync(Location location, DateTimeOffset visitedAtUtc, CancellationToken ct)
        {
            UpsertCalls++;
            LastVisitedAtUtc = visitedAtUtc;
            return Task.FromResult(upsertResult);
        }
    }

    private sealed class FakeAppStateStore(Result setResult) : IAppStateStore
    {
        public int SetCalls { get; private set; }
        public Task<Result<Location?>> GetLastCityAsync(CancellationToken ct) =>
            Task.FromResult(Result<Location?>.Ok(null));
        public Task<Result> SetLastCityAsync(Location location, CancellationToken ct)
        {
            SetCalls++;
            return Task.FromResult(setResult);
        }
    }
}
