using System.Globalization;
using System.Text.Json;
using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;

namespace WeatherDesk.Infrastructure.Ai;

public sealed class DemoAnswerGenerator : IAnswerGenerator
{
    public bool IsAvailable(AppSettings settings) => true;

    public Task<Result<string>> GenerateAsync(AgentPrompt prompt, AppSettings settings, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        if (ct.IsCancellationRequested) return Task.FromResult(Result<string>.Fail(Failure.Canceled("演示回答已取消。")));
        try
        {
            using var document = JsonDocument.Parse(ExtractJson(prompt.UserContent));
            var root = document.RootElement;
            var city = root.TryGetProperty("city", out var cityValue) ? cityValue.GetString() : "当前城市";
            var date = root.TryGetProperty("date", out var dateValue) ? dateValue.GetString() : "目标日期";
            var intent = root.TryGetProperty("intent", out var intentValue) ? intentValue.GetString() : "General";
            var daily = root.TryGetProperty("daily", out var dailyValue) && dailyValue.ValueKind == JsonValueKind.Object ? dailyValue : default;
            var condition = daily.ValueKind == JsonValueKind.Object && daily.TryGetProperty("condition", out var conditionValue) ? conditionValue.GetString() : "天气情况暂无";
            var min = Number(daily, "minC");
            var max = Number(daily, "maxC");
            var rain = Number(daily, "rainProbability");
            var wind = Number(daily, "windKph");
            var origin = root.TryGetProperty("origin", out var originValue) && originValue.ValueKind == JsonValueKind.Object ? originValue : default;
            var originDaily = origin.ValueKind == JsonValueKind.Object && origin.TryGetProperty("daily", out var originDailyValue) && originDailyValue.ValueKind == JsonValueKind.Object
                ? originDailyValue
                : default;
            var additional = root.TryGetProperty("additional", out var additionalValue) && additionalValue.ValueKind == JsonValueKind.Array ? additionalValue : default;
            var summary = min.HasValue && max.HasValue ? $"{condition}，气温 {min:0.#} 至 {max:0.#}°C" : $"{condition}";
            var advice = new List<string>();
            if (intent == "Running")
            {
                if (rain >= 40 || condition is not null && (condition.Contains("雨") || condition.Contains("雷")))
                    advice.Add("不建议户外跑步，建议改为室内运动");
                else if (wind >= 30)
                    advice.Add("风力较大，建议降低强度并注意安全");
                else
                    advice.Add("天气适宜，可以按计划进行户外跑步");
            }
            else if (intent == "Umbrella")
            {
                advice.Add(rain >= 40 || condition is not null && condition.Contains("雨") ? "建议携带雨具" : "降水风险较低，可按计划出行");
            }
            else if (intent == "Commute")
            {
                advice.Add(rain >= 40 ? "通勤建议携带雨具并预留时间" : "天气适宜，可以按计划通勤");
            }
            else
            {
                if (rain >= 40) advice.Add("建议携带雨具");
                if (wind >= 30) advice.Add("注意防风");
                if (min <= 5) advice.Add("注意保暖");
                if (advice.Count == 0) advice.Add("天气适宜，可以按计划出行");
            }
            var routeSummary = string.Empty;
            if (originDaily.ValueKind == JsonValueKind.Object && origin.TryGetProperty("city", out var originCity))
            {
                var originCondition = originDaily.TryGetProperty("condition", out var originConditionValue) ? originConditionValue.GetString() : "天气情况暂无";
                var originMin = Number(originDaily, "minC");
                var originMax = Number(originDaily, "maxC");
                var originSummary = originMin.HasValue && originMax.HasValue
                    ? $"{originCondition}，{originMin:0.#} 至 {originMax:0.#}°C"
                    : originCondition;
                routeSummary = $"出发地 {originCity.GetString()}：{originSummary}；目的地 {city}：{summary}。";
            }
            var text = $"{(routeSummary.Length > 0 ? routeSummary : $"{city} {date}：{summary}。") }建议：{string.Join("；", advice)}。";
            if (additional.ValueKind == JsonValueKind.Array)
            {
                var places = additional.EnumerateArray().Select(item => item.TryGetProperty("city", out var name) ? name.GetString() : null).Where(name => !string.IsNullOrWhiteSpace(name));
                var placeText = string.Join("、", places);
                if (placeText.Length > 0) text += $"另外请注意：{placeText}也在行程中，请分别查看当地天气。";
            }
            return Task.FromResult(Result<string>.Ok(text));
        }
        catch (JsonException)
        {
            return Task.FromResult(Result<string>.Fail(Failure.AiMalformed("演示天气数据格式无法识别。")));
        }
    }

    private static string ExtractJson(string content)
    {
        var start = content.IndexOf('{');
        var end = content.LastIndexOf('}');
        return start >= 0 && end > start ? content[start..(end + 1)] : throw new JsonException();
    }

    private static double? Number(JsonElement element, string property)
    {
        if (element.ValueKind != JsonValueKind.Object || !element.TryGetProperty(property, out var value)) return null;
        return value.ValueKind == JsonValueKind.Number && value.TryGetDouble(out var number) ? number : null;
    }
}
