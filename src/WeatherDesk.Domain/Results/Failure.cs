namespace WeatherDesk.Domain.Results;

public sealed record Failure(FailureKind Kind, string Message, bool Retryable)
{
    public static Failure Input(string message) => new(FailureKind.Input, message, false);

    public static Failure Canceled(string message) => new(FailureKind.Canceled, message, true);

    public static Failure Timeout(string message) => new(FailureKind.Timeout, message, true);

    public static Failure Network(string message) => new(FailureKind.Network, message, true);

    public static Failure Upstream(string message, bool retryable = true) => new(FailureKind.Upstream, message, retryable);

    public static Failure DataFormat(string message) => new(FailureKind.DataFormat, message, true);

    public static Failure Storage(string message) => new(FailureKind.Storage, message, true);

    public static Failure AiUnavailable(string message) => new(FailureKind.AiUnavailable, message, true);

    public static Failure AiUnauthorized(string message) => new(FailureKind.AiUnauthorized, message, false);

    public static Failure AiMalformed(string message) => new(FailureKind.AiMalformed, message, true);
}
