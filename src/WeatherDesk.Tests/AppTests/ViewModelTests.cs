using Microsoft.Extensions.Logging.Abstractions;
using WeatherDesk.App.ViewModels;
using WeatherDesk.App.Search;
using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;
using WeatherDesk.Domain.Services;

namespace WeatherDesk.Tests.AppTests;

public sealed class ViewModelTests
{
    [Fact]
    public async Task Weather_formats_temperature_using_selected_unit_and_city_offset()
    {
        var city = new Location("1", "上海", null, "中国", "CN", 31.2, 121.5, "Asia/Shanghai", 8 * 3600);
        var snapshot = new WeatherSnapshot(city,
            new CurrentWeather(new DateTimeOffset(2026, 10, 8, 4, 0, 0, TimeSpan.Zero), 20, 20, 0, 50, 10, 12, 1010, null, true),
            Array.Empty<HourlyForecast>(), Array.Empty<DailyForecast>(),
            new DateTimeOffset(2026, 10, 8, 4, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 8, 4, 0, 0, TimeSpan.Zero), "Open-Meteo");
        var query = new FakeWeatherQueryService(snapshot);
        var vm = new WeatherViewModel(query, NullLogger<WeatherViewModel>.Instance) { Unit = TemperatureUnit.Fahrenheit };

        await vm.SetCityAsync(city);

        Assert.Equal("68.0 °F", vm.CurrentTemperature);
        Assert.Contains("12:00", vm.ObservedAtText);
        Assert.Contains("晴", vm.ConditionText);
    }

