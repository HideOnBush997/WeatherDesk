# WeatherDesk 详细设计文档
 
## 1. 文档目的与依据
 
 本文基于 `doc/proposal.md`（需求确认稿）和 `doc/high-level-design.md`（概要设计），给出每个模块的接口契约、内部实现结构、状态与异常处理、数据模型、外部 API 契约和独立测试方案，用于直接指导编码与单元测试。
 
 本文与概要设计的关系：概要设计确定模块边界与依赖关系，本文确定每个模块怎么实现、怎么被单独测试。两者冲突时以需求文档为准。
 
 不在本文范围：账号体系、云同步、通用聊天、插件市场、真实硬件接入、多城市对比、天气预警（需求文档列为非目标或后续方向）。
 
## 2. 全局设计约定
 
### 2.1 模块独立性判定标准
 
 一个模块被视为“可独立测试”，必须同时满足：
 
 1. 对外只暴露接口：调用方只依赖 Domain 中定义的接口，不依赖具体实现类。
 2. 依赖可替换：模块的外部依赖（HTTP、SQLite、文件系统、时间、AI 服务）通过构造函数注入，测试时可替换为 Fake。
 3. 无隐藏全局状态：不使用静态可变状态，不直接读取 `DateTime.Now`，不自行创建 `HttpClient`。
 4. 无跨模块 UI 依赖：非展示模块不得引用 Avalonia 类型。
 5. 失败可观测：对外方法返回统一 `Result` / `Result<T>`，不使用异常向调用方传递业务失败。
 6. 单一构建块：模块拥有独立目录与独立测试目录，测试不依赖真实网络、真实数据库和真实 UI 线程。
 
### 2.2 统一结果类型
 
 ```csharp
 namespace WeatherDesk.Domain.Results;
 
 public enum FailureKind
 {
     None = 0,
     Input,            // 输入为空或非法
     Canceled,         // 用户或应用取消
     Timeout,          // 请求超时
     Network,          // 连接失败、DNS 失败等
     Upstream,         // 外部服务返回非成功状态码
     DataFormat,       // 响应结构或字段不符合预期
     Storage,          // SQLite 或文件读写失败
     AiUnavailable,    // AI 服务不可达或超时
     AiUnauthorized,   // API Key 无效或缺失
     AiMalformed       // AI 返回内容无法安全使用
 }
 
 public sealed record Failure(FailureKind Kind, string Message, bool Retryable);
 
 public sealed class Result
 {
     public bool IsSuccess { get; }
     public Failure? Failure { get; }
     public static Result Ok();
     public static Result Fail(Failure failure);
 }
 
 public sealed class Result<T>
 {
     public bool IsSuccess { get; }
     public T? Value { get; }
     public Failure? Failure { get; }
     public static Result<T> Ok(T value);
     public static Result<T> Fail(Failure failure);
 }
 ```
 
 约定：Domain 内部纯函数直接返回值；`Result` 只在应用服务、Infrastructure 适配层和 ViewModel 边界使用。
 
### 2.3 异步状态枚举
 
 ```csharp
 namespace WeatherDesk.Domain.Results;
 
 public enum AsyncStatus { Idle, Loading, Success, Empty, Error, Stale }
 ```
 
 状态由各 ViewModel 自己持有，天气、城市搜索、AI 对话三块互不覆盖。状态转换规则见第 9 节。
 
### 2.4 时间与单位约定
 
 ```csharp
 namespace WeatherDesk.Domain.Interfaces;
 
 public interface IClock
 {
     DateTimeOffset UtcNow { get; }
     DateOnly TodayIn(TimeSpan utcOffset);
     DateTimeOffset NowIn(TimeSpan utcOffset);
 }
 ```
 
 约束：
 
 1. 所有持久化时间戳使用 UTC（`DateTimeOffset`），展示层按城市时区或本地时区格式化。
 2. 领域模型内部单位：温度摄氏度、风速 `km/h`、降水 `mm`、降水概率百分比、气压 `hPa`、能见度 `km`、湿度百分比、紫外线指数为无量纲数值。
 3. 华氏度只在展示层按设置换算，不进入 API 请求参数，避免请求参数随单位设置变化。
 4. 换算集中在 `Domain.Rules.UnitConverter`（纯函数，可单测）。
 
### 2.5 取消、超时与重试默认值
 
 以下为工程内定默认值（需求未指定，见第 13 节待确认项）：
 
 | 场景 | 超时 | 自动重试 |
 |---|---|---|
 | Open-Meteo Geocoding | 10 秒 | 0 次，进入错误状态并提供手动重试 |
 | Open-Meteo Forecast | 10 秒 | 0 次，失败后转缓存 |
 | DeepSeek chat/completions | 30 秒 | 0 次，提示重试或切换演示模式 |
 | SQLite 操作 | 5 秒命令超时 | 0 次，失败降级为内存运行并提示 |
 
 所有异步方法必须接收 `CancellationToken`；同一操作重复触发时，ViewModel 先取消上一次请求。
 
### 2.6 日志约定
 
 1. 使用 `Microsoft.Extensions.Logging` 的 `ILogger<T>`，通过构造函数注入。
 2. 禁止写入：完整 API Key、Authorization 请求头、完整请求或响应正文、用户问题原文。
 3. AI 日志只记录模型名、耗时、HTTP 状态码、失败类型；天气日志只记录城市标识、查询日期、耗时、状态码。
 4. 提供 `SecretMasker.Mask`：返回脱敏串或仅保留末 4 位，用于设置页展示与日志。
 
### 2.7 依赖注入与组装
 
 1. 组装根位于 `WeatherDesk.App` 的 `ServiceRegistration.cs`；`Program.cs` 只负责启动。
 2. 注册形式一律为“Domain 接口到 Infrastructure 实现”的映射。
 3. `HttpClient` 通过 `IHttpClientFactory` 创建；`AppDbContext` 使用 `IDbContextFactory<AppDbContext>`；ViewModel 注册为瞬时。
 4. Infrastructure 内部不自行解析服务，只通过构造函数接收依赖。
 5. 单元测试不使用 DI 容器，直接手工构造被测对象与 Fake。
 
