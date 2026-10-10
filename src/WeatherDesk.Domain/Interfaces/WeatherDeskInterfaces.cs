using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;

namespace WeatherDesk.Domain.Interfaces;

public interface IGeocodingService
{
    Task<Result<IReadOnlyList<Location>>> SearchAsync(string keyword, CancellationToken ct);
}

public interface IWeatherService
{
    Task<Result<WeatherSnapshot>> GetForecastAsync(Location location, DateOnly? date, CancellationToken ct);
}

public interface IWeatherCache
{
    Task<Result<WeatherSnapshot?>> GetLatestAsync(Location location, CancellationToken ct);
    Task<Result> SaveAsync(WeatherSnapshot snapshot, CancellationToken ct);
}

public interface ICityStore
{
    Task<Result<IReadOnlyList<Location>>> GetFavoritesAsync(CancellationToken ct);
    Task<Result> AddFavoriteAsync(Location location, CancellationToken ct);
    Task<Result> RemoveFavoriteAsync(string locationId, CancellationToken ct);
    Task<Result<IReadOnlyList<Location>>> GetRecentAsync(CancellationToken ct);
    Task<Result> UpsertRecentAsync(Location location, DateTimeOffset visitedAtUtc, CancellationToken ct);
}

public interface IAppStateStore
{
    Task<Result<Location?>> GetLastCityAsync(CancellationToken ct);
    Task<Result> SetLastCityAsync(Location location, CancellationToken ct);
}

public interface ISettingsStore
{
    Task<Result<AppSettings>> LoadAsync(CancellationToken ct);
    Task<Result> SaveAsync(AppSettings settings, CancellationToken ct);
    Task<Result<string?>> GetApiKeyAsync(CancellationToken ct);
    Task<Result> SetApiKeyAsync(string apiKey, CancellationToken ct);
}

public interface IAnswerGenerator
{
    bool IsAvailable(AppSettings settings);
    Task<Result<string>> GenerateAsync(AgentPrompt prompt, AppSettings settings, CancellationToken ct);
}

public interface IWeatherAgent
{
    Task<Result<AgentReply>> AskAsync(string question, AgentContext context, CancellationToken ct);
}

public interface IClock
{
    DateTimeOffset UtcNow { get; }
    DateOnly TodayIn(TimeSpan utcOffset);
    DateTimeOffset NowIn(TimeSpan utcOffset);
}
