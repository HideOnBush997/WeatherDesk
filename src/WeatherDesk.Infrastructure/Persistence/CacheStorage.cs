using Microsoft.EntityFrameworkCore;
using WeatherDesk.Domain.Results;

namespace WeatherDesk.Infrastructure.Persistence;

public interface ICacheStorage
{
    Task<Result<WeatherCacheEntity?>> GetLatestAsync(string locationId, CancellationToken ct);
    Task<Result> UpsertAsync(WeatherCacheEntity entity, CancellationToken ct);
    Task<Result> DeleteAsync(string locationId, CancellationToken ct);
}

public sealed class EfCacheStorage(IDbContextFactory<AppDbContext> factory) : ICacheStorage
{
    public async Task<Result<WeatherCacheEntity?>> GetLatestAsync(string locationId, CancellationToken ct)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            return Result<WeatherCacheEntity?>.Ok(await db.WeatherCaches.AsNoTracking()
                .SingleOrDefaultAsync(x => x.LocationId == locationId, ct).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result<WeatherCacheEntity?>.Fail(Failure.Canceled("操作已取消。"));
        }
        catch (Exception ex) when (IsStorageException(ex))
        {
            return Result<WeatherCacheEntity?>.Fail(Failure.Storage("本地数据库不可用。"));
        }
    }

    public async Task<Result> UpsertAsync(WeatherCacheEntity entity, CancellationToken ct)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var existing = await db.WeatherCaches.SingleOrDefaultAsync(x => x.LocationId == entity.LocationId, ct).ConfigureAwait(false);
            if (existing is null) db.WeatherCaches.Add(entity);
            else
            {
                existing.PayloadJson = entity.PayloadJson;
                existing.DataVersion = entity.DataVersion;
                existing.RetrievedAtUtc = entity.RetrievedAtUtc;
                existing.DataUpdatedAtUtc = entity.DataUpdatedAtUtc;
                existing.LocationNameSnapshot = entity.LocationNameSnapshot;
            }
            await db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result.Fail(Failure.Canceled("操作已取消。"));
        }
        catch (Exception ex) when (IsStorageException(ex))
        {
            return Result.Fail(Failure.Storage("本地数据库不可用。"));
        }
    }

    public async Task<Result> DeleteAsync(string locationId, CancellationToken ct)
    {
        try
        {
            await using var db = await factory.CreateDbContextAsync(ct).ConfigureAwait(false);
            var entity = await db.WeatherCaches.SingleOrDefaultAsync(x => x.LocationId == locationId, ct).ConfigureAwait(false);
            if (entity is not null) { db.WeatherCaches.Remove(entity); await db.SaveChangesAsync(ct).ConfigureAwait(false); }
            return Result.Ok();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return Result.Fail(Failure.Canceled("操作已取消。")); }
        catch (Exception ex) when (IsStorageException(ex)) { return Result.Fail(Failure.Storage("本地数据库不可用。")); }
    }

    private static bool IsStorageException(Exception ex) => ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or TimeoutException or System.Data.Common.DbException or DbUpdateException;
}