### 2.8 源码目录结构
 
 ```text
 src/
   WeatherDesk.Domain/
     Models/        Location / WeatherSnapshot / CurrentWeather / HourlyForecast
                    / DailyForecast / AgentContext / AgentReply / AppSettings
     Interfaces/    IGeocodingService / IWeatherService / IWeatherCache / IWeatherAgent
                    / IAnswerGenerator / ICityStore / IAppStateStore / ISettingsStore / IClock
     Rules/         WeatherCodeCatalog / CacheFreshnessPolicy / AgentParameterValidator
                    / QuestionAnalyzer / UnitConverter / AdviceRuleSet / DateResolver
     Results/       Result / Result<T> / Failure / FailureKind / AsyncStatus
     Services/      WeatherQueryService（编排：最新优先 + 缓存降级）
   WeatherDesk.Infrastructure/
     Http/          OpenMeteoGeocodingClient / OpenMeteoWeatherClient / HttpFailureMapper
                    / OpenMeteoResponseParser
     Ai/            DeepSeekClient / DemoAnswerGenerator / AnswerGeneratorSelector
     Agent/         AgentService / WeatherTool / AgentPromptBuilder / AgentReplyMapper
     Persistence/   AppDbContext / Entities / EfCityStore / EfWeatherCache / EfAppStateStore
                    / Migrations
     Configuration/ AppPaths / JsonSettingsStore / SecretProtector
     Logging/       SecretMasker
   WeatherDesk.App/
     ViewModels/    MainWindowViewModel / WeatherViewModel / CitySearchViewModel
                    / CityListViewModel / ChatViewModel / SettingsViewModel / ViewModelBase
     Views/         MainWindow / SettingsWindow / 各用户控件
     Styles/ Converters/ Services/
     App.axaml / Program.cs / ServiceRegistration.cs
   WeatherDesk.Tests/
     DomainTests/ AgentTests/ CacheTests/ ServiceTests/ PersistenceTests/ AppTests/
     Fakes/        FakeWeatherService / FakeGeocodingService / FakeWeatherCache / FakeCityStore
                   / FakeAppStateStore / FakeSettingsStore / FakeAnswerGenerator / FakeClock
 ```
 
## 3. 模块划分与依赖矩阵
 
 | 编号 | 模块 | 所在项目 | 编译期依赖 | 运行期外部依赖 |
 |---|---|---|---|---|
 | M1 | 领域模型与规则 | Domain | 无 | 无 |
 | M2 | 城市搜索与地理编码 | Infrastructure | Domain | Open-Meteo Geocoding API |
 | M3 | 天气查询编排 | Domain（编排）+ Infrastructure（适配） | Domain | IWeatherService、IWeatherCache、IClock |
 | M4 | 天气缓存 | Infrastructure | Domain | ICacheStorage（SQLite） |
 | M5 | 城市管理（收藏与最近访问） | Infrastructure | Domain | ICityStore、IAppStateStore（SQLite） |
 | M6 | Agent 编排 | Infrastructure（规则在 Domain） | Domain | IWeatherService、IWeatherCache、IGeocodingService、IAnswerGenerator |
 | M7 | AI 适配（DeepSeek 与演示） | Infrastructure | Domain | OpenAI 兼容 HTTP 接口、ISettingsStore |
 | M8 | 配置与设置 | Infrastructure | Domain | 本地配置文件与凭据存储 |
 | M9 | 本地持久化（EF Core SQLite） | Infrastructure | Domain | SQLite |
 | M10 | 桌面展示（Avalonia） | App | Domain + Infrastructure 注册 | 全部应用服务 |
 | M11 | 测试 | Tests | 全部 | Fake 实现 |
 
 模块依赖方向（只列编译期依赖）：
 
 ```mermaid
 graph TD
   M10[M10 桌面展示] --> M1[M1 领域模型与规则]
   M10 --> M2[M2 城市搜索]
   M10 --> M3[M3 天气查询编排]
   M10 --> M5[M5 城市管理]
   M10 --> M6[M6 Agent 编排]
   M10 --> M8[M8 配置与设置]
   M2 --> M1
   M3 --> M1
   M4[M4 天气缓存] --> M1
   M4 --> M9[M9 本地持久化]
   M5 --> M1
   M5 --> M9
   M6 --> M1
   M6 --> M7[M7 AI 适配]
   M7 --> M1
   M7 --> M8
   M8 --> M1
   M9 --> M1
 ```
 
 关键约束：
 
 1. 天气页面与 Agent 共用同一条天气链路（M3 + M4），不允许出现两套天气数据源。
 2. Agent 只能通过 M6 的天气工具获取结构化天气数据，不允许模型自行生成天气数值。
 3. M1 不引用任何外部 NuGet 包；M9 的 EF 实体类型不出现在 Domain 与 App 中。
 4. 任何模块都不得反向依赖展示层，也不得直接依赖另一个 Infrastructure 模块的具体类（只能通过 Domain 接口）。
 
## 4. M1 领域模型与规则模块
 
