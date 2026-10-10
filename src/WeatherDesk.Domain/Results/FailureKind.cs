namespace WeatherDesk.Domain.Results;

public enum FailureKind
{
    None = 0,
    Input,
    Canceled,
    Timeout,
    Network,
    Upstream,
    DataFormat,
    Storage,
    AiUnavailable,
    AiUnauthorized,
    AiMalformed
}
