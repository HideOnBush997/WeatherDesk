using Microsoft.Extensions.Logging;
using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;
using WeatherDesk.Domain.Rules;

namespace WeatherDesk.Domain.Services;

public class WeatherQueryService
{
    private readonly IWeatherService _weatherService;
    private readonly IWeatherCache _cache;
    private readonly ICityStore _cityStore;
    private readonly IAppStateStore _appStateStore;
    private readonly IClock _clock;
    private readonly ILogger<WeatherQueryService> _logger;
    private readonly TimeSpan _cacheTtl;
    private readonly object _refreshLock = new();
    private readonly Dictionary<string, CancellationTokenSource> _refreshRequests = new(StringComparer.Ordinal);

    protected WeatherQueryService()
    {
        _weatherService = null!;
        _cache = null!;
        _cityStore = null!;
        _appStateStore = null!;
        _clock = null!;
        _logger = null!;
        _cacheTtl = CacheDefaults.DefaultTtl;
    }

    public WeatherQueryService(
        IWeatherService weatherService,
        IWeatherCache cache,
        ICityStore cityStore,
        IAppStateStore appStateStore,
        IClock clock,
        ILogger<WeatherQueryService> logger,
        TimeSpan? cacheTtl = null)
    {
        _weatherService = weatherService ?? throw new ArgumentNullException(nameof(weatherService));
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _cityStore = cityStore ?? throw new ArgumentNullException(nameof(cityStore));
        _appStateStore = appStateStore ?? throw new ArgumentNullException(nameof(appStateStore));
        _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheTtl = cacheTtl ?? CacheDefaults.DefaultTtl;
    }

    public virtual Task<Result<WeatherQueryResult>> LoadAsync(
        Location location,
        DateOnly? date,
        CancellationToken ct) =>
        LoadCoreAsync(location, date, ct);

    public virtual async Task<Result<WeatherQueryResult>> RefreshAsync(
        Location location,
        DateOnly? date,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(location);
        using var refreshRequest = CancellationTokenSource.CreateLinkedTokenSource(ct);
        lock (_refreshLock)
        {
            if (_refreshRequests.TryGetValue(location.Id, out var previousRequest))
            {
                previousRequest.Cancel();
            }

            _refreshRequests[location.Id] = refreshRequest;
        }

        try
        {
            return await LoadCoreAsync(location, date, refreshRequest.Token);
        }
        finally
        {
            lock (_refreshLock)
            {
                if (_refreshRequests.TryGetValue(location.Id, out var activeRequest)
                    && ReferenceEquals(activeRequest, refreshRequest))
                {
                    _refreshRequests.Remove(location.Id);
                }
            }
        }
    }

    private async Task<Result<WeatherQueryResult>> LoadCoreAsync(
        Location location,
        DateOnly? date,
        CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(location);

        Result<WeatherSnapshot> weatherResult;
        try
        {
            weatherResult = await _weatherService.GetForecastAsync(location, date, ct);
        }
        catch (OperationCanceledException)
        {
            return Result<WeatherQueryResult>.Fail(ct.IsCancellationRequested
                ? Failure.Canceled("天气查询已取消。")
                : Failure.Timeout("天气服务请求超时。"));
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Weather query failed before returning a result.");
            weatherResult = Result<WeatherSnapshot>.Fail(Failure.Network("天气服务请求失败。"));
        }

        if (weatherResult.IsSuccess && weatherResult.Value is { } liveSnapshot)
        {
            var normalizedSnapshot = liveSnapshot with
            {
                Provenance = WeatherProvenance.Live,
                QueryDate = date
            };
            var visitedAtUtc = _clock.UtcNow;
            await Task.WhenAll(
                PersistBestEffortAsync("weather cache", () => _cache.SaveAsync(normalizedSnapshot, ct)),
                PersistBestEffortAsync("recent city", () => _cityStore.UpsertRecentAsync(
                    normalizedSnapshot.Location, visitedAtUtc, ct)),
                PersistBestEffortAsync("last selected city", () => _appStateStore.SetLastCityAsync(
                    normalizedSnapshot.Location, ct)));

            return Result<WeatherQueryResult>.Ok(new WeatherQueryResult(
                normalizedSnapshot,
                false,
                normalizedSnapshot.DataUpdatedAtUtc,
                null));
        }

        var upstreamFailure = weatherResult.Failure ?? Failure.Network("天气服务请求失败。");
        if (ct.IsCancellationRequested || upstreamFailure.Kind == FailureKind.Canceled)
        {
            return Result<WeatherQueryResult>.Fail(ct.IsCancellationRequested
                ? Failure.Canceled("天气查询已取消。")
                : upstreamFailure);
        }

        Result<WeatherSnapshot?> cacheResult;
        try
        {
            cacheResult = await _cache.GetLatestAsync(location, ct);
        }
        catch (OperationCanceledException)
        {
            return Result<WeatherQueryResult>.Fail(ct.IsCancellationRequested
                ? Failure.Canceled("天气查询已取消。")
                : upstreamFailure);
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Weather cache fallback read failed.");
            return Result<WeatherQueryResult>.Fail(upstreamFailure);
        }

        if (ct.IsCancellationRequested)
        {
            return Result<WeatherQueryResult>.Fail(Failure.Canceled("天气查询已取消。"));
        }

        if (!cacheResult.IsSuccess || cacheResult.Value is not { } cachedSnapshot)
        {
            if (!cacheResult.IsSuccess)
            {
                _logger.LogWarning(
                    "Weather cache fallback read failed with {FailureKind}.",
                    cacheResult.Failure!.Kind);
            }

            return Result<WeatherQueryResult>.Fail(upstreamFailure);
        }

        var isStale = !CacheFreshnessPolicy.IsFresh(cachedSnapshot, _clock.UtcNow, _cacheTtl);
        var cacheSnapshot = cachedSnapshot with { Provenance = WeatherProvenance.Cache };
        return Result<WeatherQueryResult>.Ok(new WeatherQueryResult(
            cacheSnapshot,
            isStale,
            cacheSnapshot.DataUpdatedAtUtc,
            ResultError.From(upstreamFailure)));
    }

    private async Task PersistBestEffortAsync(string operation, Func<Task<Result>> persist)
    {
        try
        {
            var result = await persist();
            if (!result.IsSuccess)
            {
                _logger.LogWarning(
                    "Optional {Operation} persistence failed with {FailureKind}.",
                    operation,
                    result.Failure!.Kind);
            }
        }
        catch (Exception exception)
        {
            _logger.LogWarning(exception, "Optional {Operation} persistence threw an exception.", operation);
        }
    }
}