### 4.1 数据模型
 
 ```csharp
 namespace WeatherDesk.Domain.Models;
 
 public sealed record Location(
     string Id,            // Open-Meteo geocoding 的 id，作为稳定标识
     string Name,          // 城市名
     string? Admin1,       // 一级行政区，如省/州
     string? Country,
     string? CountryCode,
     double Latitude,
     double Longitude,
     string? Timezone,     // IANA 时区，如 Asia/Shanghai
     int? UtcOffsetSeconds // Open-Meteo 返回的 utc_offset_seconds
 )
 {
     public string DisplayName => string.Join(", ", new[] { Name, Admin1, Country }.Where(s => !string.IsNullOrWhiteSpace(s)));
 }
 
 public enum WeatherConditionGroup { Clear, PartlyCloudy, Cloudy, Fog, Drizzle, Rain, Snow, Thunderstorm, Unknown }
 
 public sealed record CurrentWeather(
     DateTimeOffset ObservedAtUtc,
     double TemperatureC,
     double ApparentTemperatureC,
     int WeatherCode,
     double HumidityPercent,
     double WindSpeedKph,
     double WindGustKph,
     double PressureHpa,
     double? VisibilityKm,
     bool IsDay
 );
 
 public sealed record HourlyForecast(
     DateTimeOffset HourUtc,
     double TemperatureC,
     double ApparentTemperatureC,
     int WeatherCode,
     double PrecipitationMm,
     double PrecipitationProbabilityPercent,
     double WindSpeedKph,
     double HumidityPercent
 );
 
 public sealed record DailyForecast(
     DateOnly Date,
     double MinTemperatureC,
     double MaxTemperatureC,
     int WeatherCode,
     double PrecipitationSumMm,
     double PrecipitationProbabilityMaxPercent,
     double WindSpeedMaxKph,
     double? UvIndexMax,
     DateTimeOffset? SunriseUtc,
     DateTimeOffset? SunsetUtc
 );
 
 public sealed record WeatherSnapshot(
     Location Location,
     CurrentWeather? Current,
     IReadOnlyList<HourlyForecast> Hourly,
     IReadOnlyList<DailyForecast> Daily,
     DateTimeOffset RetrievedAtUtc,
     DateTimeOffset DataUpdatedAtUtc,   // Open-Meteo 返回的 current.time / 数据生成时间
     string SourceName,                 // "Open-Meteo"
     int DataVersion,                   // 序列化格式版本，默认 1
     WeatherProvenance Provenance       // Live 或 Cache
 );
 
 public enum WeatherProvenance { Live, Cache }
 ```
 
 Agent 相关模型：
 
 ```csharp
 namespace WeatherDesk.Domain.Models;
 
 public enum AgentReplyKind { Answer, NeedClarification, OutOfScope, Error }
 public enum AgentIntent { Unknown, Commute, Running, Umbrella, Clothing, General }
 
 public sealed record AgentContext(
     Location? CurrentCity,          // 主界面当前城市，可为空
     TimeSpan UtcOffset,             // 用于把“今天/明天”解析为日期
     bool PreferDemoMode             // 设置项：强制演示模式
 );
 
 public sealed record AgentReply(
     AgentReplyKind Kind,
     string Text,
     Location? City,                 // 回答所依据的城市
     DateOnly? Date,                 // 回答所依据的日期
     AgentIntent Intent,
     IReadOnlyList<string> Evidence, // 供 UI 展示的关键依据，例如“降水概率 80%”
     ResultError? Error              // Kind 为 Error 时携带的失败信息
 );
 
 public sealed class ChatMessage
 {
     public Guid Id { get; init; }
     public bool IsUser { get; init; }
     public string Text { get; init; } = "";
     public DateTimeOffset CreatedAtUtc { get; init; }
     public AgentReply? Reply { get; init; }   // 助手消息时非空
 }
 ```
 
 设置模型：
 
 ```csharp
 namespace WeatherDesk.Domain.Models;
 
 public enum TemperatureUnit { Celsius, Fahrenheit }
 public enum AppTheme { Light, Dark, FollowSystem }
 public enum AiMode { Auto, Demo, DeepSeek }   // Auto = 按 API Key 是否存在自动选择
 
 public sealed record AppSettings(
     AiMode Mode,
     string DeepSeekBaseUrl,     // 默认 https://api.deepseek.com
     string DeepSeekModel,       // 默认 deepseek-chat
     bool HasApiKey,             // 由 ISettingsStore 计算得出，不保存明文到内存快照以外
     TemperatureUnit Unit,
     AppTheme Theme
 );
 ```
 
### 4.2 业务规则
 
 | 规则类 | 职责 | 关键方法 | 纯函数 |
 |---|---|---|---|
 | `WeatherCodeCatalog` | Open-Meteo weather_code 到中文描述与分组的映射 | `Describe(int code)`、`Group(int code)`、`IconKey(int code)` | 是 |
 | `CacheFreshnessPolicy` | 判断快照是否“未过期” | `IsFresh(WeatherSnapshot s, DateTimeOffset nowUtc, TimeSpan ttl)`、`AgeOf(...)` | 是 |
 | `DateResolver` | 解析相对日期词 | `TryResolve(string text, DateOnly today, out DateOnly date)`，覆盖“今天/明天/后天/周X/YYYY-MM-DD” | 是 |
 | `AgentParameterValidator` | 校验工具参数 | `Validate(WeatherToolRequest r, DateTimeOffset nowUtc)` 返回缺失项与非法项 | 是 |
 | `QuestionAnalyzer` | 从问题文本抽取城市、日期、意图 | `Analyze(string question, AgentContext ctx)` 返回 `QuestionAnalysis` | 是 |
 | `UnitConverter` | 单位换算与格式化 | `ToFahrenheit(double c)`、`FormatTemperature(double c, TemperatureUnit u)` | 是 |
 | `AdviceRuleSet` | 由结构化天气生成建议要点 | `Build(WeatherSnapshot s, DateOnly? date, AgentIntent intent)` 返回要点列表 | 是 |
 
 规则均为静态无状态类，输入全部显式传入（包括当前时间），不读取系统时间。
 
### 4.3 接口契约
 
 ```csharp
 namespace WeatherDesk.Domain.Interfaces;
 
 public interface IGeocodingService
 {
     Task<Result<IReadOnlyList<Location>>> SearchAsync(string keyword, CancellationToken ct);
 }
 
 public interface IWeatherService
 {
     Task<Result<WeatherSnapshot>> GetForecastAsync(Location location, DateOnly? date, CancellationToken ct);
 }
 
 public interface IWeatherCache
 {
     Task<Result<WeatherSnapshot?>> GetLatestAsync(Location location, CancellationToken ct);
     Task<Result> SaveAsync(WeatherSnapshot snapshot, CancellationToken ct);
 }
 
 public interface ICityStore
 {
     Task<Result<IReadOnlyList<Location>>> GetFavoritesAsync(CancellationToken ct);
     Task<Result> AddFavoriteAsync(Location location, CancellationToken ct);
     Task<Result> RemoveFavoriteAsync(string locationId, CancellationToken ct);
     Task<Result<IReadOnlyList<Location>>> GetRecentAsync(CancellationToken ct);
     Task<Result> UpsertRecentAsync(Location location, DateTimeOffset visitedAtUtc, CancellationToken ct);
 }
 
 public interface IAppStateStore
 {
     Task<Result<Location?>> GetLastCityAsync(CancellationToken ct);
     Task<Result> SetLastCityAsync(Location location, CancellationToken ct);
 }
 
 public interface ISettingsStore
 {
     Task<Result<AppSettings>> LoadAsync(CancellationToken ct);
     Task<Result> SaveAsync(AppSettings settings, CancellationToken ct);
     Task<Result<string?>> GetApiKeyAsync(CancellationToken ct);
     Task<Result> SetApiKeyAsync(string apiKey, CancellationToken ct);
 }
 
 public interface IAnswerGenerator
 {
     bool IsAvailable(AppSettings settings);
     Task<Result<string>> GenerateAsync(AgentPrompt prompt, AppSettings settings, CancellationToken ct);
 }
 
 public interface IWeatherAgent
 {
     Task<Result<AgentReply>> AskAsync(string question, AgentContext context, CancellationToken ct);
 }
 ```
 
