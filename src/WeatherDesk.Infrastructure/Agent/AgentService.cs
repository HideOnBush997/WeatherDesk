using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;
using WeatherDesk.Domain.Rules;

namespace WeatherDesk.Infrastructure.Agent;

public sealed class AgentService : IWeatherAgent
{
    private readonly IWeatherTool _tool;
    private readonly IGeocodingService _geocoding;
    private readonly IAnswerGenerator _generator;
    private readonly AgentPromptBuilder _promptBuilder;
    private readonly AppSettings _settings;

    public AgentService(IWeatherTool tool, IGeocodingService geocoding, IAnswerGenerator generator, AgentPromptBuilder? promptBuilder = null, AppSettings? settings = null)
    {
        _tool = tool ?? throw new ArgumentNullException(nameof(tool)); _geocoding = geocoding ?? throw new ArgumentNullException(nameof(geocoding)); _generator = generator ?? throw new ArgumentNullException(nameof(generator));
        _promptBuilder = promptBuilder ?? new AgentPromptBuilder();
        _settings = settings ?? new AppSettings(AiMode.Auto, "https://api.deepseek.com", "deepseek-chat", false, TemperatureUnit.Celsius, AppTheme.FollowSystem);
    }

    public async Task<Result<AgentReply>> AskAsync(string question, AgentContext context, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(context);
        if (ct.IsCancellationRequested) return Result<AgentReply>.Fail(Failure.Canceled("对话已取消。"));
        var today = context.Today ?? DateOnly.FromDateTime(DateTime.UtcNow.Add(context.UtcOffset).Date);
        var analysis = QuestionAnalyzer.Analyze(question ?? string.Empty, context, today);
        if (!analysis.RequiresWeather) return Result<AgentReply>.Ok(AgentReplyMapper.OutOfScope());
        var city = context.CurrentCity;
        var cityHint = analysis.City;
        var evidence = new List<string>();
        WeatherToolResult? originResult = null;
        var additionalResults = new List<WeatherToolResult>();
        if (!string.IsNullOrWhiteSpace(analysis.OriginCity))
        {
            var originSearch = await SearchLocationAsync(analysis.OriginCity!, ct);
            if (!originSearch.IsSuccess) return Result<AgentReply>.Ok(AgentReplyMapper.Error(originSearch.Failure!, city, analysis.Date, analysis.Intent));
            if (originSearch.Value is null) return Result<AgentReply>.Ok(AgentReplyMapper.Clarification($"没有找到“{analysis.OriginCity}”，请提供更具体的出发城市。", analysis.Intent));
            if (analysis.Date is { } originDate)
            {
                var originWeather = await _tool.InvokeAsync(new WeatherToolRequest(originSearch.Value.Name, originDate, ForecastRange.Day, originSearch.Value), ct);
                if (!originWeather.IsSuccess) return Result<AgentReply>.Ok(AgentReplyMapper.Error(originWeather.Failure!, originSearch.Value, analysis.Date, analysis.Intent));
                originResult = originWeather.Value;
                evidence.Add($"出发地：{originSearch.Value.WeatherDisplayName}");
            }
        }
        if (!string.IsNullOrWhiteSpace(cityHint))
        {
            // Keep the selected province/coordinates when the question names
            // the same place again. Re-geocoding a bare Chinese name can pick
            // a different same-named result than the left weather panel.
            if (city is null || !SamePlaceName(cityHint, city.Name))
            {
                var search = await SearchLocationAsync(cityHint, ct);
                if (!search.IsSuccess) return Result<AgentReply>.Ok(AgentReplyMapper.Error(search.Failure!, city, analysis.Date, analysis.Intent));
                if (search.Value is null) return Result<AgentReply>.Ok(AgentReplyMapper.Clarification($"没有找到“{cityHint}”，请提供更具体的城市名称。", analysis.Intent));
                city = search.Value;
            }
        }
        if (city is null) return Result<AgentReply>.Ok(AgentReplyMapper.Clarification("请告诉我想查询的城市。", analysis.Intent));
        if (analysis.Date is null) return Result<AgentReply>.Ok(AgentReplyMapper.Clarification("请告诉我想查询的日期（今天起 7 天内）。", analysis.Intent));
        var validation = AgentParameterValidator.Validate(new AgentParameterInput(city.Name, analysis.Date), today);
        if (!validation.IsValid) return Result<AgentReply>.Ok(AgentReplyMapper.Clarification(string.Join("；", validation.Missing.Concat(validation.Invalid)), analysis.Intent));
        var toolResult = await _tool.InvokeAsync(new WeatherToolRequest(city.Name, analysis.Date, ForecastRange.Day, city), ct);
        if (!toolResult.IsSuccess)
        {
            if (toolResult.Failure!.Kind == FailureKind.Canceled && ct.IsCancellationRequested)
                return Result<AgentReply>.Fail(toolResult.Failure);
            return Result<AgentReply>.Ok(AgentReplyMapper.Error(toolResult.Failure!, city, analysis.Date, analysis.Intent));
        }
        foreach (var additionalCity in analysis.AdditionalCities ?? Array.Empty<string>())
        {
            var extraSearch = await SearchLocationAsync(additionalCity, ct);
            if (!extraSearch.IsSuccess || extraSearch.Value is null || string.Equals(extraSearch.Value.Name, city.Name, StringComparison.Ordinal)) continue;
            var extraWeather = await _tool.InvokeAsync(new WeatherToolRequest(extraSearch.Value.Name, analysis.Date.Value, ForecastRange.Day, extraSearch.Value), ct);
            if (extraWeather.IsSuccess) additionalResults.Add(extraWeather.Value!);
        }
        var prompt = _promptBuilder.Build(question ?? string.Empty, analysis.Intent, toolResult.Value!, analysis.Date.Value, originResult, additionalResults);
        var settings = context.PreferDemoMode ? _settings with { Mode = AiMode.Demo } : _settings;
        Result<string> generated;
        try { generated = await _generator.GenerateAsync(prompt, settings, ct); }
        catch (OperationCanceledException) { return Result<AgentReply>.Fail(Failure.Canceled("对话已取消。")); }
        catch (Exception) { generated = Result<string>.Fail(Failure.AiUnavailable("回答生成服务不可用。")); }
        if (!generated.IsSuccess)
        {
            if (generated.Failure!.Kind == FailureKind.Canceled && ct.IsCancellationRequested)
                return Result<AgentReply>.Fail(generated.Failure);
            return Result<AgentReply>.Ok(AgentReplyMapper.Error(generated.Failure!, city, analysis.Date, analysis.Intent));
        }
        var text = CleanAnswer(generated.Value ?? string.Empty);
        return Result<AgentReply>.Ok(AgentReplyMapper.Answer(text, toolResult.Value!, analysis.Intent, analysis.Date.Value, evidence));
    }

