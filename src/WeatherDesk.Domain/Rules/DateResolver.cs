using System.Globalization;
using System.Text.RegularExpressions;

namespace WeatherDesk.Domain.Rules;

public static class DateResolver
{
    private static readonly Regex DateExpression = new(
        @"(?<iso>\d{4}-\d{2}-\d{2})|(?<relative>大后天|今天|明天|后天)|(?<weekday>(?:星期|礼拜|周)[一二三四五六日天])",
        RegexOptions.CultureInvariant);

    public static bool TryResolve(string? text, DateOnly today, out DateOnly date)
    {
        date = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        foreach (Match match in DateExpression.Matches(text))
        {
            if (match.Groups["iso"].Success)
            {
                if (DateOnly.TryParseExact(
                    match.Value,
                    "yyyy-MM-dd",
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out date))
                {
                    return true;
                }

                continue;
            }

            if (match.Groups["relative"].Success)
            {
                var daysFromToday = match.Value switch
                {
                    "今天" => 0,
                    "明天" => 1,
                    "后天" => 2,
                    "大后天" => 3,
                    _ => 0
                };

                if (today.DayNumber > DateOnly.MaxValue.DayNumber - daysFromToday)
                {
                    return false;
                }

                date = today.AddDays(daysFromToday);
                return true;
            }

            var weekday = match.Value[^1] switch
            {
                '一' => DayOfWeek.Monday,
                '二' => DayOfWeek.Tuesday,
                '三' => DayOfWeek.Wednesday,
                '四' => DayOfWeek.Thursday,
                '五' => DayOfWeek.Friday,
                '六' => DayOfWeek.Saturday,
                '日' or '天' => DayOfWeek.Sunday,
                _ => (DayOfWeek?)null
            };

            if (weekday is { } targetDay)
            {
                var daysUntil = ((int)targetDay - (int)today.DayOfWeek + 7) % 7;
                if (today.DayNumber > DateOnly.MaxValue.DayNumber - daysUntil)
                {
                    return false;
                }

                date = today.AddDays(daysUntil);
                return true;
            }
        }

        return false;
    }
}