### 4.4 独立测试方案（DomainTests）
 
 | 测试 | 输入 | 期望 |
 |---|---|---|
 | 天气代码映射 | 0、3、61、95 等全部 Open-Meteo 代码 | 有中文描述，分组正确，未知代码返回 Unknown 而不抛异常 |
 | 缓存新鲜度 | 快照时间 = now - 5 分钟 / 2 小时，TTL = 30 分钟 | 前者 fresh，后者不 fresh |
 | 日期解析 | “明天”“后天”“周一”“2026-10-05” | 解析出正确 `DateOnly`；无法解析返回 false |
 | 参数校验 | 缺 city、缺 date、date 超出预报范围 | 返回对应缺失或非法项，不抛异常 |
 | 问题分析 | “明天上海适合跑步吗”“今天适合通勤吗” | 抽出城市、日期、意图；无城市时城市为空 |
 | 单位换算 | 0、20、100 摄氏度 | 华氏度数值正确，格式化保留一位小数 |
 | 建议规则 | 降水概率 80%、风速 40、温度 -5 | 生成对应要点（带伞、风大、保暖） |
 
 测试不使用任何 Fake，全部为纯函数测试。
 
## 5. M2 城市搜索与地理编码模块
 
### 5.1 外部 API 契约
 
 请求：
 
 ```text
 GET https://geocoding-api.open-meteo.com/v1/search?name={keyword}&count=10&language=zh&format=json
 ```
 
 响应关键字段：
 
 ```json
 {
   "results": [
     { "id": 1, "name": "上海", "latitude": 31.22, "longitude": 121.46,
       "country": "中国", "country_code": "CN", "admin1": "上海市", "timezone": "Asia/Shanghai" }
   ]
 }
 ```
 
### 5.2 实现结构
 
 ```csharp
 namespace WeatherDesk.Infrastructure.Http;
 
 public sealed class OpenMeteoGeocodingClient : IGeocodingService
 {
     public OpenMeteoGeocodingClient(HttpClient http, ILogger<OpenMeteoGeocodingClient> logger);
 
     public async Task<Result<IReadOnlyList<Location>>> SearchAsync(string keyword, CancellationToken ct);
 }
 ```
 
 处理顺序：
 
 1. 入参校验：`keyword` 为空白时直接返回 `Failure.Input`，不发请求。
 2. 构造 URL 并对 `name` 做 URL 编码，`count=10`，`language=zh`。
 3. 用 `CancellationTokenSource.CreateLinkedTokenSource(ct)` 叠加 10 秒超时。
 4. 发送 GET，读取响应字符串。
 5. HTTP 非 2xx：映射为 `Upstream`，附状态码。
 6. JSON 解析：`System.Text.Json`，`PropertyNameCaseInsensitive = true`。
 7. `results` 缺失或为空：返回 `Result.Ok(空列表)`，由上层判为 Empty。
 8. 逐条映射为 `Location`；缺少必填字段（id、name、经纬度）的条目跳过。
 9. 全部条目被跳过且结果为空：返回空列表。
 
### 5.3 失败映射（`HttpFailureMapper`）
 
 | 异常或状态 | FailureKind | 是否可重试 |
 |---|---|---|
 | `OperationCanceledException` 且 ct 已取消 | Canceled | 是 |
 | `TaskCanceledException` 且未取消 | Timeout | 是 |
 | `HttpRequestException` | Network | 是 |
 | 非 2xx 状态码 | Upstream | 是（5xx 可重试，4xx 除 429 外不可重试） |
 | `JsonException`、必填字段缺失 | DataFormat | 是 |
 
### 5.4 独立测试方案（ServiceTests）
 
 使用 `HttpMessageHandler` 的 Stub 注入 `HttpClient`，覆盖：
 
 1. 正常响应：返回 3 个城市，字段映射正确，`DisplayName` 拼接正确。
 2. 空 `results`：返回空列表且不报错。
 3. HTTP 500：返回 `Upstream`，`Retryable = true`。
 4. 超时：Stub 延迟后 cancel，返回 `Timeout`。
 5. 非法 JSON：返回 `DataFormat`。
 6. 空白关键词：不发请求，返回 `Input`。
 
## 6. M3 天气查询与编排模块
 
### 6.1 外部 API 契约
 
 ```text
 GET https://api.open-meteo.com/v1/forecast
   ?latitude={lat}&longitude={lon}
   &current=temperature_2m,apparent_temperature,relative_humidity_2m,is_day,weather_code,
            surface_pressure,wind_speed_10m,wind_gusts_10m
   &hourly=temperature_2m,apparent_temperature,weather_code,precipitation,
           precipitation_probability,wind_speed_10m,relative_humidity_2m
   &daily=weather_code,temperature_2m_max,temperature_2m_min,precipitation_sum,
          precipitation_probability_max,wind_speed_10m_max,uv_index_max,sunrise,sunset
   &timezone=auto
   &forecast_days=7
 ```
 
 需求要求的展示字段（温度、体感、天气状况、湿度、风速、气压、能见度）中，能见度需要额外请求 `hourly=visibility`；若 Open-Meteo 当前返回体不含该变量，则 `VisibilityKm` 为 null 并在 UI 显示为“暂无”，不因为单个字段缺失导致整条链路失败。
 
### 6.2 编排服务
 
 ```csharp
 namespace WeatherDesk.Domain.Services;
 
 public sealed class WeatherQueryService
 {
     public WeatherQueryService(IWeatherService weatherService, IWeatherCache cache,
                                ICityStore cityStore, IAppStateStore appStateStore,
                                IClock clock, CacheFreshnessPolicy freshness);
 
     public Task<Result<WeatherQueryResult>> LoadAsync(Location location, DateOnly? date, CancellationToken ct);
     public Task<Result<WeatherQueryResult>> RefreshAsync(Location location, DateOnly? date, CancellationToken ct);
 }
 
 public sealed record WeatherQueryResult(
     WeatherSnapshot Snapshot,
     bool IsStale,                   // true 表示来自缓存或已超过 TTL
     DateTimeOffset? LastUpdatedAtUtc,
     ResultError? DegradedReason     // 降级原因，用于 UI 提示
 );
 ```
 
 编排顺序（最新优先 + 缓存降级）：
 
 ```text
 LoadAsync(location, date)
   -> 1. IWeatherService.GetForecastAsync
   -> 2. 成功：构造 Snapshot(Provenance=Live)
           -> 并行：cache.SaveAsync（失败只记录日志，不影响结果）
           -> cityStore.UpsertRecentAsync（失败只记录日志）
           -> appStateStore.SetLastCityAsync（失败只记录日志）
           -> 返回 IsStale=false
   -> 3. 失败：cache.GetLatestAsync(location)
           -> 有缓存且 freshness.IsFresh：返回 IsStale=false，Provenance=Cache，DegradedReason=上游失败
           -> 有缓存但不 fresh：返回 IsStale=true，DegradedReason=上游失败 + 数据已过期
           -> 无缓存或缓存读取失败：返回 Result.Fail（携带原始 Failure），UI 显示 Error 与重试
   -> 4. 全程只在步骤 1 使用网络；缓存与持久化失败不改变主流程成败
 ```
 
 `RefreshAsync` 与 `LoadAsync` 逻辑相同，区别是它由 UI 的“重试/刷新”按钮触发，并在开始时取消上一次未完成的同源请求。
 
