using Microsoft.EntityFrameworkCore;
using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;
using WeatherDesk.Infrastructure.Time;

namespace WeatherDesk.Infrastructure.Persistence;

public sealed class EfCityStore : ICityStore
{
    public const int MaxRecentCities = 10;
    private readonly IDbContextFactory<AppDbContext> _factory;
    private readonly IClock _clock;

    public EfCityStore(IDbContextFactory<AppDbContext> factory, IClock? clock = null)
    {
        _factory = factory;
        _clock = clock ?? new SystemClock();
    }

    public async Task<Result<IReadOnlyList<Location>>> GetFavoritesAsync(CancellationToken ct)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var rows = await db.FavoriteCities.AsNoTracking().OrderByDescending(x => x.AddedAtUtc).ToListAsync(ct).ConfigureAwait(false);
            return Result<IReadOnlyList<Location>>.Ok(rows.Select(ToLocation).ToArray());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return Result<IReadOnlyList<Location>>.Fail(Failure.Canceled("操作已取消。")); }
        catch (Exception ex) when (IsStorageException(ex)) { return Result<IReadOnlyList<Location>>.Fail(Failure.Storage("本地数据库不可用。")); }
    }

    public async Task<Result> AddFavoriteAsync(Location location, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(location);
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            if (!await db.FavoriteCities.AnyAsync(x => x.LocationId == location.Id, ct).ConfigureAwait(false))
                db.FavoriteCities.Add(ToFavorite(location, _clock.UtcNow));
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return Result.Fail(Failure.Canceled("操作已取消。")); }
        catch (Exception ex) when (IsStorageException(ex)) { return Result.Fail(Failure.Storage("本地数据库不可用。")); }
    }

    public async Task<Result> RemoveFavoriteAsync(string locationId, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(locationId)) return Result.Fail(Failure.Input("城市标识不能为空。"));
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var row = await db.FavoriteCities.SingleOrDefaultAsync(x => x.LocationId == locationId, ct).ConfigureAwait(false);
            if (row is not null) { db.FavoriteCities.Remove(row); await db.SaveChangesAsync(ct).ConfigureAwait(false); }
            return Result.Ok();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return Result.Fail(Failure.Canceled("操作已取消。")); }
        catch (Exception ex) when (IsStorageException(ex)) { return Result.Fail(Failure.Storage("本地数据库不可用。")); }
    }

    public async Task<Result<IReadOnlyList<Location>>> GetRecentAsync(CancellationToken ct)
    {
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var rows = await db.RecentCities.AsNoTracking().OrderByDescending(x => x.VisitedAtUtc).Take(MaxRecentCities).ToListAsync(ct).ConfigureAwait(false);
            return Result<IReadOnlyList<Location>>.Ok(rows.Select(ToLocation).ToArray());
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return Result<IReadOnlyList<Location>>.Fail(Failure.Canceled("操作已取消。")); }
        catch (Exception ex) when (IsStorageException(ex)) { return Result<IReadOnlyList<Location>>.Fail(Failure.Storage("本地数据库不可用。")); }
    }

    public async Task<Result> UpsertRecentAsync(Location location, DateTimeOffset visitedAtUtc, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(location);
        try
        {
            await using var db = await _factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var row = await db.RecentCities.SingleOrDefaultAsync(x => x.LocationId == location.Id, ct).ConfigureAwait(false);
            if (row is null) db.RecentCities.Add(ToRecent(location, visitedAtUtc));
            else Update(row, location, visitedAtUtc);
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            var old = await db.RecentCities.OrderByDescending(x => x.VisitedAtUtc).Skip(MaxRecentCities).ToListAsync(ct).ConfigureAwait(false);
            if (old.Count > 0) { db.RecentCities.RemoveRange(old); await db.SaveChangesAsync(ct).ConfigureAwait(false); }
            return Result.Ok();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return Result.Fail(Failure.Canceled("操作已取消。")); }
        catch (Exception ex) when (IsStorageException(ex)) { return Result.Fail(Failure.Storage("本地数据库不可用。")); }
    }

    internal static Location ToLocation(FavoriteCityEntity x) => new(x.LocationId, x.Name, x.Admin1, x.Country, x.CountryCode, x.Latitude, x.Longitude, x.Timezone, x.UtcOffsetSeconds);
    internal static Location ToLocation(RecentCityEntity x) => new(x.LocationId, x.Name, x.Admin1, x.Country, x.CountryCode, x.Latitude, x.Longitude, x.Timezone, x.UtcOffsetSeconds);
    private static FavoriteCityEntity ToFavorite(Location x, DateTimeOffset at) => new() { LocationId=x.Id, Name=x.Name, Admin1=x.Admin1, Country=x.Country, CountryCode=x.CountryCode, Latitude=x.Latitude, Longitude=x.Longitude, Timezone=x.Timezone, UtcOffsetSeconds=x.UtcOffsetSeconds, AddedAtUtc=at.ToUniversalTime() };
    private static RecentCityEntity ToRecent(Location x, DateTimeOffset at) => new() { LocationId=x.Id, Name=x.Name, Admin1=x.Admin1, Country=x.Country, CountryCode=x.CountryCode, Latitude=x.Latitude, Longitude=x.Longitude, Timezone=x.Timezone, UtcOffsetSeconds=x.UtcOffsetSeconds, VisitedAtUtc=at.ToUniversalTime() };
    private static void Update(RecentCityEntity row, Location x, DateTimeOffset at) { row.Name=x.Name; row.Admin1=x.Admin1; row.Country=x.Country; row.CountryCode=x.CountryCode; row.Latitude=x.Latitude; row.Longitude=x.Longitude; row.Timezone=x.Timezone; row.UtcOffsetSeconds=x.UtcOffsetSeconds; row.VisitedAtUtc=at.ToUniversalTime(); }
    private static bool IsStorageException(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or TimeoutException or System.Data.Common.DbException or DbUpdateException;
}
