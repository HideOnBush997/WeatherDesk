using System.Text;
using System.Text.Json;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Rules;

namespace WeatherDesk.Infrastructure.Agent;

public sealed class AgentPromptBuilder
{
    public AgentPrompt Build(string question, AgentIntent intent, WeatherToolResult toolResult, DateOnly date, WeatherToolResult? originResult = null, IReadOnlyList<WeatherToolResult>? additionalResults = null)
    {
        ArgumentNullException.ThrowIfNull(toolResult);
        var snapshot = toolResult.Snapshot;
        var daily = snapshot.Daily.FirstOrDefault(x => x.Date == date);
        var offset = TimeSpan.FromSeconds(snapshot.Location.UtcOffsetSeconds ?? 0);
        var hourly = snapshot.Hourly
            .Where(x => DateOnly.FromDateTime(x.HourUtc.UtcDateTime.Add(offset)) == date && x.HourUtc.Add(offset).Hour is >= 6 and <= 22)
            .OrderBy(x => x.HourUtc)
            .Select(x => new { time = x.HourUtc.Add(offset).ToString("HH:mm"), tempC = x.TemperatureC, rainProbability = x.PrecipitationProbabilityPercent, rainMm = x.PrecipitationMm, windKph = x.WindSpeedKph, condition = WeatherCodeCatalog.Describe(x.WeatherCode) })
            .ToArray();
        var data = new
        {
            city = snapshot.Location.WeatherDisplayName,
            date = date.ToString("yyyy-MM-dd"),
            intent = intent.ToString(),
            daily = daily is null ? null : new { minC = daily.MinTemperatureC, maxC = daily.MaxTemperatureC, rainProbability = daily.PrecipitationProbabilityMaxPercent, rainMm = daily.PrecipitationSumMm, windKph = daily.WindSpeedMaxKph, condition = WeatherCodeCatalog.Describe(daily.WeatherCode) },
            hourly,
            source = snapshot.Provenance == WeatherProvenance.Cache ? "cache" : "live",
            stale = toolResult.IsStale
            ,origin = originResult is null ? null : new
            {
                city = originResult.Snapshot.Location.WeatherDisplayName,
                daily = originResult.Snapshot.Daily.FirstOrDefault(x => x.Date == date) is { } originDay
                    ? new { minC = originDay.MinTemperatureC, maxC = originDay.MaxTemperatureC, rainProbability = originDay.PrecipitationProbabilityMaxPercent, condition = WeatherCodeCatalog.Describe(originDay.WeatherCode) }
                    : null
            },
            additional = additionalResults?.Select(result => new
            {
                city = result.Snapshot.Location.WeatherDisplayName,
                daily = result.Snapshot.Daily.FirstOrDefault(x => x.Date == date) is { } day
                    ? new { minC = day.MinTemperatureC, maxC = day.MaxTemperatureC, rainProbability = day.PrecipitationProbabilityMaxPercent, condition = WeatherCodeCatalog.Describe(day.WeatherCode) }
                    : null
            }).ToArray()
        };
        var json = JsonSerializer.Serialize(data);
        return new AgentPrompt(
            "你是 WeatherDesk 天气助手。天气相关问题包括天气状况、气温、降水、穿衣、跑步、通勤、是否适合出门或外出、旅游旅行游玩度假期间的注意事项，以及从一个城市前往另一个城市的出行安排。遇到去某地旅游几天等问题，要根据提供的天气数据给出穿衣、带伞、防晒和行程安排等可执行建议。只能依据提供的结构化天气数据，不得编造数值；路线问题必须同时比较出发地和目的地天气，先给明确可执行建议，再补充天气依据；用中文简洁回答，不要输出 Markdown 粗体符号 **。",
            $"用户问题：{question}\n意图：{intent}\n天气数据 JSON：{json}",
            new[] { "仅使用结构化天气数据", "中文输出", "控制在 200 字以内" });
    }
}