### 6.3 Open-Meteo 适配实现
 
 ```csharp
 namespace WeatherDesk.Infrastructure.Http;
 
 public sealed class OpenMeteoWeatherClient : IWeatherService
 {
     public async Task<Result<WeatherSnapshot>> GetForecastAsync(Location location, DateOnly? date, CancellationToken ct);
 }
 
 public static class OpenMeteoResponseParser   // 纯函数，便于不联网测试
 {
     public static Result<WeatherSnapshot> Parse(Location location, string json, DateTimeOffset retrievedAtUtc);
 }
 ```
 
 解析规则：
 
 1. 数组型字段（`hourly.time`、`hourly.temperature_2m` 等）按索引对齐；长度不一致时截断到最短长度，不抛异常。
 2. `null` 数值字段映射为对应可空字段；`current` 必填字段缺失时整体返回 `DataFormat`。
 3. `timezone=auto` 返回的 `utc_offset_seconds` 写入 `Location.UtcOffsetSeconds`。
 4. `date` 参数只影响结果裁剪：传入日期时保留该日 00:00-23:00 的逐小时项，`Daily` 仍保留完整 7 天（供 Agent 使用），并在快照中记录查询日期。
 5. `DataUpdatedAtUtc` 取 `current.time`（按 `utc_offset_seconds` 换算为 UTC）；缺失则回退为 `retrievedAtUtc`。
 
### 6.4 独立测试方案
 
 ServiceTests（编排，用 Fake）：
 
 1. 上游成功：返回 Live 快照，`IsStale = false`，缓存保存被调用 1 次。
 2. 上游失败 + 有新鲜缓存：返回成功且 `Provenance = Cache`、`IsStale = false`、`DegradedReason` 非空。
 3. 上游失败 + 缓存过期：返回成功且 `IsStale = true`。
 4. 上游失败 + 无缓存：返回 `Result.Fail`，Kind 与上游一致。
 5. 上游成功但缓存写入失败：结果仍为成功，不因缓存失败而降级。
 6. 取消：`ct` 取消后返回 `Canceled`，且不写缓存。
 
 ServiceTests（解析，不联网）：
 
 1. 标准 JSON：当前、逐小时 24 条、每日 7 条映射正确。
 2. 数组长度不一致：截断到最短，不抛异常。
 3. `current` 缺失：返回 `DataFormat`。
 4. 含 null 数值：对应字段为 null 或默认值，其余字段正常。
 
## 7. M4 天气缓存模块
 
### 7.1 存储抽象
 
 ```csharp
 namespace WeatherDesk.Infrastructure.Persistence;
 
 public interface ICacheStorage
 {
     Task<Result<WeatherCacheEntity?>> GetLatestAsync(string locationId, CancellationToken ct);
     Task<Result> UpsertAsync(WeatherCacheEntity entity, CancellationToken ct);
     Task<Result> DeleteAsync(string locationId, CancellationToken ct);
 }
 ```
 
 `EfWeatherCache` 实现 `IWeatherCache`：
 
 1. `SaveAsync`：把 `WeatherSnapshot` 序列化为 JSON（`System.Text.Json`），写入 `WeatherCacheEntity`，主键为 `LocationId`，采用存在即更新的 upsert。
 2. `GetLatestAsync`：读取实体，反序列化；`DataVersion` 与当前版本不一致或反序列化失败时，按“无有效缓存”返回 null 并记录日志，不抛异常。
 3. 存储异常：捕获并返回 `Storage`，同时返回 `Result.Fail`，由 M3 决定降级；`EfWeatherCache` 内部不做重试。
 4. 写入失败不影响读取路径：Save 与 Get 使用独立的 `IDbContextFactory` 创建的上下文。
 
### 7.2 缓存实体与 TTL
 
 | 字段 | 类型 | 说明 |
 |---|---|---|
 | LocationId | TEXT 主键 | Open-Meteo geocoding id |
 | PayloadJson | TEXT | 完整 `WeatherSnapshot` 序列化结果 |
 | DataVersion | INTEGER | 当前 1 |
 | RetrievedAtUtc | TEXT | 写入时的 UTC 时间 |
 | DataUpdatedAtUtc | TEXT | 上游数据时间，用于 UI 显示“最近更新时间” |
 | LocationNameSnapshot | TEXT | 冗余城市名，便于无网络时展示 |
 
 TTL 策略：`CacheFreshnessPolicy` 接受显式 TTL 参数，默认 30 分钟；具体有效期属于待确认项（第 13 节），实现时以常量形式集中在 `CacheDefaults`，避免散落。
 
### 7.3 独立测试方案（CacheTests）
 
 用 `FakeCacheStorage`（内存字典）测试：
 
 1. 保存后读取：得到等价快照，`Provenance = Cache`。
 2. 同城市重复保存：只有一条记录，时间为最新。
 3. `DataVersion` 不匹配：返回 null 且不抛异常。
 4. 反序列化失败（脏 JSON）：返回 null，记录日志。
 5. Storage 抛异常：`SaveAsync` 返回 `Storage`，`GetLatestAsync` 返回 `Storage`。
 6. 新鲜度：存入 40 分钟前的快照，TTL 30 分钟，`IsFresh` 为 false。
 
 PersistenceTests 另用真实 SQLite 文件（临时目录）验证表结构与 upsert 行为，不纳入默认快速测试集以外的网络依赖。
 
## 8. M5 城市管理模块
 
### 8.1 行为规则
 
 | 操作 | 行为 |
 |---|---|
 | 收藏城市 | 写入 `FavoriteCity`，已存在则不重复插入（按 `LocationId` 判定） |
 | 取消收藏 | 按 `LocationId` 删除；删除不存在的记录返回成功，不报错 |
 | 最近访问 | 每次成功选择城市后 upsert，按 `VisitedAtUtc` 倒序返回 |
 | 最近访问上限 | 默认 10 条，超出时删除最早记录（见第 13 节待确认项） |
 | 启动恢复 | 读取 `AppState.LastCityId` 关联收藏或最近访问，取回完整 `Location`；取不到则进入 Idle 等待搜索 |
 
 收藏与最近访问是两张独立表；城市完整信息（名称、经纬度、时区）在两张表中各存一份快照，避免启动恢复时依赖网络。
 
