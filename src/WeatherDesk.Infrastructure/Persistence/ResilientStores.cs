using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;

namespace WeatherDesk.Infrastructure.Persistence;

public sealed class ResilientWeatherCache : IWeatherCache
{
    private readonly IWeatherCache _inner;
    private readonly ILogger<ResilientWeatherCache> _logger;
    private readonly ConcurrentDictionary<string, WeatherSnapshot> _memory = new(StringComparer.Ordinal);
    public bool IsUsingMemory { get; private set; }
    public ResilientWeatherCache(IWeatherCache inner, ILogger<ResilientWeatherCache> logger) { _inner = inner; _logger = logger; }
    public async Task<Result<WeatherSnapshot?>> GetLatestAsync(Location location, CancellationToken ct)
    {
        var result = await _inner.GetLatestAsync(location, ct).ConfigureAwait(false);
        if (result.IsSuccess) return result;
        if (ct.IsCancellationRequested) return result;
        IsUsingMemory = true; _logger.LogWarning("Weather cache unavailable; using memory fallback.");
        return Result<WeatherSnapshot?>.Ok(_memory.TryGetValue(location.Id, out var snapshot) ? snapshot with { Provenance = WeatherProvenance.Cache } : null);
    }
    public async Task<Result> SaveAsync(WeatherSnapshot snapshot, CancellationToken ct)
    {
        var result = await _inner.SaveAsync(snapshot, ct).ConfigureAwait(false);
        if (result.IsSuccess) return result;
        if (ct.IsCancellationRequested) return result;
        IsUsingMemory = true; _memory[snapshot.Location.Id] = snapshot; _logger.LogWarning("Weather cache unavailable; saved to memory fallback.");
        return Result.Ok();
    }
}

public sealed class ResilientCityStore : ICityStore
{
    private readonly ICityStore _inner; private readonly ILogger<ResilientCityStore> _logger;
    private readonly ConcurrentDictionary<string, Location> _favorites = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, (Location City, DateTimeOffset At)> _recent = new(StringComparer.Ordinal);
    public bool IsUsingMemory { get; private set; }
    public ResilientCityStore(ICityStore inner, ILogger<ResilientCityStore> logger) { _inner = inner; _logger = logger; }
    public async Task<Result<IReadOnlyList<Location>>> GetFavoritesAsync(CancellationToken ct) { var r = await _inner.GetFavoritesAsync(ct); if (r.IsSuccess) return r; Activate(); return Result<IReadOnlyList<Location>>.Ok(_favorites.Values.ToArray()); }
    public async Task<Result> AddFavoriteAsync(Location location, CancellationToken ct) { var r = await _inner.AddFavoriteAsync(location, ct); if (r.IsSuccess) return r; Activate(); _favorites[location.Id] = location; return Result.Ok(); }
    public async Task<Result> RemoveFavoriteAsync(string locationId, CancellationToken ct) { var r = await _inner.RemoveFavoriteAsync(locationId, ct); if (r.IsSuccess) return r; Activate(); _favorites.TryRemove(locationId, out _); return Result.Ok(); }
    public async Task<Result<IReadOnlyList<Location>>> GetRecentAsync(CancellationToken ct) { var r = await _inner.GetRecentAsync(ct); if (r.IsSuccess) return r; Activate(); return Result<IReadOnlyList<Location>>.Ok(_recent.Values.OrderByDescending(x => x.At).Take(10).Select(x => x.City).ToArray()); }
    public async Task<Result> UpsertRecentAsync(Location location, DateTimeOffset visitedAtUtc, CancellationToken ct) { var r = await _inner.UpsertRecentAsync(location, visitedAtUtc, ct); if (r.IsSuccess) return r; Activate(); _recent[location.Id] = (location, visitedAtUtc); return Result.Ok(); }
    private void Activate() { IsUsingMemory = true; _logger.LogWarning("City storage unavailable; using memory fallback."); }
}

public sealed class ResilientAppStateStore : IAppStateStore
{
    private readonly IAppStateStore _inner; private readonly ILogger<ResilientAppStateStore> _logger; private Location? _last;
    public bool IsUsingMemory { get; private set; }
    public ResilientAppStateStore(IAppStateStore inner, ILogger<ResilientAppStateStore> logger) { _inner = inner; _logger = logger; }
    public async Task<Result<Location?>> GetLastCityAsync(CancellationToken ct) { var r = await _inner.GetLastCityAsync(ct); if (r.IsSuccess) return r; Activate(); return Result<Location?>.Ok(_last); }
    public async Task<Result> SetLastCityAsync(Location location, CancellationToken ct) { var r = await _inner.SetLastCityAsync(location, ct); if (r.IsSuccess) return r; Activate(); _last = location; return Result.Ok(); }
    private void Activate() { IsUsingMemory = true; _logger.LogWarning("Application state storage unavailable; using memory fallback."); }
}
