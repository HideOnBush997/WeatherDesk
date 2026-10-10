using System.Text.RegularExpressions;
using WeatherDesk.Domain.Models;

namespace WeatherDesk.Domain.Rules;

public static class QuestionAnalyzer
{
    private static readonly Regex DateExpressions = new(
        @"\d{4}-\d{2}-\d{2}|大后天|今天|明天|后天|(?:星期|礼拜|周)[一二三四五六日天]",
        RegexOptions.CultureInvariant);

    private static readonly Regex IsoDateExpression = new(
        @"\d{4}-\d{2}-\d{2}",
        RegexOptions.CultureInvariant);

    private static readonly Regex TimeOfDayExpressions = new(
        @"凌晨|清晨|早上|上午|中午|下午|傍晚|晚上|夜里|夜间",
        RegexOptions.CultureInvariant);

    private static readonly Regex LeadingPrompt = new(
        @"^(?:麻烦|请问|告诉我|帮我|请帮我|请帮忙|我想问|我想查|想问|想查|问一下|查一下|查询一下|看看|请)+",
        RegexOptions.CultureInvariant);

    private static readonly string[] CityBoundaries =
    [
        "下不下雨", "冷不冷", "热不热", "要不要", "会不会", "怎么样", "适不适合",
        "适合", "天气", "下雨", "降雨", "降水", "气温", "温度", "需要", "是否",
        "有雨", "刮风", "冷", "热", "风"
    ];

    public static QuestionAnalysis Analyze(string question, AgentContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        question ??= string.Empty;

        DateOnly? date = null;
        if (context.Today is { } today
            && DateResolver.TryResolve(question, today, out var resolvedDate))
        {
            date = resolvedDate;
        }
        else if (context.Today is null)
        {
            var isoDate = IsoDateExpression.Match(question);
            if (isoDate.Success
                && DateResolver.TryResolve(isoDate.Value, DateOnly.MinValue, out resolvedDate))
            {
                date = resolvedDate;
            }
        }

        // Travel planning is weather-related even when the user does not name
        // an explicit day. Use today's forecast as the starting point for the
        // trip advice instead of rejecting the question for a missing date.
        if (date is null && context.Today is { } travelToday
            && ContainsAny(question, "旅游", "旅行", "游玩", "度假", "游览"))
        {
            date = travelToday;
        }

        var route = ExtractRoute(question);
        var candidates = ExtractCityCandidates(question);
        var intent = AnalyzeIntent(question);
        if (intent == AgentIntent.Unknown && (route is not null || candidates.Count > 0))
            intent = AgentIntent.Commute;
        var city = intent == AgentIntent.Unknown ? null : route?.Destination ?? candidates.FirstOrDefault() ?? ExtractCity(question);
        var allCities = route is null ? candidates : new[] { route.Value.Origin, route.Value.Destination }.Concat(candidates);
        allCities = allCities.Distinct(StringComparer.Ordinal).ToArray();
        return new QuestionAnalysis(city, date, intent, intent != AgentIntent.Unknown,
            route?.Origin, route?.Destination, allCities.Where(value => !string.Equals(value, city, StringComparison.Ordinal)).ToArray());
    }

    public static QuestionAnalysis Analyze(string question, AgentContext context, DateOnly today)
    {
        ArgumentNullException.ThrowIfNull(context);
        return Analyze(question, context with { Today = today });
    }

    private static AgentIntent AnalyzeIntent(string question)
    {
        if (ContainsAny(question, "带伞", "雨伞", "下雨", "降雨", "降水", "下不下雨", "有雨"))
        {
            return AgentIntent.Umbrella;
        }

        if (ContainsAny(question, "跑步", "慢跑", "运动", "锻炼"))
        {
            return AgentIntent.Running;
        }

        if (ContainsAny(question, "穿什么", "穿衣", "衣服", "保暖", "穿"))
        {
            return AgentIntent.Clothing;
        }

        if (ContainsAny(question, "通勤", "上班", "出行", "出门", "外出", "路上"))
        {
            return AgentIntent.Commute;
        }

        if (ContainsAny(question, "旅游", "旅行", "游玩", "度假", "游览"))
        {
            return AgentIntent.Commute;
        }

        if (ContainsAny(
            question,
            "天气",
            "气温",
            "温度",
            "冷",
            "热",
            "风",
            "雪",
            "降雪",
            "湿度",
            "紫外线",
            "空气质量",
            "晴",
            "多云",
            "阴",
            "雾",
            "雷",
            "冰雹"))
        {
            return AgentIntent.General;
        }

        return AgentIntent.Unknown;
    }

