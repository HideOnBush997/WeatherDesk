using Microsoft.Extensions.Logging.Abstractions;
using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;
using WeatherDesk.Domain.Rules;
using WeatherDesk.Domain.Services;

namespace WeatherDesk.Tests.DomainTests;

public sealed class WeatherQueryServiceTests
{
    private static readonly Location Shanghai = new(
        "1796236", "Shanghai", "Shanghai", "China", "CN", 31.22, 121.46, "Asia/Shanghai", 28800);
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 4, 10, 0, TimeSpan.Zero);

    [Fact]
    public async Task Load_returns_live_snapshot_and_persists_weather_city_and_selection()
    {
        var fixture = new Fixture();

        var result = await fixture.Service.LoadAsync(Shanghai, new DateOnly(2026, 10, 2), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.IsStale);
        Assert.Null(result.Value.DegradedReason);
        Assert.Equal(WeatherProvenance.Live, result.Value.Snapshot.Provenance);
        Assert.Equal(
            fixture.Weather.Snapshot with { QueryDate = new DateOnly(2026, 10, 2) },
            result.Value.Snapshot);
        Assert.Same(result.Value.Snapshot, fixture.Cache.SavedSnapshot);
        Assert.Equal(1, fixture.CityStore.RecentWriteCount);
        Assert.Equal(1, fixture.AppState.WriteCount);
    }

    [Theory]
    [InlineData(20, false)]
    [InlineData(31, true)]
    public async Task Load_uses_cached_snapshot_after_upstream_failure_and_reports_freshness(int ageMinutes, bool expectedStale)
    {
        var fixture = new Fixture();
        fixture.Weather.NextFailure = Failure.Upstream("Weather provider unavailable.");
        fixture.Cache.CachedSnapshot = Snapshot(Now.AddMinutes(-ageMinutes));

        var result = await fixture.Service.LoadAsync(Shanghai, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expectedStale, result.Value!.IsStale);
        Assert.Equal(WeatherProvenance.Cache, result.Value.Snapshot.Provenance);
        Assert.Equal(FailureKind.Upstream, result.Value.DegradedReason!.Kind);
        Assert.Equal(0, fixture.Cache.SaveCount);
        Assert.Equal(0, fixture.CityStore.RecentWriteCount);
        Assert.Equal(0, fixture.AppState.WriteCount);
    }

    [Fact]
    public async Task Load_preserves_upstream_failure_when_cache_is_missing_or_unavailable()
    {
        var fixture = new Fixture();
        fixture.Weather.NextFailure = Failure.Network("Network unavailable.");
        fixture.Cache.GetFailure = Failure.Storage("Cache unavailable.");

        var result = await fixture.Service.LoadAsync(Shanghai, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Network, result.Failure!.Kind);
        Assert.Equal(1, fixture.Cache.ReadCount);
    }

    [Fact]
    public async Task Load_does_not_turn_success_into_failure_when_side_persistence_fails()
    {
        var fixture = new Fixture();
        fixture.Cache.SaveFailure = Failure.Storage("Cache unavailable.");
        fixture.CityStore.WriteFailure = Failure.Storage("City storage unavailable.");
        fixture.AppState.WriteFailure = Failure.Storage("State storage unavailable.");

        var result = await fixture.Service.LoadAsync(Shanghai, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(WeatherProvenance.Live, result.Value!.Snapshot.Provenance);
    }

    [Fact]
    public async Task Load_does_not_read_cache_when_weather_request_is_canceled()
    {
        var fixture = new Fixture();
        fixture.Weather.NextFailure = Failure.Canceled("Canceled.");

        var result = await fixture.Service.LoadAsync(Shanghai, null, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Canceled, result.Failure!.Kind);
        Assert.Equal(0, fixture.Cache.ReadCount);
    }

    [Fact]
    public async Task Refresh_cancels_previous_in_flight_request_for_the_same_city()
    {
        var fixture = new Fixture { BlockFirstWeatherRequest = true };
        var firstRefresh = fixture.Service.RefreshAsync(Shanghai, null, CancellationToken.None);
        await fixture.Weather.FirstRequestStarted.Task.WaitAsync(TimeSpan.FromSeconds(2));

        var secondRefresh = fixture.Service.RefreshAsync(Shanghai, null, CancellationToken.None);
        var results = await Task.WhenAll(firstRefresh, secondRefresh).WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(results[0].IsSuccess);
        Assert.Equal(FailureKind.Canceled, results[0].Failure!.Kind);
        Assert.True(results[1].IsSuccess);
    }

    private static WeatherSnapshot Snapshot(DateTimeOffset retrievedAtUtc) => new(
        Shanghai,
        new CurrentWeather(retrievedAtUtc, 18.5, 17, 3, 70, 12, 19, 1012, null, true),
        Array.Empty<HourlyForecast>(),
        Array.Empty<DailyForecast>(),
        retrievedAtUtc,
        retrievedAtUtc,
        "Open-Meteo");

    private sealed class Fixture
    {
        public WeatherService Weather { get; } = new(Snapshot(Now));
        public WeatherCache Cache { get; } = new();
        public CityStore CityStore { get; } = new();
        public AppStateStore AppState { get; } = new();
        public bool BlockFirstWeatherRequest { get; init; }
        public WeatherQueryService Service { get; }

        public Fixture()
        {
            Weather.BlockFirstRequest = () => BlockFirstWeatherRequest;
            Service = new WeatherQueryService(
                Weather,
                Cache,
                CityStore,
                AppState,
                new TestClock(Now),
                NullLogger<WeatherQueryService>.Instance);
        }
    }

    private sealed class WeatherService(WeatherSnapshot snapshot) : IWeatherService
    {
        public WeatherSnapshot Snapshot => snapshot;
        public Failure? NextFailure { get; set; }
        public Func<bool>? BlockFirstRequest { get; set; }
        public TaskCompletionSource FirstRequestStarted { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _requestCount;

        public async Task<Result<WeatherSnapshot>> GetForecastAsync(Location location, DateOnly? date, CancellationToken ct)
        {
            if (Interlocked.Increment(ref _requestCount) == 1 && BlockFirstRequest?.Invoke() == true)
            {
                FirstRequestStarted.TrySetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, ct);
            }

            return NextFailure is { } failure
                ? Result<WeatherSnapshot>.Fail(failure)
                : Result<WeatherSnapshot>.Ok(snapshot);
        }
    }

    private sealed class WeatherCache : IWeatherCache
    {
        public WeatherSnapshot? CachedSnapshot { get; set; }
        public WeatherSnapshot? SavedSnapshot { get; private set; }
        public Failure? GetFailure { get; set; }
        public Failure? SaveFailure { get; set; }
        public int ReadCount { get; private set; }
        public int SaveCount { get; private set; }

        public Task<Result<WeatherSnapshot?>> GetLatestAsync(Location location, CancellationToken ct)
        {
            ReadCount++;
            return Task.FromResult(GetFailure is { } failure
                ? Result<WeatherSnapshot?>.Fail(failure)
                : Result<WeatherSnapshot?>.Ok(CachedSnapshot));
        }

        public Task<Result> SaveAsync(WeatherSnapshot value, CancellationToken ct)
        {
            SaveCount++;
            SavedSnapshot = value;
            return Task.FromResult(SaveFailure is { } failure ? Result.Fail(failure) : Result.Ok());
        }
    }

    private sealed class CityStore : ICityStore
    {
        public Failure? WriteFailure { get; set; }
        public int RecentWriteCount { get; private set; }

        public Task<Result<IReadOnlyList<Location>>> GetFavoritesAsync(CancellationToken ct) =>
            Task.FromResult(Result<IReadOnlyList<Location>>.Ok(Array.Empty<Location>()));
        public Task<Result> AddFavoriteAsync(Location location, CancellationToken ct) => Task.FromResult(Result.Ok());
        public Task<Result> RemoveFavoriteAsync(string locationId, CancellationToken ct) => Task.FromResult(Result.Ok());
        public Task<Result<IReadOnlyList<Location>>> GetRecentAsync(CancellationToken ct) =>
            Task.FromResult(Result<IReadOnlyList<Location>>.Ok(Array.Empty<Location>()));

        public Task<Result> UpsertRecentAsync(Location location, DateTimeOffset visitedAtUtc, CancellationToken ct)
        {
            RecentWriteCount++;
            return Task.FromResult(WriteFailure is { } failure ? Result.Fail(failure) : Result.Ok());
        }
    }

    private sealed class AppStateStore : IAppStateStore
    {
        public Failure? WriteFailure { get; set; }
        public int WriteCount { get; private set; }
        public Task<Result<Location?>> GetLastCityAsync(CancellationToken ct) =>
            Task.FromResult(Result<Location?>.Ok(null));

        public Task<Result> SetLastCityAsync(Location location, CancellationToken ct)
        {
            WriteCount++;
            return Task.FromResult(WriteFailure is { } failure ? Result.Fail(failure) : Result.Ok());
        }
    }

    private sealed class TestClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow => utcNow;
        public DateOnly TodayIn(TimeSpan utcOffset) => DateOnly.FromDateTime(utcNow.ToOffset(utcOffset).DateTime);
        public DateTimeOffset NowIn(TimeSpan utcOffset) => utcNow.ToOffset(utcOffset);
    }
}
