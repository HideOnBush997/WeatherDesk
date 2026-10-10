using Microsoft.EntityFrameworkCore;
using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;

namespace WeatherDesk.Infrastructure.Persistence;

public sealed class EfAppStateStore(IDbContextFactory<AppDbContext> factory) : IAppStateStore
{
    public const string LastCityKey = "LastCityId";

    public async Task<Result<Location?>> GetLastCityAsync(CancellationToken ct)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var state = await db.AppStates.AsNoTracking().SingleOrDefaultAsync(x => x.Key == LastCityKey, ct).ConfigureAwait(false);
            if (state is null || string.IsNullOrWhiteSpace(state.Value)) return Result<Location?>.Ok(null);
            var favorite = await db.FavoriteCities.AsNoTracking().SingleOrDefaultAsync(x => x.LocationId == state.Value, ct).ConfigureAwait(false);
            if (favorite is not null) return Result<Location?>.Ok(EfCityStore.ToLocation(favorite));
            var recent = await db.RecentCities.AsNoTracking().SingleOrDefaultAsync(x => x.LocationId == state.Value, ct).ConfigureAwait(false);
            return Result<Location?>.Ok(recent is null ? null : EfCityStore.ToLocation(recent));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return Result<Location?>.Fail(Failure.Canceled("操作已取消。")); }
        catch (Exception ex) when (IsStorageException(ex)) { return Result<Location?>.Fail(Failure.Storage("本地数据库不可用。")); }
    }

    public async Task<Result> SetLastCityAsync(Location location, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(location);
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var state = await db.AppStates.SingleOrDefaultAsync(x => x.Key == LastCityKey, ct).ConfigureAwait(false);
            if (state is null) db.AppStates.Add(new AppStateEntity { Key = LastCityKey, Value = location.Id });
            else state.Value = location.Id;
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return Result.Fail(Failure.Canceled("操作已取消。")); }
        catch (Exception ex) when (IsStorageException(ex)) { return Result.Fail(Failure.Storage("本地数据库不可用。")); }
    }

    private static bool IsStorageException(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or TimeoutException or System.Data.Common.DbException or DbUpdateException;
}
