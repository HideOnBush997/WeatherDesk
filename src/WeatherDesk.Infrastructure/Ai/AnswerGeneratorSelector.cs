using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;

namespace WeatherDesk.Infrastructure.Ai;

public sealed class AnswerGeneratorSelector : IAnswerGenerator
{
    private readonly DeepSeekClient _deepSeek;
    private readonly DemoAnswerGenerator _demo;
    private readonly ISettingsStore _settingsStore;

    public AnswerGeneratorSelector(DeepSeekClient deepSeek, DemoAnswerGenerator demo, ISettingsStore settingsStore)
    {
        _deepSeek = deepSeek ?? throw new ArgumentNullException(nameof(deepSeek));
        _demo = demo ?? throw new ArgumentNullException(nameof(demo));
        _settingsStore = settingsStore ?? throw new ArgumentNullException(nameof(settingsStore));
    }

    public bool IsAvailable(AppSettings settings) => settings.Mode switch
    {
        AiMode.Demo => true,
        AiMode.DeepSeek => settings.HasApiKey,
        _ => true
    };

    public async Task<Result<string>> GenerateAsync(AgentPrompt prompt, AppSettings settings, CancellationToken ct)
    {
        if (settings.Mode == AiMode.Demo)
            return await _demo.GenerateAsync(prompt, settings, ct).ConfigureAwait(false);

        var keyResult = await _settingsStore.GetApiKeyAsync(ct).ConfigureAwait(false);
        if (!keyResult.IsSuccess)
            return settings.Mode == AiMode.Auto
                ? await DemoFallback(prompt, settings, ct).ConfigureAwait(false)
                : Result<string>.Fail(keyResult.Failure!);
        var hasKey = !string.IsNullOrWhiteSpace(keyResult.Value);
        if (!hasKey)
        {
            return settings.Mode == AiMode.Auto
                ? await DemoFallback(prompt, settings, ct).ConfigureAwait(false)
                : Result<string>.Fail(Failure.AiUnauthorized("未配置 DeepSeek API Key。"));
        }

        var result = await _deepSeek.GenerateAsync(prompt, settings with { HasApiKey = true }, ct).ConfigureAwait(false);
        if (result.IsSuccess || settings.Mode == AiMode.DeepSeek || result.Failure?.Kind == FailureKind.Canceled)
            return result;
        return await DemoFallback(prompt, settings, ct).ConfigureAwait(false);
    }

    private async Task<Result<string>> DemoFallback(AgentPrompt prompt, AppSettings settings, CancellationToken ct)
    {
        var result = await _demo.GenerateAsync(prompt, settings with { Mode = AiMode.Demo }, ct).ConfigureAwait(false);
        return result;
    }
}