    [Fact]
    public async Task Weather_source_text_does_not_expose_provider_name()
    {
        var city = new Location("1", "上海", null, "中国", "CN", 31.2, 121.5, "Asia/Shanghai", 8 * 3600);
        var snapshot = new WeatherSnapshot(city, null, Array.Empty<HourlyForecast>(), Array.Empty<DailyForecast>(),
            new DateTimeOffset(2026, 10, 10, 0, 15, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 10, 10, 0, 15, 0, TimeSpan.Zero), "Open-Meteo");
        var vm = new WeatherViewModel(new FakeWeatherQueryService(snapshot), NullLogger<WeatherViewModel>.Instance);

        await vm.SetCityAsync(city);

        Assert.DoesNotContain("Open-Meteo", vm.SourceText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Main_window_restores_last_city_on_initialize()
    {
        var city = new Location("1", "上海", null, "中国", "CN", 31.2, 121.5, "Asia/Shanghai", 8 * 3600);
        var state = new FakeAppStateStore(city);
        var snapshot = new WeatherSnapshot(city, null, Array.Empty<HourlyForecast>(), Array.Empty<DailyForecast>(), DateTimeOffset.UtcNow, DateTimeOffset.UtcNow, "Open-Meteo");
        var weather = new WeatherViewModel(new FakeWeatherQueryService(snapshot), NullLogger<WeatherViewModel>.Instance);
        var search = new CitySearchViewModel(new EmptyGeocoding(), NullLogger<CitySearchViewModel>.Instance);
        var lists = new CityListViewModel(new EmptyCityStore(), NullLogger<CityListViewModel>.Instance);
        var chat = new ChatViewModel(new EmptyAgent(), new FixedClock(), NullLogger<ChatViewModel>.Instance);
        var settings = new SettingsViewModel(new FakeSettingsStore(null), NullLogger<SettingsViewModel>.Instance);
        var main = new MainWindowViewModel(weather, search, lists, chat, settings, state, NullLogger<MainWindowViewModel>.Instance);

        await main.InitializeAsync();

        Assert.Equal(city, main.CurrentCity);
        Assert.Equal(city, weather.City);
        main.OpenSettings();
        Assert.True(main.IsSettingsOpen);
        main.CloseSettings();
        Assert.False(main.IsSettingsOpen);
    }

    [Fact]
    public async Task City_search_includes_local_prefix_matches_when_provider_returns_none()
    {
        var vm = new CitySearchViewModel(new EmptyGeocoding(), NullLogger<CitySearchViewModel>.Instance)
        {
            Keyword = "武"
        };

        await vm.SuggestAsync();

        Assert.Contains(vm.Results, location => location.Name == "武汉");
        Assert.Contains(vm.Results, location => location.Name == "武昌");
        Assert.Contains(vm.Results, location => location.SearchDisplayName == "武汉市 湖北省");
        Assert.Contains(vm.Results, location => location.SearchDisplayName == "武昌区 湖北省");
        Assert.True(vm.Results.Count <= 8);
    }

    [Fact]
    public async Task City_search_deduplicates_local_and_provider_match_for_wuhan()
    {
        var provider = new FixedGeocoding(new[]
        {
            new Location("provider-wuhan", "武汉", "湖北", "中国", "CN", 30.59, 114.30, "Asia/Shanghai", 28800)
        });
        var vm = new CitySearchViewModel(provider, NullLogger<CitySearchViewModel>.Instance)
        {
            Keyword = "武"
        };

        await vm.SuggestAsync();

        Assert.Equal(1, vm.Results.Count(location => location.Name == "武汉"));
        Assert.Contains(vm.Results, location => location.Name == "武昌");
    }

    [Fact]
    public async Task Offline_city_resolution_keeps_matching_province_for_same_name()
    {
        var provider = new FixedGeocoding(new[]
        {
            new Location("haikou-yunnan", "海口", "云南", "中国", "CN", 25.0, 100.0, "Asia/Shanghai", 28800),
            new Location("haikou-hainan", "海口", "海南", "中国", "CN", 20.0, 110.0, "Asia/Shanghai", 28800)
        });
        var vm = new CitySearchViewModel(provider, NullLogger<CitySearchViewModel>.Instance);
        var resolved = await vm.ResolveAsync(new Location("offline-haikou", "海口", "海南", "中国", "CN", 0, 0, "Asia/Shanghai", 28800));

        Assert.Equal("海南", resolved.Admin1);
        Assert.Equal(20.0, resolved.Latitude);
    }

    [Fact]
    public async Task Offline_city_resolution_does_not_use_another_province_when_match_is_missing()
    {
        var provider = new FixedGeocoding(new[]
        {
            new Location("haikou-yunnan", "海口", "云南", "中国", "CN", 25.0, 100.0, "Asia/Shanghai", 28800)
        });
        var vm = new CitySearchViewModel(provider, NullLogger<CitySearchViewModel>.Instance);
        var selected = new Location("offline-haikou", "海口", "海南", "中国", "CN", 0, 0, "Asia/Shanghai", 28800);

        Assert.Equal(selected, await vm.ResolveAsync(selected));
    }

    [Fact]
    public async Task Offline_city_resolution_accepts_single_name_match_when_provider_uses_suffix()
    {
        var provider = new FixedGeocoding(new[]
        {
            new Location("haikou", "海口市", "海南省", "中国", "CN", 20.0, 110.0, "Asia/Shanghai", 28800)
        });
        var vm = new CitySearchViewModel(provider, NullLogger<CitySearchViewModel>.Instance);
        var resolved = await vm.ResolveAsync(new Location("offline-haikou", "海口", "海南", "中国", "CN", 0, 0, "Asia/Shanghai", 28800));

        Assert.Equal(20.0, resolved.Latitude);
    }

    [Fact]
    public async Task Offline_city_resolution_matches_english_province_suffix()
    {
        var provider = new FixedGeocoding(new[]
        {
            new Location("wuan", "武安", "Hebei Sheng", "中国", "CN", 36.7, 114.2, "Asia/Shanghai", 28800)
        });
        var vm = new CitySearchViewModel(provider, NullLogger<CitySearchViewModel>.Instance);
        var resolved = await vm.ResolveAsync(new Location("offline-wuan", "武安", "河北", "中国", "CN", 0, 0, "Asia/Shanghai", 28800));

        Assert.Equal(36.7, resolved.Latitude);
    }

    [Fact]
    public async Task Offline_city_resolution_merges_bare_name_results_when_province_query_is_empty()
    {
        var provider = new QueryGeocoding(new Dictionary<string, IReadOnlyList<Location>>(StringComparer.OrdinalIgnoreCase)
        {
            ["海口"] = new[] { new Location("haikou-hainan", "海口市", "海南", "中国", "CN", 20.03, 110.35, "Asia/Shanghai", 28800) },
            ["海口 海南"] = Array.Empty<Location>()
        });
        var vm = new CitySearchViewModel(provider, NullLogger<CitySearchViewModel>.Instance);
        var resolved = await vm.ResolveAsync(new Location("offline-haikou", "海口", "海南", "中国", "CN", 0, 0, "Asia/Shanghai", 28800));

        Assert.Equal(20.03, resolved.Latitude);
        Assert.Equal(110.35, resolved.Longitude);
    }

    [Fact]
    public async Task Offline_city_resolution_uses_catalog_pinyin_to_find_correct_city()
    {
        var provider = new QueryGeocoding(new Dictionary<string, IReadOnlyList<Location>>(StringComparer.OrdinalIgnoreCase)
        {
            ["海口"] = new[] { new Location("wrong-haikou", "海口", "云南", "中国", "CN", 24.78, 102.58, "Asia/Shanghai", 28800) },
            ["Haikou"] = new[] { new Location("haikou-city", "海口市", "海南", "中国", "CN", 20.03421, 110.34651, "Asia/Shanghai", 28800) }
        });
        var vm = new CitySearchViewModel(provider, NullLogger<CitySearchViewModel>.Instance);
        var offlineHaikou = new Location("offline-haikou", "海口", "海南", "中国", "CN", 0, 0, "Asia/Shanghai", 28800)
        {
            GeocodingName = "Haikou",
            GeocodingNameHyphenated = "Hai-kou"
        };

        var resolved = await vm.ResolveAsync(offlineHaikou);

        Assert.Equal("海南", resolved.Admin1);
        Assert.Equal(20.03421, resolved.Latitude);
        Assert.Equal(110.34651, resolved.Longitude);
    }

    [Fact]
    public async Task Offline_catalog_city_keeps_pinyin_for_real_csv_candidate()
    {
        var offlineHaikou = OfflinePlaceCatalog.All.Single(location =>
            location.Name == "海口" && location.Admin1 == "海南" && location.AdministrativeLevel == AdministrativeLevel.PrefectureCity);
        Assert.Equal("haikou", offlineHaikou.GeocodingName);
        Assert.Equal("hai-kou", offlineHaikou.GeocodingNameHyphenated);

        var provider = new QueryGeocoding(new Dictionary<string, IReadOnlyList<Location>>(StringComparer.OrdinalIgnoreCase)
        {
            ["haikou"] = new[] { new Location("haikou-city", "海口市", "海南", "中国", "CN", 20.03421, 110.34651, "Asia/Shanghai", 28800) }
        });
        var vm = new CitySearchViewModel(provider, NullLogger<CitySearchViewModel>.Instance);

        var resolved = await vm.ResolveAsync(offlineHaikou);

        Assert.Equal(20.03421, resolved.Latitude);
        Assert.Equal("海南", resolved.Admin1);
    }

    [Fact]
    public async Task Offline_city_resolution_tries_administrative_suffix_for_exact_provider_match()
    {
        var provider = new QueryGeocoding(new Dictionary<string, IReadOnlyList<Location>>(StringComparer.OrdinalIgnoreCase)
        {
            ["海口市"] = new[] { new Location("haikou-city", "海口市", "海南", "中国", "CN", 20.03421, 110.34651, "Asia/Shanghai", 28800) }
        });
        var vm = new CitySearchViewModel(provider, NullLogger<CitySearchViewModel>.Instance);

        var resolved = await vm.ResolveAsync(new Location("offline-haikou", "海口", "海南", "中国", "CN", 0, 0, "Asia/Shanghai", 28800)
        {
            AdministrativeLevel = AdministrativeLevel.PrefectureCity
        });

        Assert.Equal(20.03421, resolved.Latitude);
        Assert.Equal("海南", resolved.Admin1);
    }

    [Fact]
    public async Task District_resolution_falls_back_to_same_province_parent_city()
    {
        var provider = new QueryGeocoding(new Dictionary<string, IReadOnlyList<Location>>(StringComparer.OrdinalIgnoreCase)
        {
            ["武昌"] = new[] { new Location("wuchang-fujian", "武昌", "福建", "中国", "CN", 26.3, 116.7, "Asia/Shanghai", 28800) },
            ["武汉"] = new[] { new Location("wuhan", "武汉", "湖北", "中国", "CN", 30.58, 114.26, "Asia/Shanghai", 28800) }
        });
        var vm = new CitySearchViewModel(provider, NullLogger<CitySearchViewModel>.Instance);
        var district = new Location("offline-wuchang", "武昌", "湖北", "中国", "CN", 0, 0, "Asia/Shanghai", 28800)
        {
            Admin2 = "武汉",
            AdministrativeLevel = AdministrativeLevel.District
        };

        var resolved = await vm.ResolveAsync(district);

        Assert.Equal(30.58, resolved.Latitude);
        Assert.Equal("武昌", resolved.Name);
        Assert.Equal("湖北", resolved.Admin1);
    }

    [Fact]
    public async Task City_search_supports_single_character_prefix_and_deduplicates_same_name_everywhere()
    {
        var emptyProvider = new EmptyGeocoding();
        var prefixVm = new CitySearchViewModel(emptyProvider, NullLogger<CitySearchViewModel>.Instance)
        {
            Keyword = "南"
        };
        await prefixVm.SuggestAsync();

        Assert.NotEmpty(prefixVm.Results);
        Assert.All(prefixVm.Results, location => Assert.StartsWith("南", location.Name));

        var duplicateProvider = new FixedGeocoding(new[]
        {
            new Location("nanjing-jiangsu", "南京", "江苏", "中国", "CN", 32.06, 118.79, "Asia/Shanghai", 28800),
            new Location("nanjing-yunnan", "南京", "云南", "中国", "CN", 23.40, 99.77, "Asia/Shanghai", 28800)
        });
        var duplicateVm = new CitySearchViewModel(duplicateProvider, NullLogger<CitySearchViewModel>.Instance)
        {
            Keyword = "南京"
        };
        await duplicateVm.SuggestAsync();

        Assert.Equal(1, duplicateVm.Results.Count(location => location.Name == "南京"));
    }

    [Fact]
    public void Offline_catalog_preserves_autonomous_prefecture_name_and_level()
    {
        var haibei = OfflinePlaceCatalog.All.Single(location =>
            location.Name == "海北" && location.Admin1 == "青海");

        Assert.Equal(AdministrativeLevel.PrefectureRegion, haibei.AdministrativeLevel);
        Assert.Equal("藏族自治州", haibei.AdministrativeSuffix);
        Assert.Equal("海北藏族自治州 青海省", haibei.SearchDisplayName);

        var autonomousCounty = OfflinePlaceCatalog.All.Single(location =>
            location.Name == "河南" && location.Admin1 == "青海");
        Assert.Equal("蒙古族自治县", autonomousCounty.AdministrativeSuffix);
        Assert.Equal("河南蒙古族自治县 青海省", autonomousCounty.SearchDisplayName);
    }
    [Fact]
    public async Task Settings_clear_command_removes_key_and_masks_value()
    {
        var store = new FakeSettingsStore("secret-key");
        var vm = new SettingsViewModel(store, NullLogger<SettingsViewModel>.Instance);
        await vm.LoadAsync();

        Assert.NotEmpty(vm.MaskedApiKey);
        await vm.ClearApiKeyCommand.ExecuteAsync(null);

        Assert.Null(store.Key);
        Assert.Equal(string.Empty, vm.MaskedApiKey);
    }

    [Fact]
    public void Chat_messages_expose_distinct_speaker_labels()
    {
        var user = new ChatMessage { IsUser = true, Text = "天气怎么样" };
        var assistant = new ChatMessage { IsUser = false, Text = "适合出行" };

        Assert.Equal("我", user.SpeakerText);
        Assert.Equal("AI", assistant.SpeakerText);
    }

    [Fact]
    public void Chat_quick_questions_are_removed()
    {
        var chat = new ChatViewModel(new EmptyAgent(), new FixedClock(), NullLogger<ChatViewModel>.Instance);

        Assert.Empty(chat.QuickQuestions);
    }

    [Fact]
    public async Task Settings_save_command_persists_preferences_and_key()
    {
        var store = new FakeSettingsStore(null);
        var vm = new SettingsViewModel(store, NullLogger<SettingsViewModel>.Instance)
        {
            BaseUrl = "https://api.example.test",
            Model = "demo-model",
            ApiKeyInput = "new-key",
            Mode = AiMode.DeepSeek,
            Unit = TemperatureUnit.Fahrenheit,
            Theme = AppTheme.Dark
        };

        await vm.SaveCommand.ExecuteAsync(null);

        Assert.Equal("new-key", store.Key);
        Assert.Equal(AiMode.DeepSeek, store.Settings.Mode);
        Assert.Equal(TemperatureUnit.Fahrenheit, store.Settings.Unit);
        Assert.Equal(AppTheme.Dark, store.Settings.Theme);
    }

    private sealed class FakeSettingsStore(string? key) : ISettingsStore
    {
        public string? Key { get; private set; } = key;
        public AppSettings Settings { get; private set; } = new(AiMode.Auto, "https://api.deepseek.com", "deepseek-chat", key is not null, TemperatureUnit.Celsius, AppTheme.FollowSystem);
        public Task<Result<AppSettings>> LoadAsync(CancellationToken ct) => Task.FromResult(Result<AppSettings>.Ok(Settings));
        public Task<Result> SaveAsync(AppSettings settings, CancellationToken ct) { Settings = settings; return Task.FromResult(Result.Ok()); }
        public Task<Result<string?>> GetApiKeyAsync(CancellationToken ct) => Task.FromResult(Result<string?>.Ok(Key));
        public Task<Result> SetApiKeyAsync(string apiKey, CancellationToken ct) { Key = string.IsNullOrWhiteSpace(apiKey) ? null : apiKey; return Task.FromResult(Result.Ok()); }
    }

    private sealed class FakeWeatherQueryService(WeatherSnapshot snapshot) : WeatherQueryService
    {
        public override Task<Result<WeatherQueryResult>> LoadAsync(Location location, DateOnly? date, CancellationToken ct)
            => Task.FromResult(Result<WeatherQueryResult>.Ok(new WeatherQueryResult(snapshot, false, snapshot.DataUpdatedAtUtc, null)));
        public override Task<Result<WeatherQueryResult>> RefreshAsync(Location location, DateOnly? date, CancellationToken ct) => LoadAsync(location, date, ct);
    }

    private sealed class EmptyGeocoding : IGeocodingService
    {
        public Task<Result<IReadOnlyList<Location>>> SearchAsync(string keyword, CancellationToken ct) => Task.FromResult(Result<IReadOnlyList<Location>>.Ok(Array.Empty<Location>()));
    }

    private sealed class FixedGeocoding(IReadOnlyList<Location> locations) : IGeocodingService
    {
        public Task<Result<IReadOnlyList<Location>>> SearchAsync(string keyword, CancellationToken ct) =>
            Task.FromResult(Result<IReadOnlyList<Location>>.Ok(locations));
    }

    private sealed class QueryGeocoding(IReadOnlyDictionary<string, IReadOnlyList<Location>> results) : IGeocodingService
    {
        public Task<Result<IReadOnlyList<Location>>> SearchAsync(string keyword, CancellationToken ct) =>
            Task.FromResult(Result<IReadOnlyList<Location>>.Ok(results.TryGetValue(keyword, out var value) ? value : Array.Empty<Location>()));
    }

    private sealed class EmptyCityStore : ICityStore
    {
        public Task<Result<IReadOnlyList<Location>>> GetFavoritesAsync(CancellationToken ct) => Task.FromResult(Result<IReadOnlyList<Location>>.Ok(Array.Empty<Location>()));
        public Task<Result> AddFavoriteAsync(Location location, CancellationToken ct) => Task.FromResult(Result.Ok());
        public Task<Result> RemoveFavoriteAsync(string locationId, CancellationToken ct) => Task.FromResult(Result.Ok());
        public Task<Result<IReadOnlyList<Location>>> GetRecentAsync(CancellationToken ct) => Task.FromResult(Result<IReadOnlyList<Location>>.Ok(Array.Empty<Location>()));
        public Task<Result> UpsertRecentAsync(Location location, DateTimeOffset visitedAtUtc, CancellationToken ct) => Task.FromResult(Result.Ok());
    }

    private sealed class EmptyAgent : IWeatherAgent
    {
        public Task<Result<AgentReply>> AskAsync(string question, AgentContext context, CancellationToken ct) => Task.FromResult(Result<AgentReply>.Ok(new AgentReply(AgentReplyKind.OutOfScope, "", null, null, AgentIntent.Unknown, Array.Empty<string>(), null)));
    }

    private sealed class FixedClock : IClock
    {
        public DateTimeOffset UtcNow => new(2026, 10, 8, 0, 0, 0, TimeSpan.Zero);
        public DateOnly TodayIn(TimeSpan utcOffset) => DateOnly.FromDateTime(UtcNow.ToOffset(utcOffset).DateTime);
        public DateTimeOffset NowIn(TimeSpan utcOffset) => UtcNow.ToOffset(utcOffset);
    }

    private sealed class FakeAppStateStore(Location? city) : IAppStateStore
    {
        public Task<Result<Location?>> GetLastCityAsync(CancellationToken ct) => Task.FromResult(Result<Location?>.Ok(city));
        public Task<Result> SetLastCityAsync(Location location, CancellationToken ct) => Task.FromResult(Result.Ok());
    }
}