    private async Task<Result<Location?>> SearchLocationAsync(string keyword, CancellationToken ct)
    {
        keyword = keyword.Trim();
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["海南"] = "海口",
            ["海南省"] = "海口",
            ["新疆"] = "乌鲁木齐",
            ["新疆维吾尔自治区"] = "乌鲁木齐",
            ["西藏"] = "拉萨",
            ["西藏自治区"] = "拉萨",
            ["内蒙古"] = "呼和浩特",
            ["宁夏"] = "银川",
            ["广西"] = "南宁"
        };
        if (aliases.TryGetValue(keyword, out var alias)) keyword = alias;
        try
        {
            var result = await _geocoding.SearchAsync(keyword, ct);
            return result.IsSuccess
                ? Result<Location?>.Ok(result.Value?.FirstOrDefault())
                : Result<Location?>.Fail(result.Failure!);
        }
        catch (OperationCanceledException) { return Result<Location?>.Fail(Failure.Canceled("对话已取消。")); }
        catch (Exception) { return Result<Location?>.Fail(Failure.Network("城市搜索失败。")); }
    }

    private static string CleanAnswer(string text) => text.Replace("**", string.Empty, StringComparison.Ordinal).Trim();

    private static bool SamePlaceName(string left, string right) =>
        NormalizePlaceName(left).Equals(NormalizePlaceName(right), StringComparison.OrdinalIgnoreCase);

    private static string NormalizePlaceName(string value) =>
        value.Trim().TrimEnd('市', '区', '县', '旗');
}
