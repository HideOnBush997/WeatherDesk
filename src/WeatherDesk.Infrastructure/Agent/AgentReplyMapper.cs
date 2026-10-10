using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;

namespace WeatherDesk.Infrastructure.Agent;

public static class AgentReplyMapper
{
    public static AgentReply Answer(string text, WeatherToolResult result, AgentIntent intent, DateOnly date, IEnumerable<string>? extraEvidence = null)
    {
        var evidence = new List<string>();
        var day = result.Snapshot.Daily.FirstOrDefault(x => x.Date == date);
        if (day is not null)
        {
            evidence.Add($"{date:yyyy-MM-dd} 最高 {day.MaxTemperatureC:0.#}°C，最低 {day.MinTemperatureC:0.#}°C");
            evidence.Add($"降水概率 {day.PrecipitationProbabilityMaxPercent:0.#}%");
        }
        if (result.Snapshot.Provenance == WeatherProvenance.Cache) evidence.Add(result.IsStale ? "使用过期缓存，数据可能不是实时" : "实时服务不可用，使用缓存数据");
        if (extraEvidence is not null) evidence.AddRange(extraEvidence);
        return new AgentReply(AgentReplyKind.Answer, text, result.Snapshot.Location, date, intent, evidence, null);
    }

    public static AgentReply Clarification(string text, AgentIntent intent = AgentIntent.Unknown) => new(AgentReplyKind.NeedClarification, text, null, null, intent, Array.Empty<string>(), null);
    public static AgentReply OutOfScope() => new(AgentReplyKind.OutOfScope, "抱歉，我只能回答天气相关问题。", null, null, AgentIntent.Unknown, Array.Empty<string>(), null);
    public static AgentReply Error(Failure failure, Location? city, DateOnly? date, AgentIntent intent) => new(AgentReplyKind.Error, failure.Message, city, date, intent, Array.Empty<string>(), ResultError.From(failure));
}
