using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;
using WeatherDesk.Domain.Services;

namespace WeatherDesk.Infrastructure.Agent;

public enum ForecastRange { Day, Hour }

public sealed record WeatherToolRequest(
    string? City,
    DateOnly? Date,
    ForecastRange Range,
    Location? ResolvedLocation = null);

public sealed record WeatherToolResult(WeatherSnapshot Snapshot, bool IsStale, DateTimeOffset? DataUpdatedAtUtc);

public interface IWeatherTool
{
    Task<Result<WeatherToolResult>> InvokeAsync(WeatherToolRequest request, CancellationToken ct);
}

public sealed class WeatherTool : IWeatherTool
{
    private readonly WeatherQueryService _query;
    private readonly IGeocodingService _geocoding;

    public WeatherTool(WeatherQueryService query, IGeocodingService geocoding)
    {
        _query = query ?? throw new ArgumentNullException(nameof(query));
        _geocoding = geocoding ?? throw new ArgumentNullException(nameof(geocoding));
    }

    public async Task<Result<WeatherToolResult>> InvokeAsync(WeatherToolRequest request, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.City) || request.Date is null)
        {
            return Result<WeatherToolResult>.Fail(Failure.Input("城市和日期不能为空。"));
        }
        if (request.Range is not ForecastRange.Day and not ForecastRange.Hour)
        {
            return Result<WeatherToolResult>.Fail(Failure.Input("不支持的预报范围。"));
        }

        Location? location = request.ResolvedLocation;
        if (location is null)
        {
            Result<IReadOnlyList<Location>> locations;
            try { locations = await _geocoding.SearchAsync(request.City.Trim(), ct); }
            catch (OperationCanceledException) { return Result<WeatherToolResult>.Fail(Failure.Canceled("天气查询已取消。")); }
            catch (Exception) { return Result<WeatherToolResult>.Fail(Failure.Network("城市搜索失败。")); }
            if (!locations.IsSuccess) return Result<WeatherToolResult>.Fail(locations.Failure!);
            location = locations.Value?.FirstOrDefault();
        }
        if (location is null) return Result<WeatherToolResult>.Fail(Failure.Input("未找到该城市。"));

        var result = await _query.LoadAsync(location, request.Date, ct);
        if (!result.IsSuccess) return Result<WeatherToolResult>.Fail(result.Failure!);
        var value = result.Value!;
        return Result<WeatherToolResult>.Ok(new WeatherToolResult(value.Snapshot, value.IsStale, value.LastUpdatedAtUtc));
    }
}
