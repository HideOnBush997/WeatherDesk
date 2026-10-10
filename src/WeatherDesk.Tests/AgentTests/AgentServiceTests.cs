using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;
using WeatherDesk.Infrastructure.Agent;

namespace WeatherDesk.Tests.AgentTests;

public sealed class AgentServiceTests
{
    private static readonly DateOnly Today = new(2026, 10, 8);
    private static readonly Location Shanghai = new("1", "上海", null, "中国", "CN", 31, 121, "Asia/Shanghai", 28800);
    private static readonly AppSettings Settings = new(AiMode.Auto, "", "", false, TemperatureUnit.Celsius, AppTheme.FollowSystem);

    [Fact]
    public async Task Non_weather_question_is_out_of_scope_without_tool_call()
    {
        var tool = new FakeTool();
        var service = Create(tool);
        var result = await service.AskAsync("你是谁？", Context(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(AgentReplyKind.OutOfScope, result.Value!.Kind);
        Assert.Equal(0, tool.Calls);
    }

    [Fact]
    public async Task Missing_city_is_clarification()
    {
        var tool = new FakeTool();
        var result = await Create(tool).AskAsync("明天天气怎么样", Context(), CancellationToken.None);
        Assert.Equal(AgentReplyKind.NeedClarification, result.Value!.Kind);
        Assert.Equal(0, tool.Calls);
    }

    [Fact]
    public async Task Missing_date_is_clarification()
    {
        var tool = new FakeTool();
        var result = await Create(tool).AskAsync("上海天气怎么样", Context(), CancellationToken.None);
        Assert.Equal(AgentReplyKind.NeedClarification, result.Value!.Kind);
        Assert.Contains("日期", result.Value.Text);
        Assert.Equal(0, tool.Calls);
    }

    [Fact]
    public async Task Complete_question_generates_answer_and_evidence()
    {
        var tool = new FakeTool { Result = Result<WeatherToolResult>.Ok(new WeatherToolResult(Snapshot(), false, Snapshot().DataUpdatedAtUtc)) };
        var result = await Create(tool).AskAsync("上海明天天气怎么样", Context(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(AgentReplyKind.Answer, result.Value!.Kind);
        Assert.Equal(Shanghai, result.Value.City);
        Assert.Equal(Today.AddDays(1), result.Value.Date);
        Assert.NotEmpty(result.Value.Evidence);
        Assert.Equal(1, tool.Calls);
    }

    [Fact]
    public async Task Tool_failure_is_returned_with_retry_metadata()
    {
        var failure = Failure.Network("offline");
        var tool = new FakeTool { Result = Result<WeatherToolResult>.Fail(failure) };
        var result = await Create(tool).AskAsync("上海明天天气怎么样", Context(), CancellationToken.None);
        Assert.True(result.IsSuccess);
        Assert.Equal(AgentReplyKind.Error, result.Value!.Kind);
        Assert.Equal(FailureKind.Network, result.Value.Error!.Kind);
        Assert.True(result.Value.Error.Retryable);
    }

    [Fact]
    public async Task Out_of_range_date_is_clarification_without_tool_call()
    {
        var tool = new FakeTool();
        var result = await Create(tool).AskAsync("上海 2026-10-20 天气怎么样", Context(), CancellationToken.None);
        Assert.Equal(AgentReplyKind.NeedClarification, result.Value!.Kind);
        Assert.Equal(0, tool.Calls);
    }

    [Fact]
    public async Task Demo_mode_returns_user_facing_answer_without_internal_mode_label()
    {
        var tool = new FakeTool { Result = Result<WeatherToolResult>.Ok(new WeatherToolResult(Snapshot(), false, null)) };
        var service = new AgentService(tool, new FakeGeocoding(), new FakeGenerator(), settings: Settings with { Mode = AiMode.Demo });
        var result = await service.AskAsync("上海明天天气怎么样", Context() with { PreferDemoMode = true }, CancellationToken.None);
        Assert.DoesNotContain("演示模式", result.Value!.Text);
    }

    [Fact]
    public async Task Unknown_city_requests_clarification()
    {
        var tool = new FakeTool();
        var result = await new AgentService(tool, new FakeGeocoding(Array.Empty<Location>()), new FakeGenerator(), settings: Settings)
            .AskAsync("不存在城市明天天气怎么样", Context(), CancellationToken.None);
        Assert.Equal(AgentReplyKind.NeedClarification, result.Value!.Kind);
        Assert.Equal(0, tool.Calls);
    }

    [Fact]
    public async Task Weather_query_reuses_the_selected_location_instead_of_regeocoding_by_name()
    {
        var tool = new FakeTool { Result = Result<WeatherToolResult>.Ok(new WeatherToolResult(Snapshot(), false, null)) };
        var result = await Create(tool).AskAsync("明天天气怎么样", Context() with { CurrentCity = Shanghai }, CancellationToken.None);

        Assert.Equal(AgentReplyKind.Answer, result.Value!.Kind);
        Assert.Same(Shanghai, tool.LastRequest!.ResolvedLocation);
    }

    private static AgentService Create(FakeTool tool) => new(tool, new FakeGeocoding(), new FakeGenerator(), settings: Settings);
    private static AgentContext Context() => new(null, TimeSpan.FromHours(8), false) { Today = Today };
    private static WeatherSnapshot Snapshot() => new(Shanghai, null, Array.Empty<HourlyForecast>(), new[] { new DailyForecast(Today.AddDays(1), 18, 25, 1, 0, 10, 15, null, null, null) }, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "fake");

    private sealed class FakeTool : IWeatherTool
    {
        public int Calls { get; private set; }
        public WeatherToolRequest? LastRequest { get; private set; }
        public Result<WeatherToolResult> Result { get; set; } = Result<WeatherToolResult>.Fail(Failure.Network("not configured"));
        public Task<Result<WeatherToolResult>> InvokeAsync(WeatherToolRequest request, CancellationToken ct) { Calls++; LastRequest = request; return Task.FromResult(Result); }
    }
    private sealed class FakeGeocoding : IGeocodingService
    {
        private readonly IReadOnlyList<Location> _locations;
        public FakeGeocoding(IReadOnlyList<Location>? locations = null) => _locations = locations ?? new[] { Shanghai };
        public Task<Result<IReadOnlyList<Location>>> SearchAsync(string keyword, CancellationToken ct) => Task.FromResult(Result<IReadOnlyList<Location>>.Ok(_locations));
    }
    private sealed class FakeGenerator : IAnswerGenerator
    {
        public bool IsAvailable(AppSettings settings) => true;
        public Task<Result<string>> GenerateAsync(AgentPrompt prompt, AppSettings settings, CancellationToken ct) => Task.FromResult(Result<string>.Ok("明天适合出行。"));
    }
}