### 8.2 独立测试方案（PersistenceTests + ServiceTests）
 
 1. 收藏后列表包含该城市；重复收藏不产生重复项。
 2. 取消收藏后列表不含该城市；取消不存在的城市返回成功。
 3. 最近访问 upsert 后按时间倒序；超过上限时只保留最新的 N 条。
 4. 写入 `LastCityId` 后重启前读取能还原完整 `Location`。
 5. Store 抛异常：返回 `Storage`，UI 进入 Error 并可重试。
 6. 收藏与最近访问互不影响（收藏城市被删除后仍可留在最近访问中）。
 
## 9. M6 Agent 编排模块
 
### 9.1 工具定义
 
 ```text
 GetWeatherForecast(city, date, range)
 ```
 
 ```csharp
 namespace WeatherDesk.Infrastructure.Agent;
 
 public enum ForecastRange { Day, Hour }   // Day = 指定某天汇总；Hour = 指定某天的逐小时
 
 public sealed record WeatherToolRequest(string? City, DateOnly? Date, ForecastRange Range);
 public sealed record WeatherToolResult(WeatherSnapshot Snapshot, bool IsStale, DateTimeOffset? DataUpdatedAtUtc);
 
 public interface IWeatherTool
 {
     Task<Result<WeatherToolResult>> InvokeAsync(WeatherToolRequest request, CancellationToken ct);
 }
 ```
 
 工具只返回结构化数据；生成自然语言的职责属于 `IAnswerGenerator`。
 
### 9.2 执行流程
 
 ```text
 AskAsync(question, context, ct)
   1. 空问题校验 -> Failure.Input
   2. QuestionAnalyzer.Analyze
      - 抽取 city 文本、date 文本、intent
      - city 文本为空时使用 context.CurrentCity
      - date 文本经 DateResolver 解析；无法解析时为 null
   3. 判断是否需要天气：intent 属于 Commute/Running/Umbrella/Clothing/General 中的天气相关项，
      或问题包含天气关键词（下雨、气温、风、冷、热、雪）
   4. 不需要天气 -> 返回 AgentReply(OutOfScope)，文案见第 13 节待确认项
   5. 需要天气但 city 或 date 缺失 -> AgentReply(NeedClarification)，明确说明缺什么
   6. AgentParameterValidator.Validate
      - city 无法解析为 Location -> 调用 IGeocodingService 搜索，0 结果返回 NeedClarification，多结果取第一条并在 Evidence 中说明
      - date 超出 [today, today+6] -> NeedClarification（7 天预报范围）
   7. IWeatherTool.InvokeAsync
      - 内部复用 WeatherQueryService，因此同样享受缓存降级
      - 失败 -> AgentReply(Error)，保留 Retryable 语义
   8. AgentPromptBuilder.Build(question, intent, toolResult, context)
   9. IAnswerGenerator.GenerateAsync
      - DeepSeek 不可用时按设置决定是否回退 DemoAnswerGenerator
   10. AgentReplyMapper 组装 AgentReply(Answer)，写入 City、Date、Intent、Evidence
 ```
 
### 9.3 提示词与上下文组装
 
 ```csharp
 public sealed record AgentPrompt(
     string SystemPrompt,
     string UserContent,       // 用户问题 + 结构化天气 JSON + 目标城市与日期
     IReadOnlyList<string> Constraints
 );
 ```
 
 `UserContent` 中包含的最小结构化字段：城市名、目标日期、当日最高/最低温、降水概率、降水量、最大风速、天气状况描述、逐小时摘要（仅目标日期 06:00-22:00）。不发送收藏列表、最近访问、API Key 和无关城市数据。
 
 System 约束固定包含：只回答与天气和出行相关的内容；必须引用给定结构化数据；不编造未提供的数据；输出中文，控制在 200 字以内并给出 2-3 条具体建议。
 
### 9.4 演示模式回答生成
 
 `DemoAnswerGenerator` 是纯函数式生成器，输入结构化天气与意图，输出固定模板回答：
 
 1. 用 `WeatherCodeCatalog` 把 weather_code 转为中文描述。
 2. 用 `AdviceRuleSet` 按意图生成要点（带伞、保暖、防风、适宜/不适宜运动）。
 3. 拼接为“城市 + 日期 + 天气概况 + 建议要点 + 数据来源与更新时间”。
 4. 明确标注演示模式，避免用户误以为是真实模型输出。
 
### 9.5 独立测试方案（AgentTests）
 
 全部使用 Fake（`FakeWeatherService`、`FakeWeatherCache`、`FakeGeocodingService`、`FakeAnswerGenerator`、`FakeClock`）：
 
 1. 缺城市：返回 `NeedClarification`，且未调用天气工具。
 2. 缺日期：返回 `NeedClarification`，且未调用天气工具。
 3. 完整问题：调用工具 1 次，返回 `Answer`，`Evidence` 非空，`City` 与 `Date` 正确。
 4. 工具失败：返回 `Error`，`Retryable` 与底层一致。
 5. 日期超出 7 天：返回 `NeedClarification`，不调用工具。
 6. 非天气问题：返回 `OutOfScope`，不调用工具。
 7. 城市名无法解析：调用 `IGeocodingService`，0 结果返回 `NeedClarification`。
 8. 缓存降级：上游失败但有缓存，Agent 仍给出回答并在文本中说明数据非实时。
 9. 取消：`ct` 取消后返回 `Canceled`。
 10. 演示模式：`IAnswerGenerator` 不可用时使用 Demo，输出包含演示模式标识。
 *** End Patch
## 10. M7 AI 适配模块
 
### 10.1 接口与选择逻辑
 
 ```csharp
 namespace WeatherDesk.Infrastructure.Ai;
 
 public sealed class AnswerGeneratorSelector : IAnswerGenerator
 {
     public AnswerGeneratorSelector(DeepSeekClient deepSeek, DemoAnswerGenerator demo, ISettingsStore settingsStore);
 
     public bool IsAvailable(AppSettings settings);
     public Task<Result<string>> GenerateAsync(AgentPrompt prompt, AppSettings settings, CancellationToken ct);
 }
 ```
 
 选择规则：
 
 | 设置 | 行为 |
 |---|---|
 | `Mode = Demo` | 直接使用 `DemoAnswerGenerator` |
 | `Mode = DeepSeek` 且无 API Key | 返回 `AiUnauthorized`，UI 提示去设置页配置或切换演示模式 |
 | `Mode = DeepSeek` 且有 API Key | 使用 `DeepSeekClient`；失败按 10.3 规则处理 |
 | `Mode = Auto` | 有 API Key 用 DeepSeek，否则用 Demo |
 
