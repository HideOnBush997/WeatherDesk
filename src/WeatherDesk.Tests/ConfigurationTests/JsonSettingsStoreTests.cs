using System.Security.Cryptography;
using System.Text;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;
using WeatherDesk.Infrastructure.Configuration;

namespace WeatherDesk.Tests.ConfigurationTests;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task Load_returns_defaults_when_the_settings_file_does_not_exist()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(new AppSettings(
            AiMode.Auto,
            "https://api.deepseek.com",
            "deepseek-chat",
            false,
            TemperatureUnit.Celsius,
            AppTheme.FollowSystem), result.Value);
    }

    [Fact]
    public async Task Load_falls_back_to_defaults_when_the_settings_json_is_malformed()
    {
        using var directory = new TemporaryDirectory();
        var paths = Paths(directory.Path);
        Directory.CreateDirectory(paths.RootDirectory);
        await File.WriteAllTextAsync(paths.SettingsPath, "{ malformed");
        var store = CreateStore(directory.Path);

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AiMode.Auto, result.Value!.Mode);
        Assert.Equal("https://api.deepseek.com", result.Value.DeepSeekBaseUrl);
        Assert.Equal("deepseek-chat", result.Value.DeepSeekModel);
        Assert.Equal(TemperatureUnit.Celsius, result.Value.Unit);
        Assert.Equal(AppTheme.FollowSystem, result.Value.Theme);
    }

    [Fact]
    public async Task Load_uses_defaults_for_missing_settings_fields()
    {
        using var directory = new TemporaryDirectory();
        var paths = Paths(directory.Path);
        Directory.CreateDirectory(paths.RootDirectory);
        await File.WriteAllTextAsync(paths.SettingsPath, "{\"Mode\":\"Demo\"}");
        var store = CreateStore(directory.Path);

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(AiMode.Demo, result.Value!.Mode);
        Assert.Equal("https://api.deepseek.com", result.Value.DeepSeekBaseUrl);
        Assert.Equal("deepseek-chat", result.Value.DeepSeekModel);
        Assert.Equal(TemperatureUnit.Celsius, result.Value.Unit);
        Assert.Equal(AppTheme.FollowSystem, result.Value.Theme);
    }

    [Fact]
    public async Task Save_and_load_round_trip_settings_without_serializing_the_api_key()
    {
        using var directory = new TemporaryDirectory();
        var protector = new TestSecretProtector();
        var store = CreateStore(directory.Path, protector);
        const string apiKey = "private-credential-value";
        var settings = new AppSettings(
            AiMode.DeepSeek,
            "https://proxy.example.test/deepseek",
            "weather-model",
            false,
            TemperatureUnit.Fahrenheit,
            AppTheme.Dark);

        var saveResult = await store.SaveAsync(settings, CancellationToken.None);
        var keyResult = await store.SetApiKeyAsync(apiKey, CancellationToken.None);
        var loadResult = await store.LoadAsync(CancellationToken.None);

        Assert.True(saveResult.IsSuccess);
        Assert.True(keyResult.IsSuccess);
        Assert.True(loadResult.IsSuccess);
        Assert.Equal(settings with { HasApiKey = true }, loadResult.Value);
        var json = await File.ReadAllTextAsync(Paths(directory.Path).SettingsPath);
        Assert.DoesNotContain(apiKey, json, StringComparison.Ordinal);
        Assert.DoesNotContain("HasApiKey", json, StringComparison.Ordinal);
        var encryptedCredentials = await File.ReadAllBytesAsync(Paths(directory.Path).CredentialsPath);
        Assert.DoesNotContain(apiKey, Encoding.UTF8.GetString(encryptedCredentials), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Load_ignores_a_serialized_api_key_flag_and_derives_it_from_credentials()
    {
        using var directory = new TemporaryDirectory();
        var paths = Paths(directory.Path);
        Directory.CreateDirectory(paths.RootDirectory);
        await File.WriteAllTextAsync(paths.SettingsPath, "{\"HasApiKey\":true}");
        var store = CreateStore(directory.Path);

        var result = await store.LoadAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value!.HasApiKey);
    }

    [Fact]
    public async Task Save_rejects_invalid_endpoint_without_writing_it()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        var invalid = new AppSettings(AiMode.Auto, "file:///unsafe", "model", false,
            TemperatureUnit.Celsius, AppTheme.Light);

        var result = await store.SaveAsync(invalid, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Input, result.Failure!.Kind);
        Assert.False(File.Exists(Paths(directory.Path).SettingsPath));
    }

    [Fact]
    public async Task Save_rejects_undefined_enum_values_without_writing_settings()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        var invalid = new AppSettings((AiMode)99, "https://api.deepseek.com", "model", false,
            TemperatureUnit.Celsius, AppTheme.Light);

        var result = await store.SaveAsync(invalid, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Input, result.Failure!.Kind);
    }

    [Fact]
    public async Task Set_get_and_clear_api_key_round_trip_through_the_protector()
    {
        using var directory = new TemporaryDirectory();
        var protector = new TestSecretProtector();
        var store = CreateStore(directory.Path, protector);
        const string apiKey = "test-credential-value";

        var setResult = await store.SetApiKeyAsync(apiKey, CancellationToken.None);
        var getResult = await store.GetApiKeyAsync(CancellationToken.None);
        var clearResult = await store.SetApiKeyAsync(" ", CancellationToken.None);
        var clearedResult = await store.GetApiKeyAsync(CancellationToken.None);

        Assert.True(setResult.IsSuccess);
        Assert.Equal(apiKey, getResult.Value);
        Assert.True(clearResult.IsSuccess);
        Assert.Null(clearedResult.Value);
        Assert.False(File.Exists(Paths(directory.Path).CredentialsPath));
    }

    [Fact]
    public async Task GetApiKey_treats_corrupt_ciphertext_as_unconfigured()
    {
        using var directory = new TemporaryDirectory();
        var paths = Paths(directory.Path);
        Directory.CreateDirectory(paths.RootDirectory);
        await File.WriteAllBytesAsync(paths.CredentialsPath, [1, 2, 3, 4]);
        var store = CreateStore(directory.Path, new TestSecretProtector(throwOnUnprotect: true));

        var result = await store.GetApiKeyAsync(CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value);
    }

    [Fact]
    public async Task SetApiKey_maps_protection_errors_to_safe_storage_failures()
    {
        using var directory = new TemporaryDirectory();
        const string apiKey = "must-not-leak";
        var store = CreateStore(directory.Path, new TestSecretProtector(throwOnProtect: true));

        var result = await store.SetApiKeyAsync(apiKey, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Storage, result.Failure!.Kind);
        Assert.DoesNotContain(apiKey, result.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SetApiKey_does_not_write_after_cancellation_during_protection()
    {
        using var directory = new TemporaryDirectory();
        using var cancellation = new CancellationTokenSource();
        var store = CreateStore(directory.Path,
            new TestSecretProtector(onProtect: cancellation.Cancel));

        var result = await store.SetApiKeyAsync("test-credential-value", cancellation.Token);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Canceled, result.Failure!.Kind);
        Assert.False(File.Exists(Paths(directory.Path).CredentialsPath));
    }

    [Fact]
    public async Task Settings_operations_map_directory_errors_to_storage_failures()
    {
        using var directory = new TemporaryDirectory();
        var blocker = System.IO.Path.Combine(directory.Path, "file-not-directory");
        await File.WriteAllTextAsync(blocker, "x");
        var store = new JsonSettingsStore(
            System.IO.Path.Combine(blocker, "settings.json"),
            System.IO.Path.Combine(blocker, "credentials.dat"),
            new TestSecretProtector());

        var result = await store.SaveAsync(new AppSettings(AiMode.Auto,
            "https://api.deepseek.com", "deepseek-chat", false,
            TemperatureUnit.Celsius, AppTheme.FollowSystem), CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal(FailureKind.Storage, result.Failure!.Kind);
        Assert.DoesNotContain(blocker, result.Failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Canceled_operations_return_canceled_results()
    {
        using var directory = new TemporaryDirectory();
        var store = CreateStore(directory.Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var load = await store.LoadAsync(cancellation.Token);
        var save = await store.SaveAsync(new AppSettings(AiMode.Auto,
            "https://api.deepseek.com", "deepseek-chat", false,
            TemperatureUnit.Celsius, AppTheme.FollowSystem), cancellation.Token);
        var getKey = await store.GetApiKeyAsync(cancellation.Token);
        var setKey = await store.SetApiKeyAsync("not-stored", cancellation.Token);

        Assert.Equal(FailureKind.Canceled, load.Failure!.Kind);
        Assert.Equal(FailureKind.Canceled, save.Failure!.Kind);
        Assert.Equal(FailureKind.Canceled, getKey.Failure!.Kind);
        Assert.Equal(FailureKind.Canceled, setKey.Failure!.Kind);
    }

    private static JsonSettingsStore CreateStore(string root, ISecretProtector? protector = null)
    {
        var paths = Paths(root);
        return new JsonSettingsStore(paths.SettingsPath, paths.CredentialsPath,
            protector ?? new TestSecretProtector());
    }

    private static (string RootDirectory, string SettingsPath, string CredentialsPath) Paths(string root) =>
        (root, System.IO.Path.Combine(root, "settings.json"), System.IO.Path.Combine(root, "credentials.dat"));

    private sealed class TestSecretProtector(
        bool throwOnProtect = false,
        bool throwOnUnprotect = false,
        Action? onProtect = null)
        : ISecretProtector
    {
        public byte[] Protect(ReadOnlySpan<byte> plaintext)
        {
            if (throwOnProtect)
            {
                throw new CryptographicException("test failure");
            }

            onProtect?.Invoke();
            return plaintext.ToArray().Select(value => (byte)(value ^ 0xA5)).ToArray();
        }

        public byte[] Unprotect(ReadOnlySpan<byte> protectedData)
        {
            if (throwOnUnprotect)
            {
                throw new CryptographicException("test failure");
            }

            return protectedData.ToArray().Select(value => (byte)(value ^ 0xA5)).ToArray();
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public TemporaryDirectory()
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }
}