    private static string? ExtractCity(string question)
    {
        var candidateText = DateExpressions.Replace(question, " ");
        candidateText = TimeOfDayExpressions.Replace(candidateText, " ");
        candidateText = LeadingPrompt.Replace(candidateText.Trim(), string.Empty);

        var boundaryIndex = FindFirstBoundary(candidateText);
        if (boundaryIndex >= 0)
        {
            candidateText = candidateText[..boundaryIndex];
        }

        var lastPlacePreposition = Math.Max(
            candidateText.LastIndexOf('在'),
            Math.Max(candidateText.LastIndexOf('去'), candidateText.LastIndexOf('到')));
        if (lastPlacePreposition >= 0)
        {
            candidateText = candidateText[(lastPlacePreposition + 1)..];
        }

        candidateText = Regex.Replace(candidateText, @"[^\p{IsCJKUnifiedIdeographs}]", string.Empty);
        candidateText = candidateText.TrimEnd('的');

        candidateText = NormalizeCityCandidate(candidateText);

        var provinceSeparator = candidateText.LastIndexOf('省');
        if (provinceSeparator >= 0)
        {
            candidateText = candidateText[(provinceSeparator + 1)..];
        }

        return candidateText.Length is >= 2 and <= 8 ? candidateText : null;
    }

    private static (string Origin, string Destination)? ExtractRoute(string question)
    {
        var route = Regex.Match(question,
            @"(?:从|由)(?<origin>[\p{IsCJKUnifiedIdeographs}]{2,8}?)(?:市|区|县)?(?:出发)?(?:去|到|前往)(?<destination>[\p{IsCJKUnifiedIdeographs}]{2,8}?)(?:市|区|县)?(?=天气|适合|怎么|如何|要|是否|会不会|下雨|穿|带|出门|外出|[，,。？?！!]|$)",
            RegexOptions.CultureInvariant);
        if (!route.Success)
        {
            var routeText = DateExpressions.Replace(question, " ");
            routeText = TimeOfDayExpressions.Replace(routeText, " ");
            routeText = LeadingPrompt.Replace(routeText.Trim(), string.Empty);
            routeText = Regex.Replace(routeText, @"^(?:我|要|想)+", string.Empty, RegexOptions.CultureInvariant);
            route = Regex.Match(routeText,
                @"^(?<origin>[\p{IsCJKUnifiedIdeographs}]{2,8}?)(?:市|区|县)?(?:出发)?(?:去|到|前往)(?<destination>[\p{IsCJKUnifiedIdeographs}]{2,8}?)(?:市|区|县)?(?=天气|适合|怎么|如何|要|是否|会不会|下雨|穿|带|出门|外出|[，,。？?！!]|$)",
                RegexOptions.CultureInvariant);
        }
        if (!route.Success) return null;
        var origin = route.Groups["origin"].Value.TrimEnd('市', '区', '县');
        var destination = route.Groups["destination"].Value.TrimEnd('市', '区', '县');
        return origin.Length >= 2 && destination.Length >= 2 ? (origin, destination) : null;
    }

    private static IReadOnlyList<string> ExtractCityCandidates(string question)
    {
        var text = DateExpressions.Replace(question, " ");
        text = TimeOfDayExpressions.Replace(text, " ");
        text = LeadingPrompt.Replace(text.Trim(), string.Empty);
        var match = Regex.Match(text, @"(?:去|到|前往)(?<places>[\p{IsCJKUnifiedIdeographs}]{2,8}(?:[和、，,][\p{IsCJKUnifiedIdeographs}]{2,8})*)", RegexOptions.CultureInvariant);
        if (!match.Success)
            match = Regex.Match(text, @"(?:问|查|查询)(?<places>[\p{IsCJKUnifiedIdeographs}]{2,8}(?:[和、，,][\p{IsCJKUnifiedIdeographs}]{2,8})*)", RegexOptions.CultureInvariant);
        if (!match.Success) return Array.Empty<string>();
        return match.Groups["places"].Value.Split(new[] { '和', '、', '，', ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(NormalizeCityCandidate)
            .Where(value => value.Length is >= 2 and <= 8)
            .Distinct(StringComparer.Ordinal).ToArray();
    }

    private static string NormalizeCityCandidate(string value)
    {
        value = value.Trim().TrimEnd('市', '区', '县');
        foreach (var suffix in new[] { "旅游", "旅行", "游玩", "度假", "游览" })
        {
            if (value.EndsWith(suffix, StringComparison.Ordinal))
            {
                value = value[..^suffix.Length];
                break;
            }
        }

        return value.Trim().TrimEnd('市', '区', '县');
    }

    private static int FindFirstBoundary(string text)
    {
        var firstIndex = -1;
        foreach (var boundary in CityBoundaries)
        {
            var index = text.IndexOf(boundary, StringComparison.Ordinal);
            if (index >= 0 && (firstIndex < 0 || index < firstIndex))
            {
                firstIndex = index;
            }
        }

        return firstIndex;
    }

    private static bool ContainsAny(string value, params string[] terms) =>
        terms.Any(term => value.Contains(term, StringComparison.Ordinal));
}
