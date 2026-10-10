using System.Security.Cryptography;
using System.Security;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;

namespace WeatherDesk.Infrastructure.Configuration;

public sealed class JsonSettingsStore : ISettingsStore
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private readonly string _settingsPath;
    private readonly string _credentialsPath;
    private readonly ISecretProtector _secretProtector;

    public JsonSettingsStore()
        : this(AppPaths.SettingsPath, AppPaths.CredentialsPath, new SecretProtector())
    {
    }

    public JsonSettingsStore(string settingsPath, string credentialsPath, ISecretProtector secretProtector)
    {
        _settingsPath = settingsPath ?? throw new ArgumentNullException(nameof(settingsPath));
        _credentialsPath = credentialsPath ?? throw new ArgumentNullException(nameof(credentialsPath));
        _secretProtector = secretProtector ?? throw new ArgumentNullException(nameof(secretProtector));
    }

    public async Task<Result<AppSettings>> LoadAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Result<AppSettings>.Fail(Failure.Canceled("操作已取消。"));
        }

        try
        {
            EnsureDirectories();
            var document = new SettingsDocument();
            if (File.Exists(_settingsPath))
            {
                var json = await File.ReadAllTextAsync(_settingsPath, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                try
                {
                    document = JsonSerializer.Deserialize<SettingsDocument>(json, JsonOptions) ?? new SettingsDocument();
                }
                catch (JsonException)
                {
                    document = new SettingsDocument();
                }
            }

            var keyResult = await GetApiKeyAsync(ct).ConfigureAwait(false);
            if (!keyResult.IsSuccess)
            {
                return Result<AppSettings>.Fail(keyResult.Failure!);
            }

            return Result<AppSettings>.Ok(ToSettings(document, !string.IsNullOrEmpty(keyResult.Value)));
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result<AppSettings>.Fail(Failure.Canceled("操作已取消。"));
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return Result<AppSettings>.Fail(StorageFailure());
        }
    }

    public async Task<Result> SaveAsync(AppSettings settings, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Result.Fail(Failure.Canceled("操作已取消。"));
        }

        if (!TryCreateDocument(settings, out var document))
        {
            return Result.Fail(Failure.Input("设置值无效。"));
        }

        var temporaryPath = CreateTemporaryPath(_settingsPath);
        try
        {
            EnsureDirectories();
            var json = JsonSerializer.Serialize(document, JsonOptions);
            await File.WriteAllTextAsync(temporaryPath, json, StrictUtf8, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            File.Move(temporaryPath, _settingsPath, overwrite: true);
            return Result.Ok();
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result.Fail(Failure.Canceled("操作已取消。"));
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return Result.Fail(StorageFailure());
        }
        finally
        {
            TryDelete(temporaryPath);
        }
    }

    public async Task<Result<string?>> GetApiKeyAsync(CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Result<string?>.Fail(Failure.Canceled("操作已取消。"));
        }

        try
        {
            EnsureDirectories();
            ct.ThrowIfCancellationRequested();
            if (!File.Exists(_credentialsPath))
            {
                return Result<string?>.Ok(null);
            }

            var encrypted = await File.ReadAllBytesAsync(_credentialsPath, ct).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            byte[] plaintext;
            try
            {
                plaintext = _secretProtector.Unprotect(encrypted);
                try
                {
                    var apiKey = StrictUtf8.GetString(plaintext);
                    return Result<string?>.Ok(string.IsNullOrWhiteSpace(apiKey) ? null : apiKey);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }
            catch (Exception exception) when (IsProtectionException(exception))
            {
                return Result<string?>.Ok(null);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result<string?>.Fail(Failure.Canceled("操作已取消。"));
        }
        catch (Exception exception) when (IsStorageException(exception))
        {
            return Result<string?>.Fail(StorageFailure());
        }
    }

    public async Task<Result> SetApiKeyAsync(string apiKey, CancellationToken ct)
    {
        if (ct.IsCancellationRequested)
        {
            return Result.Fail(Failure.Canceled("操作已取消。"));
        }

        try
        {
            EnsureDirectories();
            ct.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                File.Delete(_credentialsPath);
                return Result.Ok();
            }

            var plaintext = StrictUtf8.GetBytes(apiKey);
            byte[] encrypted;
            try
            {
                encrypted = _secretProtector.Protect(plaintext);
            }
            finally
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
            ct.ThrowIfCancellationRequested();
            var temporaryPath = CreateTemporaryPath(_credentialsPath);
            try
            {
                await File.WriteAllBytesAsync(temporaryPath, encrypted, ct).ConfigureAwait(false);
                ct.ThrowIfCancellationRequested();
                File.Move(temporaryPath, _credentialsPath, overwrite: true);
                return Result.Ok();
            }
            finally
            {
                TryDelete(temporaryPath);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return Result.Fail(Failure.Canceled("操作已取消。"));
        }
        catch (Exception exception) when (IsStorageException(exception) || IsProtectionException(exception))
        {
            return Result.Fail(StorageFailure());
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
        return options;
    }

    private static AppSettings ToSettings(SettingsDocument document, bool hasApiKey)
    {
        var mode = Enum.IsDefined(document.Mode) ? document.Mode : AiMode.Auto;
        var unit = Enum.IsDefined(document.Unit) ? document.Unit : TemperatureUnit.Celsius;
        var theme = Enum.IsDefined(document.Theme) ? document.Theme : AppTheme.FollowSystem;
        var baseUrl = TryNormalizeBaseUrl(document.DeepSeekBaseUrl, out var normalizedBaseUrl)
            ? normalizedBaseUrl
            : "https://api.deepseek.com";
        var model = string.IsNullOrWhiteSpace(document.DeepSeekModel)
            ? "deepseek-chat"
            : document.DeepSeekModel.Trim();

        return new AppSettings(mode, baseUrl, model, hasApiKey, unit, theme);
    }

    private static bool TryCreateDocument(AppSettings settings, out SettingsDocument document)
    {
        document = new SettingsDocument();
        if (settings is null ||
            !Enum.IsDefined(settings.Mode) ||
            !Enum.IsDefined(settings.Unit) ||
            !Enum.IsDefined(settings.Theme) ||
            string.IsNullOrWhiteSpace(settings.DeepSeekModel) ||
            !TryNormalizeBaseUrl(settings.DeepSeekBaseUrl, out var normalizedBaseUrl))
        {
            return false;
        }

        document = new SettingsDocument
        {
            Mode = settings.Mode,
            DeepSeekBaseUrl = normalizedBaseUrl,
            DeepSeekModel = settings.DeepSeekModel.Trim(),
            Unit = settings.Unit,
            Theme = settings.Theme
        };
        return true;
    }

    private static bool TryNormalizeBaseUrl(string? value, out string normalized)
    {
        normalized = string.Empty;
        if (string.IsNullOrWhiteSpace(value) ||
            !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp) ||
            string.IsNullOrWhiteSpace(uri.Host) ||
            !string.IsNullOrEmpty(uri.UserInfo) ||
            !string.IsNullOrEmpty(uri.Query) ||
            !string.IsNullOrEmpty(uri.Fragment))
        {
            return false;
        }

        normalized = value.Trim().TrimEnd('/');
        return true;
    }

    private void EnsureDirectories()
    {
        var settingsDirectory = Path.GetDirectoryName(_settingsPath);
        var credentialsDirectory = Path.GetDirectoryName(_credentialsPath);
        if (string.IsNullOrWhiteSpace(settingsDirectory) || string.IsNullOrWhiteSpace(credentialsDirectory))
        {
            throw new IOException("A storage directory is unavailable.");
        }

        Directory.CreateDirectory(settingsDirectory);
        Directory.CreateDirectory(credentialsDirectory);
    }

    private static string CreateTemporaryPath(string destinationPath) =>
        $"{destinationPath}.{Guid.NewGuid():N}.tmp";

    private static void TryDelete(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
        catch (ArgumentException)
        {
        }
        catch (NotSupportedException)
        {
        }
        catch (SecurityException)
        {
        }
    }

    private static bool IsStorageException(Exception exception) =>
        exception is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or InvalidOperationException or JsonException or SecurityException;

    private static bool IsProtectionException(Exception exception) =>
        exception is CryptographicException or PlatformNotSupportedException or
            ArgumentException or InvalidOperationException or DecoderFallbackException;

    private static Failure StorageFailure() => Failure.Storage("本地设置存储不可用。");

    private sealed class SettingsDocument
    {
        public AiMode Mode { get; set; } = AiMode.Auto;

        public string DeepSeekBaseUrl { get; set; } = "https://api.deepseek.com";

        public string DeepSeekModel { get; set; } = "deepseek-chat";

        public TemperatureUnit Unit { get; set; } = TemperatureUnit.Celsius;

        public AppTheme Theme { get; set; } = AppTheme.FollowSystem;
    }
}
