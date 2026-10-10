namespace WeatherDesk.Domain.Results;

/// <summary>
/// Result for operations without a payload. Failures are never expressed as null or exceptions.
/// </summary>
public sealed class Result
{
    private Result(bool isSuccess, Failure? failure)
    {
        IsSuccess = isSuccess;
        Failure = failure;
    }

    public bool IsSuccess { get; }

    public Failure? Failure { get; }

    public static Result Ok() => new(true, null);

    public static Result Fail(Failure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new Result(false, failure);
    }
}
