using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;
using WeatherDesk.Infrastructure.Time;

namespace WeatherDesk.Infrastructure.Ai;

public sealed class DeepSeekClient : IAnswerGenerator
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly HttpClient _httpClient;
    private readonly ISettingsStore _settingsStore;
    private readonly ILogger<DeepSeekClient> _logger;
    private readonly IClock _clock;

    public DeepSeekClient(HttpClient httpClient, ISettingsStore settingsStore, ILogger<DeepSeekClient>? logger = null, IClock? clock = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<DeepSeekClient>.Instance;
        _clock = clock ?? new SystemClock();
    }

    public bool IsAvailable(AppSettings settings) => settings.Mode != AiMode.Demo && settings.HasApiKey;

    public async Task<Result<string>> GenerateAsync(AgentPrompt prompt, AppSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        if (ct.IsCancellationRequested) return Result<string>.Fail(Failure.Canceled("AI 请求已取消。"));

        var keyResult = await _settingsStore.GetApiKeyAsync(ct).ConfigureAwait(false);
        if (!keyResult.IsSuccess)
        {
            return Result<string>.Fail(keyResult.Failure?.Kind == FailureKind.Canceled
                ? keyResult.Failure
                : Failure.AiUnavailable("读取 AI 凭据失败。"));
        }
        if (string.IsNullOrWhiteSpace(keyResult.Value))
            return Result<string>.Fail(Failure.AiUnauthorized("未配置 DeepSeek API Key。"));

        var baseUrl = string.IsNullOrWhiteSpace(settings.DeepSeekBaseUrl)
            ? "https://api.deepseek.com"
            : settings.DeepSeekBaseUrl.TrimEnd('/');
        var endpoint = baseUrl.EndsWith("/chat/completions", StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : baseUrl + "/chat/completions";
        var payload = new
        {
            model = string.IsNullOrWhiteSpace(settings.DeepSeekModel) ? "deepseek-chat" : settings.DeepSeekModel,
            messages = new[]
            {
                new { role = "system", content = prompt.SystemPrompt },
                new { role = "user", content = prompt.UserContent }
            },
            temperature = 0.3,
            max_tokens = 500,
            stream = false
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", keyResult.Value);
        request.Content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var started = _clock.UtcNow;
        try
        {
            using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, timeout.Token).ConfigureAwait(false);
            _logger.LogDebug("DeepSeek response status {StatusCode}, elapsed {ElapsedMs}ms", (int)response.StatusCode, (_clock.UtcNow - started).TotalMilliseconds);
            if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
                return Result<string>.Fail(Failure.AiUnauthorized("DeepSeek API Key 无效。"));
            if (!response.IsSuccessStatusCode)
            {
                var retryable = response.StatusCode == HttpStatusCode.RequestTimeout || response.StatusCode == (HttpStatusCode)429 || (int)response.StatusCode >= 500;
                return Result<string>.Fail(retryable
                    ? Failure.AiUnavailable($"DeepSeek 服务暂时不可用（HTTP {(int)response.StatusCode}）。")
                    : Failure.AiMalformed($"DeepSeek 请求被拒绝（HTTP {(int)response.StatusCode}）。"));
            }

            var json = await response.Content.ReadAsStringAsync(timeout.Token).ConfigureAwait(false);
            try
            {
                using var document = JsonDocument.Parse(json);
                var content = document.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();
                return string.IsNullOrWhiteSpace(content)
                    ? Result<string>.Fail(Failure.AiMalformed("DeepSeek 返回了空内容。"))
                    : Result<string>.Ok(content.Trim());
            }
            catch (Exception ex) when (ex is JsonException or InvalidOperationException or KeyNotFoundException or IndexOutOfRangeException)
            {
                return Result<string>.Fail(Failure.AiMalformed("DeepSeek 返回格式无法识别。"));
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result<string>.Fail(Failure.Canceled("AI 请求已取消。"));
        }
        catch (OperationCanceledException)
        {
            return Result<string>.Fail(Failure.AiUnavailable("DeepSeek 请求超时。"));
        }
        catch (HttpRequestException)
        {
            return Result<string>.Fail(Failure.AiUnavailable("无法连接 DeepSeek 服务。"));
        }
        catch (IOException)
        {
            return Result<string>.Fail(Failure.AiUnavailable("读取 DeepSeek 响应失败。"));
        }
    }
}
