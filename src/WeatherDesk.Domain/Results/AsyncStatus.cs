namespace WeatherDesk.Domain.Results;

/// <summary>
/// Per-region async state; weather, city search and chat each keep their own instance.
/// </summary>
public enum AsyncStatus
{
    Idle,
    Loading,
    Success,
    Empty,
    Error,
    Stale
}
