using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;
using WeatherDesk.Infrastructure.Ai;

namespace WeatherDesk.Tests.ServiceTests;

public sealed class AiAdapterTests
{
    private static readonly AppSettings Settings = new(AiMode.DeepSeek, "https://example.test", "deepseek-chat", true, TemperatureUnit.Celsius, AppTheme.FollowSystem);
    private static readonly AgentPrompt Prompt = new("system rules", "user weather data", new[] { "Chinese" });

    [Fact]
    public async Task DeepSeek_sends_compatible_request_and_reads_content()
    {
        HttpRequestMessage? request = null;
        string? requestBody = null;
        using var client = new HttpClient(new StubHandler(async r =>
        {
            request = r;
            requestBody = r.Content is null ? null : await r.Content.ReadAsStringAsync();
            return Json("{\"choices\":[{\"message\":{\"content\":\"回答\"}}]}");
        }));
        var result = await new DeepSeekClient(client, new FakeSettings("secret-key")).GenerateAsync(Prompt, Settings, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal("回答", result.Value);
        Assert.Equal("Bearer", request!.Headers.Authorization!.Scheme);
        Assert.Equal("secret-key", request.Headers.Authorization.Parameter);
        Assert.Contains("system rules", requestBody);
        Assert.Contains("user weather data", requestBody);
        Assert.Contains("\"stream\":false", requestBody);
    }

    [Fact]
    public async Task DeepSeek_maps_auth_and_malformed_failures()
    {
        using var unauthorized = new HttpClient(new StubHandler(_ => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Unauthorized))));
        var auth = await new DeepSeekClient(unauthorized, new FakeSettings("secret-key")).GenerateAsync(Prompt, Settings, CancellationToken.None);
        Assert.Equal(FailureKind.AiUnauthorized, auth.Failure!.Kind);

        using var malformed = new HttpClient(new StubHandler(_ => Task.FromResult(Json("{\"choices\":[]}"))));
        var bad = await new DeepSeekClient(malformed, new FakeSettings("secret-key")).GenerateAsync(Prompt, Settings, CancellationToken.None);
        Assert.Equal(FailureKind.AiMalformed, bad.Failure!.Kind);
    }

    [Fact]
    public async Task Demo_mode_does_not_call_http_and_is_stable()
    {
        var demo = new DemoAnswerGenerator();
        var prompt = new AgentPrompt("system", "天气数据 JSON：{\"city\":\"上海\",\"date\":\"2026-10-09\",\"daily\":{\"condition\":\"晴\",\"minC\":10,\"maxC\":20,\"rainProbability\":0,\"windKph\":10}}", Array.Empty<string>());
        var first = await demo.GenerateAsync(prompt, Settings with { Mode = AiMode.Demo }, CancellationToken.None);
        var second = await demo.GenerateAsync(prompt, Settings with { Mode = AiMode.Demo }, CancellationToken.None);
        Assert.True(first.IsSuccess);
        Assert.Equal(first.Value, second.Value);
        Assert.Contains("上海", first.Value);
        Assert.DoesNotContain("演示模式", first.Value);
    }

    [Fact]
    public async Task Demo_route_answer_includes_weather_for_both_cities()
    {
        var prompt = new AgentPrompt("system", "天气数据 JSON：{\"city\":\"上海\",\"date\":\"2026-10-09\",\"intent\":\"Clothing\",\"daily\":{\"condition\":\"多云\",\"minC\":15,\"maxC\":22,\"rainProbability\":10},\"origin\":{\"city\":\"武汉\",\"daily\":{\"condition\":\"小雨\",\"minC\":12,\"maxC\":18,\"rainProbability\":60}}}", Array.Empty<string>());

        var result = await new DemoAnswerGenerator().GenerateAsync(prompt, Settings with { Mode = AiMode.Demo }, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("武汉", result.Value);
        Assert.Contains("小雨", result.Value);
        Assert.Contains("上海", result.Value);
        Assert.Contains("多云", result.Value);
    }

    [Fact]
    public async Task Auto_without_key_falls_back_to_demo_without_http()
    {
        var handler = new StubHandler(_ => throw new InvalidOperationException("HTTP should not be called"));
        using var client = new HttpClient(handler);
        var selector = new AnswerGeneratorSelector(new DeepSeekClient(client, new FakeSettings(null)), new DemoAnswerGenerator(), new FakeSettings(null));
        var result = await selector.GenerateAsync(new AgentPrompt("system", "天气数据 JSON：{\"city\":\"上海\",\"date\":\"今天\",\"daily\":{\"condition\":\"晴\"}}", Array.Empty<string>()), Settings with { Mode = AiMode.Auto, HasApiKey = false }, CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Contains("上海", result.Value);
        Assert.DoesNotContain("演示模式", result.Value);
    }

    private static HttpResponseMessage Json(string content) => new(HttpStatusCode.OK) { Content = new StringContent(content, System.Text.Encoding.UTF8, "application/json") };

    private sealed class StubHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> callback) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => callback(request);
    }

    private sealed class FakeSettings(string? key) : ISettingsStore
    {
        public Task<Result<AppSettings>> LoadAsync(CancellationToken ct) => Task.FromResult(Result<AppSettings>.Ok(Settings));
        public Task<Result> SaveAsync(AppSettings settings, CancellationToken ct) => Task.FromResult(Result.Ok());
        public Task<Result<string?>> GetApiKeyAsync(CancellationToken ct) => Task.FromResult(Result<string?>.Ok(key));
        public Task<Result> SetApiKeyAsync(string apiKey, CancellationToken ct) => Task.FromResult(Result.Ok());
    }
}
