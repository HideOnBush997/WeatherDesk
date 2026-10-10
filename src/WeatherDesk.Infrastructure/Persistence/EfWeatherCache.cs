using System.Text.Json;
using Microsoft.Extensions.Logging;
using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;

namespace WeatherDesk.Infrastructure.Persistence;

public sealed class EfWeatherCache : IWeatherCache
{
    public const int CurrentDataVersion = 1;
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ICacheStorage _storage;
    private readonly ILogger<EfWeatherCache> _logger;

    public EfWeatherCache(ICacheStorage storage, ILogger<EfWeatherCache> logger)
    {
        _storage = storage;
        _logger = logger;
    }

    public static EfWeatherCache FromDbContextFactory(Microsoft.EntityFrameworkCore.IDbContextFactory<AppDbContext> factory, ILogger<EfWeatherCache> logger)
        => new(new EfCacheStorage(factory), logger);

    public async Task<Result<WeatherSnapshot?>> GetLatestAsync(Location location, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(location);
        Result<WeatherCacheEntity?> result;
        try { result = await _storage.GetLatestAsync(location.Id, ct).ConfigureAwait(false); }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return Result<WeatherSnapshot?>.Fail(Failure.Canceled("操作已取消。")); }
        catch (Exception ex) { _logger.LogError(ex, "Unable to read weather cache."); return Result<WeatherSnapshot?>.Fail(Failure.Storage("本地数据库不可用。")); }
        if (!result.IsSuccess) return Result<WeatherSnapshot?>.Fail(result.Failure!);
        var entity = result.Value;
        if (entity is null) return Result<WeatherSnapshot?>.Ok(null);
        if (entity.DataVersion != CurrentDataVersion)
        {
            _logger.LogWarning("Ignoring weather cache for {LocationId} with unsupported version {DataVersion}.", location.Id, entity.DataVersion);
            return Result<WeatherSnapshot?>.Ok(null);
        }
        try
        {
            var snapshot = JsonSerializer.Deserialize<WeatherSnapshot>(entity.PayloadJson, JsonOptions);
            if (snapshot is null) return Result<WeatherSnapshot?>.Ok(null);
            if (snapshot.DataVersion != CurrentDataVersion)
            {
                _logger.LogWarning("Ignoring weather cache for {LocationId} with unsupported payload version {DataVersion}.", location.Id, snapshot.DataVersion);
                return Result<WeatherSnapshot?>.Ok(null);
            }
            return Result<WeatherSnapshot?>.Ok(snapshot with { Provenance = WeatherProvenance.Cache });
        }
        catch (JsonException ex)
        {
            _logger.LogWarning(ex, "Ignoring invalid weather cache for {LocationId}.", location.Id);
            return Result<WeatherSnapshot?>.Ok(null);
        }
        catch (NotSupportedException ex)
        {
            _logger.LogWarning(ex, "Ignoring unsupported weather cache for {LocationId}.", location.Id);
            return Result<WeatherSnapshot?>.Ok(null);
        }
    }

    public async Task<Result> SaveAsync(WeatherSnapshot snapshot, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        try
        {
            var entity = new WeatherCacheEntity
            {
                LocationId = snapshot.Location.Id,
                PayloadJson = JsonSerializer.Serialize(snapshot with { DataVersion = CurrentDataVersion }, JsonOptions),
                DataVersion = CurrentDataVersion,
                RetrievedAtUtc = snapshot.RetrievedAtUtc.ToUniversalTime(),
                DataUpdatedAtUtc = snapshot.DataUpdatedAtUtc.ToUniversalTime(),
                LocationNameSnapshot = snapshot.Location.Name
            };
            try { return await _storage.UpsertAsync(entity, ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { return Result.Fail(Failure.Canceled("操作已取消。")); }
            catch (Exception ex) { _logger.LogError(ex, "Unable to write weather cache."); return Result.Fail(Failure.Storage("本地数据库不可用。")); }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { return Result.Fail(Failure.Canceled("操作已取消。")); }
        catch (JsonException ex) { _logger.LogError(ex, "Unable to serialize weather cache."); return Result.Fail(Failure.Storage("天气缓存无法保存。")); }
    }
}
