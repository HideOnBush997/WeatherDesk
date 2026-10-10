using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.Logging;
using WeatherDesk.Domain.Interfaces;
using WeatherDesk.Domain.Models;
using WeatherDesk.Domain.Results;
using WeatherDesk.Domain.Rules;
using WeatherDesk.Domain.Services;
using WeatherDesk.Infrastructure.Configuration;
using WeatherDesk.App.Search;

namespace WeatherDesk.App.ViewModels;

public sealed record HourlyWeatherDisplay(string DateTimeText, string TemperatureText, string ConditionText);
public sealed record DailyWeatherDisplay(string DateText, string TemperatureText, string ConditionText);

public abstract class ViewModelBase : ObservableObject
{
    protected readonly ILogger Logger;
    private CancellationTokenSource? _operation;

    protected ViewModelBase(ILogger logger) => Logger = logger;

    protected async Task RunExclusiveAsync(Func<CancellationToken, Task> action)
    {
        _operation?.Cancel();
        _operation?.Dispose();
        _operation = new CancellationTokenSource();
        try { await action(_operation.Token); }
        catch (OperationCanceledException) when (_operation.IsCancellationRequested) { }
    }
}

public sealed class WeatherViewModel : ViewModelBase
{
    private readonly WeatherQueryService _query;
    private Location? _city;
    private WeatherQueryResult? _result;
    private AsyncStatus _status = AsyncStatus.Idle;
    private string _statusMessage = "搜索城市以开始。";
    private TemperatureUnit _unit = TemperatureUnit.Celsius;
    public IReadOnlyList<HourlyWeatherDisplay> HourlyItems => Snapshot?.Hourly.Select(hour => new HourlyWeatherDisplay(
        FormatLocal(hour.HourUtc),
        UnitConverter.FormatTemperature(hour.TemperatureC, Unit),
        WeatherCodeCatalog.Describe(hour.WeatherCode))).ToArray() ?? Array.Empty<HourlyWeatherDisplay>();
    public IReadOnlyList<DailyWeatherDisplay> DailyItems => Snapshot?.Daily.Take(7).Select(day => new DailyWeatherDisplay(
        day.Date.ToString("MM-dd ddd", System.Globalization.CultureInfo.InvariantCulture),
        $"{UnitConverter.FormatTemperature(day.MinTemperatureC, Unit)} / {UnitConverter.FormatTemperature(day.MaxTemperatureC, Unit)}",
        WeatherCodeCatalog.Describe(day.WeatherCode))).ToArray() ?? Array.Empty<DailyWeatherDisplay>();

