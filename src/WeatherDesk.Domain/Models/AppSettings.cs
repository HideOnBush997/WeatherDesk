namespace WeatherDesk.Domain.Models;

public enum TemperatureUnit
{
    Celsius,
    Fahrenheit
}

public enum AppTheme
{
    Light,
    Dark,
    FollowSystem
}

public enum AiMode
{
    Auto,
    Demo,
    DeepSeek
}

public sealed record AppSettings(
    AiMode Mode,
    string DeepSeekBaseUrl,
    string DeepSeekModel,
    bool HasApiKey,
    TemperatureUnit Unit,
    AppTheme Theme);
