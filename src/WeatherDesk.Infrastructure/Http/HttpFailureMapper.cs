using System.Net;
using WeatherDesk.Domain.Results;

namespace WeatherDesk.Infrastructure.Http;

public static class HttpFailureMapper
{
    public static Failure FromException(Exception exception, CancellationToken callerToken)
    {
        ArgumentNullException.ThrowIfNull(exception);

        if (callerToken.IsCancellationRequested)
        {
            return Failure.Canceled("请求已取消。");
        }

        return exception switch
        {
            OperationCanceledException => Failure.Timeout("外部服务请求超时。"),
            HttpRequestException => Failure.Network("无法连接到外部服务。"),
            _ => Failure.Network("外部服务请求失败。")
        };
    }

    public static Failure FromStatusCode(HttpStatusCode statusCode)
    {
        var numericCode = (int)statusCode;
        var retryable = numericCode == 408
            || numericCode == 429
            || numericCode >= 500;

        return Failure.Upstream(
            $"外部服务返回 HTTP {numericCode}。",
            retryable);
    }
}
