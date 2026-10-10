using System.ComponentModel;
using WeatherDesk.Domain.Results;

namespace WeatherDesk.Domain.Models;

public enum AgentReplyKind
{
    Answer,
    NeedClarification,
    OutOfScope,
    Error
}

public enum AgentIntent
{
    Unknown,
    Commute,
    Running,
    Umbrella,
    Clothing,
    General
}

public sealed record AgentContext(
    Location? CurrentCity,
    TimeSpan UtcOffset,
    bool PreferDemoMode)
{
    public DateOnly? Today { get; init; }
}

public sealed record AgentReply(
    AgentReplyKind Kind,
    string Text,
    Location? City,
    DateOnly? Date,
    AgentIntent Intent,
    IReadOnlyList<string> Evidence,
    ResultError? Error);

public sealed class ChatMessage : INotifyPropertyChanged
{
    public Guid Id { get; init; }

    public bool IsUser { get; init; }

    private string _text = string.Empty;
    public string Text { get => _text; set { if (_text == value) return; _text = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Text))); } }

    public string SpeakerText => IsUser ? "我" : "AI";

    public string MessageBackground => IsUser ? "#D9ECFF" : "#EEF2F7";

    public string MessageAlignment => IsUser ? "Right" : "Left";

    public string SpeakerLabelColor => IsUser ? "#0B76E8" : "#5B6573";

    public DateTimeOffset CreatedAtUtc { get; init; }

    public AgentReply? Reply { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;
}

public sealed record AgentPrompt(
    string SystemPrompt,
    string UserContent,
    IReadOnlyList<string> Constraints);

public sealed record QuestionAnalysis(
    string? City,
    DateOnly? Date,
    AgentIntent Intent,
    bool RequiresWeather,
    string? OriginCity = null,
    string? DestinationCity = null,
    IReadOnlyList<string>? AdditionalCities = null)
{
    public string? CityText => City;
}

public sealed record AgentParameterInput(
    string? City,
    DateOnly? Date,
    string? Range = "Day",
    TimeSpan UtcOffset = default);

public sealed record AgentParameterValidationResult(
    IReadOnlyList<string> Missing,
    IReadOnlyList<string> Invalid)
{
    public IReadOnlyList<string> MissingParameters => Missing;

    public IReadOnlyList<string> InvalidParameters => Invalid;

    public bool IsValid => Missing.Count == 0 && Invalid.Count == 0;
}