### 10.2 DeepSeek 请求契约
 
 ```text
 POST {BaseUrl}/chat/completions
 Authorization: Bearer {API_KEY}
 Content-Type: application/json
 
 {
   "model": "deepseek-chat",
   "messages": [
     { "role": "system", "content": "<system 约束>" },
     { "role": "user", "content": "<用户问题 + 结构化天气>" }
   ],
   "temperature": 0.3,
   "max_tokens": 500,
   "stream": false
 }
 ```
 
 响应解析：取 `choices[0].message.content`；缺失、空字符串或结构异常时返回 `AiMalformed`。
 
### 10.3 失败处理
 
 | 情况 | FailureKind | UI 行为 |
 |---|---|---|
 | 401 或 403 | `AiUnauthorized` | 提示 API Key 无效，提供前往设置与切换演示模式 |
 | 408、429、5xx、超时、网络错误 | `AiUnavailable` | 提示重试；由用户决定是否切演示模式 |
 | 2xx 但 JSON 结构异常 | `AiMalformed` | 安全降级为普通文本错误提示，不展示模型原始内容 |
 | `Mode = Auto` 且 DeepSeek 失败 | 按 Auto 语义回退 `DemoAnswerGenerator`，并在回答中标注 | 用户可见数据来源 |
 
 自动回退只发生在 `Mode = Auto`；`Mode = DeepSeek` 时不静默回退，避免用户误以为调用的是真实模型。
 
### 10.4 独立测试方案（ServiceTests）
 
 用 Stub `HttpMessageHandler` 注入 `HttpClient`：
 
 1. 正常 2xx：返回模型文本；请求体包含 system 与 user 两条消息。
 2. 401：返回 `AiUnauthorized`；日志中不出现完整 API Key。
 3. 500：返回 `AiUnavailable`，`Retryable = true`。
 4. 2xx 但 `choices` 为空：返回 `AiMalformed`。
 5. 取消：返回 `Canceled`。
 6. 选择器：`Mode = Demo` 时不发起任何 HTTP 请求；`Mode = Auto` 且无 Key 时使用 Demo。
 7. 演示生成器：相同结构化输入在不同时间下输出稳定（不依赖随机与时间）。
 
## 11. M8 配置与 M9 本地持久化模块
 
### 11.1 存储位置
 
 ```csharp
 namespace WeatherDesk.Infrastructure.Configuration;
 
 public static class AppPaths
 {
     public static string RootDirectory { get; }        // %LOCALAPPDATA%\WeatherDesk
     public static string DatabasePath { get; }         // RootDirectory\weatherdesk.db
     public static string SettingsPath { get; }         // RootDirectory\settings.json
 }
 ```
 
 目录在应用启动时创建；创建失败时降级为内存设置与内存缓存，并在状态区提示“本地存储不可用，本次数据不会保存”。
 
### 11.2 设置存储
 
 ```csharp
 public sealed class JsonSettingsStore : ISettingsStore
 {
     public Task<Result<AppSettings>> LoadAsync(CancellationToken ct);
     public Task<Result> SaveAsync(AppSettings settings, CancellationToken ct);
     public Task<Result<string?>> GetApiKeyAsync(CancellationToken ct);
     public Task<Result> SetApiKeyAsync(string apiKey, CancellationToken ct);
 }
 ```
 
 规则：
 
 1. `settings.json` 只保存 Mode、BaseUrl、Model、Unit、Theme；API Key 不写入该文件。
 2. API Key 的存储方式属于待确认项（第 13 节）：默认方案为 Windows 下使用 `ProtectedData.Protect` 加密后写入 `credentials.dat`，当前用户可解密；如你选择明文配置文件，请明确告知，我会改为普通 JSON 字段并补充gitignore 规则。
 3. 设置文件损坏或字段缺失：回退为默认设置并在状态区提示，不阻止应用启动。
 4. 保存失败：返回 `Storage`，设置页显示错误，应用其余功能不受影响。
 5. `SecretProtector` 负责加密与解密；解密失败按“未配置 API Key”处理，不抛出到 UI。
 
### 11.3 数据库结构
 
 ```csharp
 namespace WeatherDesk.Infrastructure.Persistence;
 
 public sealed class AppDbContext : DbContext
 {
     public DbSet<FavoriteCityEntity> FavoriteCities { get; set; }
     public DbSet<RecentCityEntity> RecentCities { get; set; }
     public DbSet<WeatherCacheEntity> WeatherCaches { get; set; }
     public DbSet<AppStateEntity> AppStates { get; set; }
 }
 ```
 
 | 表 | 字段 | 键与索引 |
 |---|---|---|
 | `FavoriteCities` | Id(INTEGER PK)、LocationId(TEXT)、Name、Admin1、Country、CountryCode、Latitude(REAL)、Longitude(REAL)、Timezone、UtcOffsetSeconds(INTEGER)、AddedAtUtc(TEXT) | `LocationId` 唯一索引 |
 | `RecentCities` | Id(INTEGER PK)、LocationId(TEXT)、Name、Admin1、Country、CountryCode、Latitude、Longitude、Timezone、UtcOffsetSeconds、VisitedAtUtc(TEXT) | `LocationId` 唯一索引；`VisitedAtUtc` 索引 |
 | `WeatherCaches` | LocationId(TEXT PK)、PayloadJson(TEXT)、DataVersion(INTEGER)、RetrievedAtUtc(TEXT)、DataUpdatedAtUtc(TEXT)、LocationNameSnapshot(TEXT) | 主键即 LocationId |
 | `AppStates` | Key(TEXT PK)、Value(TEXT) | 主键 Key，例如 `LastCityId` |
 
 迁移策略：MVP 使用 `Database.EnsureCreated()` + `PRAGMA user_version` 记录；正式迁移是否改用 EF Core Migrations 属于待确认项（第 13 节）。
 
### 11.4 持久化失败降级
 
 1. 数据库打开失败：`ICityStore`、`IWeatherCache`、`IAppStateStore` 返回 `Storage`，应用可继续使用网络天气与 AI 功能。
 2. 单次写入失败：只影响该功能（如收藏不落盘），状态区提示一次，不弹阻断式错误。
 3. 所有 EF 调用使用短命令超时（5 秒），避免 UI 长时间等待。
 
