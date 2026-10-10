using WeatherDesk.Domain.Models;

namespace WeatherDesk.Domain.Rules;

public static class AgentParameterValidator
{
    public static AgentParameterValidationResult Validate(
        AgentParameterInput input,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(input);
        var today = DateOnly.FromDateTime(nowUtc.UtcDateTime.Add(input.UtcOffset));
        return Validate(input, today);
    }

    public static AgentParameterValidationResult Validate(AgentParameterInput input, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(input);
        var missing = new List<string>();
        var invalid = new List<string>();

        if (string.IsNullOrWhiteSpace(input.City))
        {
            missing.Add("城市");
        }

        if (input.Date is null)
        {
            missing.Add("日期");
        }
        else
        {
            var latestDate = DateOnly.FromDayNumber(
                Math.Min(DateOnly.MaxValue.DayNumber, today.DayNumber + 6));
            if (input.Date.Value < today || input.Date.Value > latestDate)
            {
                invalid.Add("日期必须在今天起 7 天预报范围内。");
            }
        }

        if (string.IsNullOrWhiteSpace(input.Range))
        {
            missing.Add("范围");
        }
        else if (!input.Range.Equals("Day", StringComparison.OrdinalIgnoreCase)
                 && !input.Range.Equals("Hour", StringComparison.OrdinalIgnoreCase))
        {
            invalid.Add("范围仅支持 Day 或 Hour。");
        }

        return new AgentParameterValidationResult(
            Array.AsReadOnly(missing.ToArray()),
            Array.AsReadOnly(invalid.ToArray()));
    }
}
