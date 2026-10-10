namespace WeatherDesk.Domain.Results;

/// <summary>
/// Serializable error carrier for domain models (for example AgentReply.Error) so a failure can
/// travel with a model without nesting Result instances.
/// </summary>
public sealed record ResultError(FailureKind Kind, string Message, bool Retryable)
{
    public static ResultError From(Failure failure) => new(failure.Kind, failure.Message, failure.Retryable);

    public Failure ToFailure() => new(Kind, Message, Retryable);
}
