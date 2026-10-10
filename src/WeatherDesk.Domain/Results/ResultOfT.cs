namespace WeatherDesk.Domain.Results;

public sealed class Result<T>
{
    private Result(bool isSuccess, T? value, Failure? failure)
    {
        IsSuccess = isSuccess;
        Value = value;
        Failure = failure;
    }

    public bool IsSuccess { get; }

    public T? Value { get; }

    public Failure? Failure { get; }

    public static Result<T> Ok(T value) => new(true, value, null);

    public static Result<T> Fail(Failure failure)
    {
        ArgumentNullException.ThrowIfNull(failure);
        return new Result<T>(false, default, failure);
    }
}