### 11.5 独立测试方案（PersistenceTests）
 
 使用临时目录中的真实 SQLite 文件，逐个用例独立建库：
 
 1. 建库后四张表可用；重复启动不丢数据。
 2. 收藏唯一索引：重复收藏不产生重复行。
 3. 最近访问上限：插入 15 条后按 `VisitedAtUtc` 只保留最新 10 条。
 4. 缓存 upsert：同城市第二次写入更新时间与 Payload。
 5. `AppStates` 读写：设置与读取 `LastCityId` 成功。
 6. 设置存储：保存后重新加载一致；API Key 不出现在 `settings.json` 文本中（断言文件内容）。
 7. 损坏的 `settings.json`：加载返回默认设置且 `IsSuccess = true`。
 
## 12. M10 桌面展示模块
 
### 12.1 ViewModel 结构
 
 ```csharp
 public abstract class ViewModelBase : ObservableObject
 {
     protected readonly ILogger Logger;
     protected CancellationTokenSource? CurrentOperation;   // 同一操作重复触发时先取消
     protected async Task RunExclusiveAsync(Func<CancellationToken, Task> action);
 }
 ```
 
 | ViewModel | 主要状态属性 | 主要命令 |
 |---|---|---|
 | `MainWindowViewModel` | `CurrentCity`、`WeatherStatus`、`StatusMessage`、`NetworkState` | `RefreshCommand`、`OpenSettingsCommand` |
 | `WeatherViewModel` | `Status`、`Snapshot`、`Hourly`、`Daily`、`IsStale`、`DataUpdatedAtText`、`SourceText` | `RetryCommand` |
 | `CitySearchViewModel` | `Keyword`、`Status`、`Results` | `SearchCommand`、`SelectCityCommand` |
 | `CityListViewModel` | `Favorites`、`Recents` | `ToggleFavoriteCommand`、`SelectCityCommand` |
 | `ChatViewModel` | `Messages`、`InputText`、`IsSending`、`LastError`、`QuickQuestions` | `SendCommand`、`UseQuickQuestionCommand`、`RetryCommand`、`SwitchToDemoCommand` |
 | `SettingsViewModel` | `Mode`、`BaseUrl`、`Model`、`ApiKeyInput`、`MaskedApiKey`、`Unit`、`Theme` | `SaveCommand`、`ClearApiKeyCommand`、`TestConnectionCommand` |
 
 通信方式：主窗口持有子 ViewModel，通过领域事件式方法调用传递城市变更（`MainWindowViewModel.OnCitySelected(Location)`），不使用静态消息总线，避免隐藏耦合。
 
### 12.2 状态转换规则
 
 ```mermaid
 stateDiagram-v2
     [*] --> Idle
     Idle --> Loading: 发起请求
     Loading --> Success: 拿到最新数据
     Loading --> Empty: 结果为空
     Loading --> Error: 失败且无缓存
     Loading --> Stale: 失败但有缓存
     Stale --> Loading: 点击重试或刷新
     Error --> Loading: 点击重试
     Success --> Loading: 切换城市或刷新
     Empty --> Loading: 修改关键词或重试
 ```
 
 规则：
 
 1. 每个异步区域独立维护状态，天气失败不会把对话面板切到 Error。
 2. `Stale` 必须同时显示“最近更新时间”和“数据可能已过期”。
 3. `Loading` 时禁用触发该操作的按钮，避免重复请求。
 4. 取消操作回到上一个有效状态，而不是 Error。
 
### 12.3 展示层换算与格式化
 
 1. 温度按 `AppSettings.Unit` 在 `Converters.TemperatureConverter` 中换算，领域模型始终是摄氏度。
 2. 时间展示：使用城市时区（`Location.Timezone`）格式化逐小时与日出日落；无法确定时区时回退本地时区。
 3. 天气图标与配色由 `WeatherCodeCatalog.IconKey` 驱动，浅色与深色主题各有一套资源字典，不使用透明度叠加文本。
 
### 12.4 独立测试方案（AppTests）
 
 ViewModel 测试不启动 Avalonia 应用，只依赖 `CommunityToolkit.Mvvm` 与 Fake 服务；若属性通知需要 UI 线程，则通过 `Dispatcher.UIThread` 相关抽象注入（测试用同步调度器）。
 
 1. `WeatherViewModel`：上游成功 → Success；上游失败无缓存 → Error；上游失败有缓存 → Stale 且显示更新时间。
 2. 重试命令：Error 状态下执行 → Loading → Success。
 3. `CitySearchViewModel`：空关键词 → 不调用服务；0 结果 → Empty；选择城市 → 触发主窗口城市变更。
 4. `CityListViewModel`：收藏与取消收藏更新列表；Store 失败 → 显示错误且不崩溃。
 5. `ChatViewModel`：发送消息后 `Messages` 增加用户与助手两条；Agent 返回 `NeedClarification` 时展示追问；`Error` 时展示重试与切演示入口。
 6. `SettingsViewModel`：保存后 `MaskedApiKey` 脱敏；清空 Key 后 `HasApiKey = false`；主题与单位变更写入设置。
 
## 13. 待确认项（需要你确认后才能定稿）
 
 以下条目需求文档与概要设计都没有给出确定规则，我在文中按“默认值 + 集中常量”的方式预留，但不视为已确认：
 
 1. 缓存有效期（TTL）：当前默认 30 分钟；当前天气、逐小时、7 天是否使用不同 TTL。
 2. 最近访问城市保留条数：当前默认 10 条。
 3. API Key 本机存储方式：默认 Windows DPAPI 加密文件；是否接受明文 `settings.json`。
 4. 非天气问题的处理：默认返回 OutOfScope 固定文案；是否需要允许简短通用回复。
 5. 真实模式与演示模式的切换：默认设置项 `AiMode = Auto/Demo/DeepSeek`；是否只要一个开关。
 6. AI 对话是否跨重启恢复：当前设计只保存在内存，重启后清空。
 7. SQLite 与配置文件路径：当前默认 `%LOCALAPPDATA%\WeatherDesk`；是否需要放在程序目录或支持自定义。
 8. 数据库迁移方式：MVP 用 `EnsureCreated` + `user_version`；是否需要 EF Core Migrations。
 9. 能见度字段：Open-Meteo 是否包含可用 visibility 变量；若无，是否接受显示“暂无”。
 10. 搜索结果数量与多结果策略：当前默认取 10 条，Agent 遇到多结果取第一条；是否应让用户选择。
 11. 是否需要在设置页提供“测试 DeepSeek 连接”按钮：当前设计包含该命令，但需求未要求。
 12. 主题切换是否需要运行时立即生效：当前设计为立即生效并保存。