    public WeatherViewModel(WeatherQueryService query, ILogger<WeatherViewModel> logger) : base(logger) => _query = query;
    public Location? City { get => _city; private set => SetProperty(ref _city, value); }
    public TemperatureUnit Unit { get => _unit; set { if (SetProperty(ref _unit, value)) { OnPropertyChanged(nameof(HourlyItems)); OnPropertyChanged(nameof(DailyItems)); OnPropertyChanged(nameof(CurrentTemperature)); OnPropertyChanged(nameof(DailyTemperatureSummary)); } } }
    public WeatherQueryResult? Result { get => _result; private set { SetProperty(ref _result, value); OnPropertyChanged(nameof(Snapshot)); OnPropertyChanged(nameof(HourlyItems)); OnPropertyChanged(nameof(DailyItems)); OnPropertyChanged(nameof(CurrentTemperature)); OnPropertyChanged(nameof(SourceText)); OnPropertyChanged(nameof(ObservedAtText)); OnPropertyChanged(nameof(ConditionText)); OnPropertyChanged(nameof(DailyTemperatureSummary)); } }
    public WeatherSnapshot? Snapshot => Result?.Snapshot;
    public AsyncStatus Status { get => _status; private set => SetProperty(ref _status, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public bool IsSettingsOpen { get; private set; }
    public string CurrentTemperature => Snapshot?.Current is { } current ? UnitConverter.FormatTemperature(current.TemperatureC, Unit) : "暂无";
    public string ObservedAtText => Snapshot?.Current is { } current ? FormatLocal(current.ObservedAtUtc) : "暂无更新时间";
    public string ConditionText => Snapshot?.Current is { } current ? WeatherCodeCatalog.Describe(current.WeatherCode) : "暂无天气描述";
    public string DailyTemperatureSummary => Snapshot?.Daily is { Count: > 0 } daily
        ? string.Join(" / ", daily.Take(7).Select(day => $"{day.Date:MM-dd} {UnitConverter.FormatTemperature(day.MinTemperatureC, Unit)}~{UnitConverter.FormatTemperature(day.MaxTemperatureC, Unit)}"))
        : "暂无预报";
    public string SourceText => Result is null
        ? ""
        : Result.IsStale || Snapshot is null || Snapshot.Provenance == WeatherProvenance.Cache
            ? "缓存数据 · 可能已过期"
            : $"数据更新时间 · {Snapshot.DataUpdatedAtUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
    public IAsyncRelayCommand RetryCommand => new AsyncRelayCommand(() => LoadAsync(true));

    public Task SetCityAsync(Location city) { City = city; return LoadAsync(false); }
    public async Task LoadAsync(bool refresh)
    {
        if (City is null) { Status = AsyncStatus.Empty; StatusMessage = "尚未选择城市。"; return; }
        await RunExclusiveAsync(async ct => {
            var previous = Status; Status = AsyncStatus.Loading; StatusMessage = "正在获取天气…";
            var result = refresh ? await _query.RefreshAsync(City, null, ct) : await _query.LoadAsync(City, null, ct);
            if (result.IsSuccess) { Result = result.Value; Status = result.Value!.IsStale ? AsyncStatus.Stale : AsyncStatus.Success; StatusMessage = result.Value.IsStale ? "网络不可用，显示最近缓存。" : "天气已更新。"; }
            else if (result.Failure?.Kind == FailureKind.Canceled) Status = previous;
            else { Status = AsyncStatus.Error; StatusMessage = result.Failure?.Message ?? "天气加载失败。"; }
        });
    }

    private string FormatLocal(DateTimeOffset utc)
    {
        var offset = City?.UtcOffsetSeconds is int seconds ? TimeSpan.FromSeconds(seconds) : TimeZoneInfo.Local.GetUtcOffset(utc);
        return utc.ToOffset(offset).ToString("yyyy-MM-dd HH:mm");
    }
}

public sealed class CitySearchViewModel : ViewModelBase
{
    private readonly IGeocodingService _geocoding;
    private string _keyword = "";
    private AsyncStatus _status = AsyncStatus.Idle;
    private static IReadOnlyList<Location> LocalCandidates => OfflinePlaceCatalog.All;
    /*
        new("local-beijing", "北京", "北京", "中国", "CN", 39.9042, 116.4074, "Asia/Shanghai", 8 * 3600),
        new("local-shanghai", "上海", "上海", "中国", "CN", 31.2304, 121.4737, "Asia/Shanghai", 8 * 3600),
        new("local-guangzhou", "广州", "广东", "中国", "CN", 23.1291, 113.2644, "Asia/Shanghai", 8 * 3600),
        new("local-shenzhen", "深圳", "广东", "中国", "CN", 22.5431, 114.0579, "Asia/Shanghai", 8 * 3600),
        new("local-wuhan", "武汉", "湖北", "中国", "CN", 30.5928, 114.3055, "Asia/Shanghai", 8 * 3600),
        new("local-wuchang", "武昌", "湖北", "中国", "CN", 30.5465, 114.3423, "Asia/Shanghai", 8 * 3600),
        new("local-nanjing", "南京", "江苏", "中国", "CN", 32.0603, 118.7969, "Asia/Shanghai", 8 * 3600),
        new("local-nanchang", "南昌", "江西", "中国", "CN", 28.6829, 115.8582, "Asia/Shanghai", 8 * 3600),
        new("local-nanning", "南宁", "广西", "中国", "CN", 22.8170, 108.3669, "Asia/Shanghai", 8 * 3600),
        new("local-nantong", "南通", "江苏", "中国", "CN", 31.9802, 120.8943, "Asia/Shanghai", 8 * 3600),
        new("local-nanyang", "南阳", "河南", "中国", "CN", 32.9908, 112.5283, "Asia/Shanghai", 8 * 3600),
        new("local-nanchong", "南充", "四川", "中国", "CN", 30.8373, 106.1107, "Asia/Shanghai", 8 * 3600),
        new("local-nanping", "南平", "福建", "中国", "CN", 26.6418, 118.1777, "Asia/Shanghai", 8 * 3600),
        new("local-nan'an", "南安", "福建", "中国", "CN", 24.9604, 118.3863, "Asia/Shanghai", 8 * 3600),
        new("local-nansha", "南沙", "广东", "中国", "CN", 22.8016, 113.5250, "Asia/Shanghai", 8 * 3600),
        new("local-nanjing-county", "南京县", "云南", "中国", "CN", 23.1900, 102.7000, "Asia/Shanghai", 8 * 3600)
    };*/
    public CitySearchViewModel(IGeocodingService geocoding, ILogger<CitySearchViewModel> logger) : base(logger) => _geocoding = geocoding;
    public string Keyword { get => _keyword; set => SetProperty(ref _keyword, value); }
    public ObservableCollection<Location> Results { get; } = new();
    public bool HasResults => Results.Count > 0;
    public AsyncStatus Status { get => _status; private set => SetProperty(ref _status, value); }
    public string StatusText => Status switch { AsyncStatus.Loading => "搜索中…", AsyncStatus.Empty => "没有找到城市。", AsyncStatus.Error => "搜索失败。", _ => "" };
    public IAsyncRelayCommand SearchCommand => new AsyncRelayCommand(SearchAsync);
    public event Action<Location>? CitySelected;
    public Task SuggestAsync() => SearchAsync();
    private async Task SearchAsync() => await RunExclusiveAsync(async ct => {
        Results.Clear(); OnPropertyChanged(nameof(HasResults)); if (string.IsNullOrWhiteSpace(Keyword)) { Status = AsyncStatus.Empty; return; }
        Status = AsyncStatus.Loading;
        var keyword = Keyword.Trim();
        var matches = LocalCandidates
            .Where(location => location.Name.StartsWith(keyword, StringComparison.CurrentCultureIgnoreCase))
            .OrderBy(location => location.Name.Equals(keyword, StringComparison.CurrentCultureIgnoreCase) ? 0 : 1)
            .ThenBy(location => AdministrativeRank(location.AdministrativeLevel))
            .ThenBy(location => location.Name, StringComparer.CurrentCulture)
            .Take(8).ToList();
        var result = matches.Count > 0 ? Result<IReadOnlyList<Location>>.Ok(matches) : await _geocoding.SearchAsync(keyword, ct);

        var providerMatches = matches.Count > 0 || !result.IsSuccess
            ? Array.Empty<Location>()
            : result.Value ?? Array.Empty<Location>();
        foreach (var location in matches.Concat(providerMatches)
            .Where(IsMainlandGeographic)
            .Where(location => location.Name.StartsWith(keyword, StringComparison.CurrentCultureIgnoreCase))
            .GroupBy(location => $"{location.Name.Trim()}|{location.Admin1?.Trim()}", StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(8)) Results.Add(location);
        OnPropertyChanged(nameof(HasResults));
        Status = Results.Count > 0 ? AsyncStatus.Success : result.IsSuccess ? AsyncStatus.Empty : AsyncStatus.Error;
    });
    public void Select(Location location) => CitySelected?.Invoke(location);
    public async Task<Location> ResolveAsync(Location location, CancellationToken ct = default)
    {
        if (location.Latitude != 0 || location.Longitude != 0) return location;
        // Open-Meteo ranks bare Chinese names by popularity, so a result for a
        // same-named place in another province can hide the intended city. The
        // administrative suffix query is exact for normal Chinese city names;
        // pinyin remains the fallback for providers that do not understand the
        // Chinese query.
        var suffix = location.AdministrativeLevel switch
        {
            AdministrativeLevel.PrefectureCity or AdministrativeLevel.CountyCity => location.AdministrativeSuffix ?? "市",
            AdministrativeLevel.PrefectureRegion => location.AdministrativeSuffix ?? string.Empty,
            AdministrativeLevel.District => location.AdministrativeSuffix ?? "区",
            AdministrativeLevel.County => location.AdministrativeSuffix ?? "县",
            _ => string.Empty
        };
        var exactName = suffix.Length > 0 && !location.Name.EndsWith(suffix, StringComparison.Ordinal)
            ? location.Name + suffix
            : location.Name;
        var queries = new[] { exactName, location.GeocodingName, location.GeocodingNameHyphenated, location.Name, $"{location.Name} {location.Admin1}" }
            .Where(query => query is not null)
            .Cast<string>()
            .Where(query => !string.IsNullOrWhiteSpace(query))
            .Distinct(StringComparer.OrdinalIgnoreCase);
        var candidates = new List<Location>();
        foreach (var query in queries)
        {
            var result = await _geocoding.SearchAsync(query, ct);
            if (result.IsSuccess && result.Value is { Count: > 0 }) candidates.AddRange(result.Value);
        }
        if (candidates.Count == 0) return location;
        var sameName = candidates
            .Where(candidate =>
                string.Equals(NormalizePlaceName(candidate.Name), NormalizePlaceName(location.Name), StringComparison.OrdinalIgnoreCase)
                || candidate.Name.Contains(location.Name, StringComparison.OrdinalIgnoreCase)
                || location.Name.Contains(candidate.Name, StringComparison.OrdinalIgnoreCase))
            .GroupBy(candidate => candidate.Id, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .ToArray();
        var province = NormalizeProvince(location.Admin1);
        var sameProvince = sameName.FirstOrDefault(candidate => string.Equals(NormalizeProvince(candidate.Admin1), province, StringComparison.OrdinalIgnoreCase));
        var resolved = province.Length > 0 ? sameProvince : sameName.FirstOrDefault();
        if (resolved is null && !string.IsNullOrWhiteSpace(location.Admin2))
        {
            var parentQueries = new[] { location.Admin2, location.GeocodingName, location.GeocodingNameHyphenated }
                .Where(query => !string.IsNullOrWhiteSpace(query)).Cast<string>().Distinct(StringComparer.OrdinalIgnoreCase);
            foreach (var parentQuery in parentQueries)
            {
                var parentResult = await _geocoding.SearchAsync(parentQuery, ct);
                var parent = parentResult.IsSuccess
                    ? parentResult.Value?.FirstOrDefault(candidate => string.Equals(NormalizeProvince(candidate.Admin1), province, StringComparison.OrdinalIgnoreCase))
                    : null;
                if (parent is not null)
                {
                    resolved = parent with { Name = location.Name, Admin1 = location.Admin1, Admin2 = location.Admin2, AdministrativeLevel = location.AdministrativeLevel };
                    break;
                }
            }
        }
        if (resolved is null) return location;
        return resolved with { AdministrativeLevel = location.AdministrativeLevel };
    }

    private static string NormalizePlaceName(string name) =>
        name.Trim().TrimEnd('市', '区', '县', '旗');

    private static int AdministrativeRank(AdministrativeLevel level) => level switch
    {
        AdministrativeLevel.PrefectureCity => 0,
        AdministrativeLevel.CountyCity => 1,
        AdministrativeLevel.District => 2,
        AdministrativeLevel.County => 3,
        AdministrativeLevel.PrefectureRegion => 4,
        _ => 5
    };

    private static string NormalizeProvince(string? province)
    {
        var value = (province ?? string.Empty).Trim().ToLowerInvariant()
            .Replace("province", string.Empty, StringComparison.Ordinal)
            .Replace("sheng", string.Empty, StringComparison.Ordinal)
            .Replace("省", string.Empty, StringComparison.Ordinal)
            .Replace("自治区", string.Empty, StringComparison.Ordinal)
            .Trim();
        var aliases = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["hainan"] = "海南", ["hainan sheng"] = "海南", ["yunnan"] = "云南", ["yunnan sheng"] = "云南",
            ["guangdong"] = "广东", ["guangxi"] = "广西", ["hubei"] = "湖北", ["hunan"] = "湖南",
            ["beijing"] = "北京", ["shanghai"] = "上海", ["jiangsu"] = "江苏", ["zhejiang"] = "浙江",
            ["sichuan"] = "四川", ["heilongjiang"] = "黑龙江", ["xinjiang"] = "新疆",
            ["neimenggu"] = "内蒙古", ["inner mongolia"] = "内蒙古", ["shanxi"] = "山西", ["shaanxi"] = "陕西",
            ["hebei"] = "河北", ["henan"] = "河南", ["shandong"] = "山东", ["liaoning"] = "辽宁",
            ["jilin"] = "吉林", ["fujian"] = "福建", ["jiangxi"] = "江西", ["anhui"] = "安徽",
            ["guizhou"] = "贵州", ["gansu"] = "甘肃", ["qinghai"] = "青海", ["ningxia"] = "宁夏",
            ["tibet"] = "西藏", ["xizang"] = "西藏", ["tianjin"] = "天津", ["chongqing"] = "重庆"
        };
        if (aliases.TryGetValue(value, out var canonical)) return canonical;
        return value.TrimEnd('省', '市');
    }

    private static bool IsMainlandGeographic(Location location) =>
        string.Equals(location.CountryCode, "CN", StringComparison.OrdinalIgnoreCase)
        && !string.IsNullOrWhiteSpace(location.Admin1)
        && !location.Name.Contains('@', StringComparison.Ordinal)
        && !location.Name.Contains("学校", StringComparison.Ordinal)
        && !location.Name.Contains("大学", StringComparison.Ordinal)
        && !location.Name.Contains("学院", StringComparison.Ordinal)
        && !location.Name.Contains("公司", StringComparison.Ordinal)
        && !location.Name.Contains("影视", StringComparison.Ordinal);
}

public sealed class CityListViewModel : ViewModelBase
{
    private readonly ICityStore _store;
    public CityListViewModel(ICityStore store, ILogger<CityListViewModel> logger) : base(logger) => _store = store;
    public ObservableCollection<Location> Favorites { get; } = new();
    public ObservableCollection<Location> Recents { get; } = new();
    public event Action<Location>? CitySelected;
    public async Task LoadAsync(CancellationToken ct = default)
    {
        var favorites = await _store.GetFavoritesAsync(ct); var recents = await _store.GetRecentAsync(ct);
        if (favorites.IsSuccess) { Favorites.Clear(); foreach (var item in favorites.Value!) Favorites.Add(item); }
        if (recents.IsSuccess) { Recents.Clear(); foreach (var item in recents.Value!) Recents.Add(item); }
    }
    public async Task ToggleFavoriteAsync(Location city, CancellationToken ct = default)
    {
        var existing = Favorites.Any(x => x.Id == city.Id); var result = existing ? await _store.RemoveFavoriteAsync(city.Id, ct) : await _store.AddFavoriteAsync(city, ct);
        if (result.IsSuccess) await LoadAsync(ct);
    }
    public void Select(Location city) => CitySelected?.Invoke(city);
}

public sealed class ChatViewModel : ViewModelBase
{
    private readonly IWeatherAgent _agent; private readonly IClock _clock; private Location? _city; private string _input = ""; private bool _sending;
    public ChatViewModel(IWeatherAgent agent, IClock clock, ILogger<ChatViewModel> logger) : base(logger) { _agent = agent; _clock = clock; }
    public ObservableCollection<ChatMessage> Messages { get; } = new();
    public IReadOnlyList<string> QuickQuestions { get; } = Array.Empty<string>();
    public string InputText { get => _input; set => SetProperty(ref _input, value); }
    public bool IsSending { get => _sending; private set => SetProperty(ref _sending, value); }
    public Location? City { set => _city = value; }
    public IAsyncRelayCommand SendCommand => new AsyncRelayCommand(SendAsync);
    public void UseQuickQuestion(string question) => InputText = question;
    private async Task SendAsync()
    {
        var text = InputText.Trim(); if (text.Length == 0 || IsSending) return; InputText = ""; IsSending = true;
        Messages.Add(new ChatMessage { Id = Guid.NewGuid(), IsUser = true, Text = text, CreatedAtUtc = _clock.UtcNow });
        try
        {
            var offset = _city?.UtcOffsetSeconds is int seconds ? TimeSpan.FromSeconds(seconds) : TimeSpan.Zero;
            var result = await _agent.AskAsync(text, new AgentContext(_city, offset, false) { Today = _clock.TodayIn(offset) }, CancellationToken.None);
            var reply = result.IsSuccess && result.Value is not null ? result.Value : new AgentReply(AgentReplyKind.Error, result.Failure?.Message ?? "请求失败。", _city, null, AgentIntent.Unknown, Array.Empty<string>(), null);
            var assistantMessage = new ChatMessage { Id = Guid.NewGuid(), Reply = reply, CreatedAtUtc = _clock.UtcNow };
            Messages.Add(assistantMessage);
            foreach (var chunk in StreamChunks(reply.Text, 4))
            {
                assistantMessage.Text += chunk;
                await Task.Delay(18);
            }
        }
        finally { IsSending = false; }
    }

    private static IEnumerable<string> StreamChunks(string text, int chunkSize)
    {
        for (var index = 0; index < text.Length; index += chunkSize)
            yield return text.Substring(index, Math.Min(chunkSize, text.Length - index));
    }
}

public sealed class SettingsViewModel : ViewModelBase
{
    private readonly ISettingsStore _store; private string _baseUrl = "https://api.deepseek.com"; private string _model = "deepseek-chat"; private string _apiKey = ""; private AiMode _mode; private TemperatureUnit _unit; private AppTheme _theme;
    public SettingsViewModel(ISettingsStore store, ILogger<SettingsViewModel> logger) : base(logger) => _store = store;
    public string BaseUrl { get => _baseUrl; set => SetProperty(ref _baseUrl, value); } public string Model { get => _model; set => SetProperty(ref _model, value); } public string ApiKeyInput { get => _apiKey; set => SetProperty(ref _apiKey, value); }
    public AiMode Mode { get => _mode; set => SetProperty(ref _mode, value); }
    public TemperatureUnit Unit { get => _unit; set { if (SetProperty(ref _unit, value)) UnitChanged?.Invoke(value); } }
    public AppTheme Theme { get => _theme; set { if (SetProperty(ref _theme, value)) ThemeChanged?.Invoke(value); } }
    public event Action<AppTheme>? ThemeChanged;
    public event Action<TemperatureUnit>? UnitChanged;
    public IReadOnlyList<AiMode> Modes { get; } = Enum.GetValues<AiMode>();
    public IReadOnlyList<TemperatureUnit> Units { get; } = Enum.GetValues<TemperatureUnit>();
    public IReadOnlyList<AppTheme> Themes { get; } = Enum.GetValues<AppTheme>();
    public string MaskedApiKey => SecretMasker.Mask(ApiKeyInput);
    public IAsyncRelayCommand SaveCommand => new AsyncRelayCommand(SaveAsync);
    public IAsyncRelayCommand ClearApiKeyCommand => new AsyncRelayCommand(ClearApiKeyAsync);
    public async Task LoadAsync(CancellationToken ct = default) { var result = await _store.LoadAsync(ct); if (!result.IsSuccess) return; var s = result.Value!; Mode = s.Mode; BaseUrl = s.DeepSeekBaseUrl; Model = s.DeepSeekModel; Unit = s.Unit; Theme = s.Theme; ThemeChanged?.Invoke(Theme); var key = await _store.GetApiKeyAsync(ct); ApiKeyInput = key.Value ?? ""; }
    private async Task SaveAsync() { var result = await _store.SaveAsync(new AppSettings(Mode, BaseUrl, Model, !string.IsNullOrWhiteSpace(ApiKeyInput), Unit, Theme), CancellationToken.None); if (result.IsSuccess) await _store.SetApiKeyAsync(ApiKeyInput, CancellationToken.None); OnPropertyChanged(nameof(MaskedApiKey)); }
    private async Task ClearApiKeyAsync() { await _store.SetApiKeyAsync(string.Empty, CancellationToken.None); ApiKeyInput = string.Empty; OnPropertyChanged(nameof(MaskedApiKey)); }
}

public sealed class MainWindowViewModel : ViewModelBase
{
    private readonly IAppStateStore _state; private Location? _currentCity; private string _statusMessage = "请选择城市或搜索地点。";
    public MainWindowViewModel(WeatherViewModel weather, CitySearchViewModel search, CityListViewModel cityList, ChatViewModel chat, SettingsViewModel settings, IAppStateStore state, ILogger<MainWindowViewModel> logger) : base(logger) { Weather = weather; Search = search; CityList = cityList; Chat = chat; Settings = settings; _state = state; Search.CitySelected += city => _ = OnCitySelected(city); CityList.CitySelected += city => _ = OnCitySelected(city); Settings.UnitChanged += unit => Weather.Unit = unit; }
    public WeatherViewModel Weather { get; } public CitySearchViewModel Search { get; } public CityListViewModel CityList { get; } public ChatViewModel Chat { get; } public SettingsViewModel Settings { get; }
    public Location? CurrentCity { get => _currentCity; private set { SetProperty(ref _currentCity, value); Chat.City = value; } }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    private bool _isSettingsOpen;
    public bool IsSettingsOpen { get => _isSettingsOpen; private set => SetProperty(ref _isSettingsOpen, value); }
    public void OpenSettings() { IsSettingsOpen = true; OnPropertyChanged(nameof(IsSettingsOpen)); }
    public void CloseSettings() { IsSettingsOpen = false; OnPropertyChanged(nameof(IsSettingsOpen)); }
    public async Task InitializeAsync() { await CityList.LoadAsync(); await Settings.LoadAsync(); var last = await _state.GetLastCityAsync(CancellationToken.None); if (last.IsSuccess && last.Value is { } city) await OnCitySelected(city); }
    private async Task OnCitySelected(Location city)
    {
        city = await Search.ResolveAsync(city);
        if (city.Id.StartsWith("offline-", StringComparison.Ordinal) && city.Latitude == 0 && city.Longitude == 0)
        {
            StatusMessage = $"未找到 {city.SearchDisplayName} 的匹配坐标，请重试。";
            return;
        }
        CurrentCity = city;
        await Weather.SetCityAsync(city);
        StatusMessage = Weather.StatusMessage;
    }
}
