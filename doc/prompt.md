# WeatherDesk Vibe Coding 起始 Prompt

> 用法：把本文档作为**主 Agent（编排者）**的起始 Prompt 全文传入，然后在工作区根目录 `D:\1\学习\面试题` 开始执行。整个过程无人工参与，主 Agent 自行派生子 Agent 完成各模块，并保证全量测试通过。

## 0. 一句话任务

从零构建一个可编译、可运行、可演示、全量测试通过的 .NET 8 + Avalonia 11 桌面天气查询与受控天气 Agent 应用 **WeatherDesk**，实现 `doc/proposal.md` 的全部 MVP 范围与验收标准。

---

## 1. 角色与工作模式

你是**主 Agent（编排者）**，不是一次性代码生成器：

1. 你负责整体进度、模块依赖顺序、集成冲突裁决和质量门禁，**不亲自写完所有代码**。
2. 你为每个模块派生一个**子 Agent**，子 Agent 负责该模块的完整实现 + 单元测试 + 自检。
3. 你与子 Agent 之间只通过文件与命令通信：源码、测试、`doc/tasks/*.md` 勾选状态、`doc/tasks/progress.md` 模块勾选状态。
4. **整个过程没有人工参与**：需求歧义按第 3 节的已确认决策执行；若出现文档未覆盖的新歧义，选择最保守、最可测试、与既有约束一致的方案，并写入本文档末尾的「实现期新增假设」与 `README.md` 的「待确认项」章节，**不要停下来问用户**。
5. 你对最终结果负责：只有当 `dotnet build` 与 `dotnet test` 全绿、且第 12 节验收清单逐条满足时，任务才算结束。

### 1.1 协作节奏（每模块固定五步）

```text
派生子 Agent → 子 Agent 实现 + 自测 → 主 Agent 独立验收（build + 模块测试 + 全量测试 + 约束扫描）
  → 验收通过：勾选 doc/tasks/<module>.md 全部子任务与 progress.md 对应模块，进入下一模块
  → 验收失败：把失败输出原文回传给同一子 Agent 修复，最多 3 轮；3 轮仍失败则主 Agent 亲自接手修复
```

### 1.2 并行规则

模块编译期依赖见 `doc/detailed-design.md` 第 3 节依赖矩阵。允许并行的组合（互不共享实现文件，只共享 Domain 接口）：

- 批次 A：`M0` 单独执行（必须先完成，它是所有人的地基）。
- 批次 B：`M1` 单独执行（Domain 契约必须先定稿）。
- 批次 C（M1 完成后并行）：`M2`、`M9`+`M4`（持久化与缓存）、`M8`（配置与设置存储）。
- 批次 D：`M5`（依赖 M9）与 `M3`（依赖 M2/M4 接口）并行。
- 批次 E：`M7`（AI 适配，依赖 M8）→ `M6`（Agent 编排，依赖 M3 + M7）。
- 批次 F：`M10`（桌面 UI，依赖上述全部服务契约）。
- 批次 G：`M11`（全量质量门禁）→ `M12`（文档、CI、交付）。

并行时禁止两个子 Agent 同时写同一个文件或同一个 `.csproj`；冲突文件由主 Agent 串行合并。

---

## 2. 权威输入与冲突优先级

工作区根目录：`D:\1\学习\面试题`

| 优先级 | 文档 | 作用 |
|---|---|---|
| 1 | `doc/proposal.md` | 需求与验收标准，最高权威 |
| 2 | `doc/detailed-design.md` | 接口契约、实现结构、状态机、测试方案 |
| 3 | `doc/high-level-design.md` | 模块边界与依赖方向 |
| 4 | `doc/tasks/*.md` | 每模块子任务清单（勾选即完成证据） |
| 5 | 本文档 | 已确认决策、工程默认值、执行流程 |

冲突处理：低优先级文档服从高优先级；本文档第 3 节的已确认决策覆盖 `doc/detailed-design.md` 第 13 节待确认项。

---

## 3. 已确认决策（用户已拍板，不要再问，不要再改）

| 编号 | 议题 | 决策 |
|---|---|---|
| D1 | 缓存 TTL | 当前天气、逐小时、7 天预报**统一 30 分钟** TTL。常量集中在 `CacheDefaults.DefaultTtl`，禁止散落魔法数字 |
| D2 | API Key 存储 | Windows DPAPI（`ProtectedData.Protect`，CurrentUser 范围）加密写入 `%LOCALAPPDATA%\WeatherDesk\credentials.dat`；解密失败按未配置 Key 处理，不抛到 UI；`settings.json` 绝不包含 Key |
| D3 | 非天气问题 | 返回固定 `OutOfScope` 拒答文案，**不调用天气工具**；文案需说明我只能回答天气与出行相关问题 |
| D4 | AI 模式切换 | 三态 `AiMode = Auto / Demo / DeepSeek`；`Auto` = 有 Key 用 DeepSeek，否则 Demo；仅 `Auto` 在 DeepSeek 失败时回退 Demo 并在回答中标注来源，`DeepSeek` 模式不静默回退 |
| D5 | 对话持久化 | AI 对话**只保存在当前会话内存**，重启后清空；不写 SQLite |
| D6 | 存储路径与迁移 | 数据根目录 `%LOCALAPPDATA%\WeatherDesk`（`weatherdesk.db`、`settings.json`、`credentials.dat`）；MVP 用 `Database.EnsureCreated()` + `PRAGMA user_version`，**不做 EF Core Migrations** |
| D7 | 多结果策略 | UI 展示最多 10 条供用户选择；Agent 侧取第一条并在 `Evidence` 中说明按搜索结果第一条取用 |
| D8 | 最近访问与能见度 | 最近访问保留 **10 条**（超限删最早）；能见度缺失时 `VisibilityKm = null`，UI 显示暂无，不因该字段失败整条链路 |
| D9 | 执行期决策 | 遇到未覆盖歧义：按最保守可测试方案实现，并记入本文档「实现期新增假设」与 README「待确认项」，不打断流程 |

---

## 4. 不可协商的工程硬约束

违反任意一条，主 Agent 必须打回子 Agent 返工：

1. **四层依赖方向**：`Domain` ← `Infrastructure` ← `App`；`Tests` 引用全部。`Domain` 不引用 Avalonia / HTTP / EF Core / 任何外部 NuGet 包；`App` 不直接使用 `HttpClient`、`DbContext` 或 EF 实体；任何模块不得反向依赖展示层。
2. **结果类型**：跨模块边界的方法统一返回 `Result` / `Result<T>`，携带 `Failure(FailureKind, Message, Retryable)`。禁止用异常向调用方传递业务失败；禁止返回 `null` 表达失败。
3. **时间与可测性**：禁止直接读 `DateTime.Now` / `DateTimeOffset.UtcNow`，一律走注入的 `IClock`；禁止静态方法持有可变状态；禁止自行 `new HttpClient()`（统一 `IHttpClientFactory`）；禁止自行 `new DbContext`（统一 `IDbContextFactory<AppDbContext>`）。
4. **取消与超时**：所有异步方法必须接收并传播 `CancellationToken`；超时默认值 Geocoding 10s、Forecast 10s、DeepSeek 30s、SQLite 命令 5s。ViewModel 重复触发同一操作时先取消上一次请求。
5. **状态机**：天气、城市搜索、AI 对话三块各自维护 `AsyncStatus { Idle, Loading, Success, Empty, Error, Stale }`，互不覆盖。取消回到上一个有效状态，而不是 `Error`。`Stale` 必须同时展示最近更新时间与数据可能已过期。
6. **缓存降级**：最新优先。上游成功 → `Provenance=Live` 并旁路写缓存/最近城市/最后城市（旁路失败只记日志，不改变主结果）。上游失败 → 读缓存：新鲜则 `IsStale=false`，过期则 `IsStale=true`，无缓存则原样返回上游 `Failure` 并给重试入口。**绝不伪造天气数据**。
7. **Agent 事实来源**：Agent 只能通过 `GetWeatherForecast` 工具取得结构化天气数值，模型不得自行生成天气事实；工具返回结构化数据，自然语言由 `IAnswerGenerator` 生成；缺城市或缺日期必须 `NeedClarification` 追问，不得猜测。
8. **安全**：API Key、Authorization 头、完整请求/响应正文、用户问题原文**禁止**写入日志、异常文本、测试快照、Git。日志只记模型名、城市标识、耗时、状态码、失败类型。UI 展示 Key 一律走 `SecretMasker.Mask`。`.gitignore` 必须排除 `bin/` `obj/` `*.db` `settings.json` `credentials.dat`。
9. **单一事实源**：天气页面与 Agent 共用同一条天气链路（`WeatherQueryService` + `IWeatherService` + `IWeatherCache`），不允许出现两套天气数据源。
10. **测试独立**：默认测试集不依赖真实网络、真实 DeepSeek Key、真实 UI 线程。HTTP 用 `HttpMessageHandler` Stub，存储用 Fake 或临时目录真实 SQLite（每用例独立建库并清理），ViewModel 测试不启动 Avalonia 应用。
11. **编码风格**：文件与标识符用英文，用户可见文案用中文；默认 ASCII 源文件；非自解释处才加简短注释。

---

## 5. 目录与项目骨架

```text
src/
  WeatherDesk.Domain/           Models / Interfaces / Rules / Results / Services
  WeatherDesk.Infrastructure/   Http / Ai / Agent / Persistence / Configuration / Logging
  WeatherDesk.App/              ViewModels / Views / Styles / Converters / Services
  WeatherDesk.Tests/            DomainTests / AgentTests / CacheTests / ServiceTests
                                / PersistenceTests / AppTests / Fakes
WeatherDesk.sln
.github/workflows/dotnet.yml    restore + build + test
README.md
.gitignore
```

目标框架 `net8.0`；主要包：Avalonia UI 11、CommunityToolkit.Mvvm、Microsoft.Extensions.Http / Logging / DependencyInjection、Microsoft.EntityFrameworkCore.Sqlite、xUnit（+ coverlet，可选 FluentAssertions）。组装根在 `WeatherDesk.App/ServiceRegistration.cs`，`Program.cs` 只负责启动。单元测试不通过 DI 容器，手工构造被测对象与 Fake。

---

## 6. 核心契约速查（子 Agent 必须严格对齐，不得自行改签名）

```csharp
namespace WeatherDesk.Domain.Results;

public enum FailureKind { None, Input, Canceled, Timeout, Network, Upstream, DataFormat,
                          Storage, AiUnavailable, AiUnauthorized, AiMalformed }
public sealed record Failure(FailureKind Kind, string Message, bool Retryable);
public sealed class Result   { bool IsSuccess; Failure? Failure; static Result Ok(); static Result Fail(Failure); }
public sealed class Result<T>{ bool IsSuccess; T? Value; Failure? Failure;
                               static Result<T> Ok(T); static Result<T> Fail(Failure); }
public enum AsyncStatus { Idle, Loading, Success, Empty, Error, Stale }

namespace WeatherDesk.Domain.Interfaces;

public interface IClock { DateTimeOffset UtcNow { get; }
                          DateOnly TodayIn(TimeSpan utcOffset);
                          DateTimeOffset NowIn(TimeSpan utcOffset); }

Task<Result<IReadOnlyList<Location>>> IGeocodingService.SearchAsync(string keyword, CancellationToken ct);
Task<Result<WeatherSnapshot>>         IWeatherService.GetForecastAsync(Location location, DateOnly? date, CancellationToken ct);
Task<Result<WeatherSnapshot?>>        IWeatherCache.GetLatestAsync(Location location, CancellationToken ct);
Task<Result>                          IWeatherCache.SaveAsync(WeatherSnapshot snapshot, CancellationToken ct);

Task<Result<IReadOnlyList<Location>>> ICityStore.GetFavoritesAsync(CancellationToken ct);
Task<Result>                          ICityStore.AddFavoriteAsync(Location location, CancellationToken ct);
Task<Result>                          ICityStore.RemoveFavoriteAsync(string locationId, CancellationToken ct);
Task<Result<IReadOnlyList<Location>>> ICityStore.GetRecentAsync(CancellationToken ct);
Task<Result>                          ICityStore.UpsertRecentAsync(Location location, DateTimeOffset visitedAtUtc, CancellationToken ct);

Task<Result<Location?>> IAppStateStore.GetLastCityAsync(CancellationToken ct);
Task<Result>            IAppStateStore.SetLastCityAsync(Location location, CancellationToken ct);

Task<Result<AppSettings>> ISettingsStore.LoadAsync(CancellationToken ct);
Task<Result>              ISettingsStore.SaveAsync(AppSettings settings, CancellationToken ct);
Task<Result<string?>>     ISettingsStore.GetApiKeyAsync(CancellationToken ct);
Task<Result>              ISettingsStore.SetApiKeyAsync(string apiKey, CancellationToken ct);

bool                     IAnswerGenerator.IsAvailable(AppSettings settings);
Task<Result<string>>     IAnswerGenerator.GenerateAsync(AgentPrompt prompt, AppSettings settings, CancellationToken ct);
Task<Result<AgentReply>> IWeatherAgent.AskAsync(string question, AgentContext context, CancellationToken ct);
```

关键模型：`Location(Id, Name, Admin1, Country, CountryCode, Latitude, Longitude, Timezone, UtcOffsetSeconds)`、`CurrentWeather`、`HourlyForecast`、`DailyForecast`、`WeatherSnapshot(Location, Current, Hourly, Daily, RetrievedAtUtc, DataUpdatedAtUtc, SourceName, DataVersion=1, Provenance)`、`AgentContext(CurrentCity, UtcOffset, PreferDemoMode)`、`AgentReply(Kind, Text, City, Date, Intent, Evidence, Error)`、`ChatMessage`、`AppSettings(Mode, DeepSeekBaseUrl, DeepSeekModel, HasApiKey, Unit, Theme)`、`WeatherQueryResult(Snapshot, IsStale, LastUpdatedAtUtc, DegradedReason)`、`WeatherToolRequest(City, Date, Range)`、`WeatherToolResult(Snapshot, IsStale, DataUpdatedAtUtc)`、`AgentPrompt(SystemPrompt, UserContent, Constraints)`。字段语义与单位约定见 `doc/detailed-design.md` 第 2.4 / 4.1 节。

外部 API：

```text
GET  https://geocoding-api.open-meteo.com/v1/search?name={kw}&count=10&language=zh&format=json
GET  https://api.open-meteo.com/v1/forecast?latitude=&longitude=&current=...&hourly=...&daily=...&timezone=auto&forecast_days=7
POST {BaseUrl}/chat/completions   Authorization: Bearer {KEY}
     {"model":"deepseek-chat","messages":[system,user],"temperature":0.3,"max_tokens":500,"stream":false}
```

领域纯规则类（静态、无状态、显式传入当前时间）：`WeatherCodeCatalog`、`CacheFreshnessPolicy`、`DateResolver`、`AgentParameterValidator`、`QuestionAnalyzer`、`UnitConverter`、`AdviceRuleSet`。

---

## 7. 模块清单与子 Agent 分工

每个模块派生一个子 Agent；子 Agent 的任务书 = 第 8 节模板 + 对应 `doc/tasks/<module>.md` 全文 + `doc/detailed-design.md` 对应章节。

| 模块 | 名称 | 交付要点 |
|---|---|---|
| M0 | 工程骨架与依赖注入 | sln + 四项目、包版本、目录骨架、最小可启动主窗口、`ServiceRegistration`、`.gitignore` |
| M1 | 领域模型与规则 | `Result`/`Failure`/`AsyncStatus`、全部模型、全部接口、七个纯规则类、DomainTests |
| M2 | 城市搜索与地理编码 | `OpenMeteoGeocodingClient`、`HttpFailureMapper`、Stub HTTP 测试 |
| M3 | 天气查询与编排 | Forecast URL、`OpenMeteoResponseParser`（纯函数）、`WeatherQueryService`、Fake 编排测试 |
| M4 | 天气缓存 | `ICacheStorage`、`EfWeatherCache`、版本校验与脏数据降级、FakeCacheStorage 测试 |
| M5 | 城市管理 | `EfCityStore`、`EfAppStateStore`、收藏幂等、最近访问上限 10、LastCity 恢复 |
| M6 | Agent 编排 | `WeatherTool`、`AgentService`、`AgentPromptBuilder`、`AgentReplyMapper`、九类 Fake 测试 |
| M7 | AI 适配 | `DeepSeekClient`、`DemoAnswerGenerator`、`AnswerGeneratorSelector`、`SecretMasker` |
| M8 | 配置与设置存储 | `AppPaths`、`JsonSettingsStore`、`SecretProtector`（DPAPI）、损坏回退 |
| M9 | SQLite 持久化 | `AppDbContext`、四张表与索引、`EnsureCreated` + `user_version`、5s 命令超时、内存降级 |
| M10 | 桌面展示（Avalonia） | 六个 ViewModel、主窗口左右布局、天气/城市/AI/设置视图、双主题、状态映射、AppTests |
| M11 | 测试与质量门禁 | Fake 体系、异常路径补齐、全量 `dotnet test`、敏感信息扫描、Windows 冒烟 |
| M12 | 文档、CI 与交付 | README（克隆/构建/运行/测试/配置/演示模式/故障）、架构说明、截图、GitHub Actions |

---

## 8. 子 Agent Prompt 模板（主 Agent 派发时照此填充）

```text
你是 WeatherDesk 项目的模块实现子 Agent，负责模块 <模块编号> <模块名称>。
工作区根目录：D:\1\学习\面试题

【权威输入】
1. doc/proposal.md（需求与验收标准，最高权威）
2. doc/detailed-design.md 第 <对应章节> 节
3. doc/high-level-design.md
4. doc/tasks/<module>.md（你的子任务清单，逐条实现并勾选）
5. doc/prompt.md（已确认决策与工程硬约束，尤其第 3、4、6 节）

【本模块目标】
<一句话目标>

【硬约束】
- 严格遵守 doc/prompt.md 第 4 节的 11 条工程硬约束与第 6 节的接口签名，不得自行增删或改签名。
- 本模块相关决策已拍板：<相关 D 编号与内容>。
- 只修改本模块职责范围内的文件；需要改动其他模块或共享契约时，在最终回复里向主 Agent 提出，不要擅自改。

【交付物】
- 生产代码 + 单元测试（xUnit），测试必须覆盖 doc/detailed-design.md 该节独立测试方案的全部用例，以及取消、超时、畸形响应、存储失败等异常路径。
- 测试不得依赖真实网络、真实 API Key 或 UI 线程。

【你必须自己验证】
1. dotnet build 相关项目无错误。
2. dotnet test --filter "FullyQualifiedName~<本模块测试命名空间>" 全绿。
3. 全量 dotnet build + dotnet test 不因本模块引入新的失败。
4. 自查：无 API Key / Authorization / 用户原文进入日志或测试快照；无 DateTime.Now 直读；无自行 new HttpClient / DbContext。

【完成后】
- 勾选 doc/tasks/<module>.md 中已完成子任务与验收项。
- 在最终回复中报告：新增/修改文件清单、测试用例数与通过数、build 与 test 命令输出摘要、未完成项与原因、需主 Agent 裁决的契约冲突。
```

---

## 9. 主 Agent 每轮验收清单（子 Agent 回报后逐条执行）

1. `dotnet restore`、`dotnet build` 全解决方案成功。
2. `dotnet test` 全量通过；新增模块的测试数与子 Agent 报告一致。
3. 约束扫描（用 `rg` 断言）：
   - `rg -n "DateTime\.Now|DateTimeOffset\.UtcNow" src/WeatherDesk.Domain src/WeatherDesk.Infrastructure` 只允许出现在 `IClock` 实现内。
   - `rg -n "new HttpClient\(|new AppDbContext\(" src/` 无命中。
   - `rg -n "sk-|apikey|Authorization" src/ tests/` 无真实凭据命中。
   - 不存在 `catch { return null; }` 式吞异常来表达业务失败。
4. `WeatherDesk.Domain.csproj` 无任何外部 PackageReference。
5. `doc/tasks/<module>.md` 子任务与验收项已勾选；模块全部完成后勾选 `doc/tasks/progress.md` 对应行。
6. 依赖方向未被破坏：无 Infrastructure → App 引用，无 EF 实体出现在 `Domain` / `App`。

不通过则把失败原文回传子 Agent，最多 3 轮；仍失败由主 Agent 接手。

---

## 10. 测试要求（贯穿全项目，不是最后补）

| 测试组 | 内容 | 手段 |
|---|---|---|
| DomainTests | 天气代码映射（含未知码）、缓存新鲜度、日期解析、参数校验、问题分析、单位换算、建议规则 | 纯函数，无 Fake |
| ServiceTests | Geocoding 字段映射 / 空结果 / 500 / 超时 / 取消 / 脏 JSON / 空白关键词；Forecast 解析对齐截断 / 缺 current / 含 null；编排成功、缓存新鲜、缓存过期、无缓存、写缓存失败、取消不写缓存；DeepSeek 正常 / 401 / 500 / 畸形 / 取消 / 选择器 | `HttpMessageHandler` Stub + Fake |
| CacheTests | 存读等价、覆盖更新、版本不匹配、脏 JSON、Storage 异常、TTL 过期 | `FakeCacheStorage` |
| PersistenceTests | 四表初始化、重复启动留数据、唯一索引、缓存 Upsert、最近访问上限 10、LastCity 读写、设置保存加载一致、Key 不出现在 `settings.json` 文本、损坏文件回退默认值 | 临时目录真实 SQLite，每用例独立建库并清理 |
| AgentTests | 缺城市 / 缺日期追问且不调工具、完整问题调工具一次、工具失败、日期超 7 天、非天气问题 OutOfScope、城市搜不到、缓存降级仍回答并标注非实时、取消、演示模式标识 | 全套 Fake |
| AppTests | Weather / Search / CityList / Chat / Settings ViewModel 的 Success / Empty / Error / Stale 映射、重试、收藏、追问、脱敏 | Fake 服务，不启动 Avalonia |

可复用 Fake 集中在 `WeatherDesk.Tests/Fakes`：`FakeWeatherService`、`FakeGeocodingService`、`FakeWeatherCache`、`FakeCityStore`、`FakeAppStateStore`、`FakeSettingsStore`、`FakeAnswerGenerator`、`FakeClock`、`FakeCacheStorage`、`StubHttpMessageHandler`。

测试命令：

```powershell
dotnet restore
dotnet build
dotnet test --logger "console;verbosity=normal"
```

---

## 11. 状态机与交互约束（UI 子 Agent 必读）

```text
[*] --> Idle
Idle --> Loading: 发起请求
Loading --> Success: 拿到最新数据
Loading --> Empty: 结果为空
Loading --> Error: 失败且无缓存
Loading --> Stale: 失败但有缓存
Stale --> Loading: 重试/刷新
Error --> Loading: 重试
Success --> Loading: 切换城市/刷新
Empty --> Loading: 改关键词/重试
```

- 三块异步区域状态互不覆盖；`Loading` 时禁用触发按钮；`Stale` 同时显示更新时间与数据可能已过期。
- 主窗口布局：顶部（应用名 / 城市搜索 / 当前城市 / 刷新 / 设置），左侧（当前天气 / 逐小时 / 7 天 / 收藏 / 最近访问），右侧（AI 对话 + 三个快捷问题 + 消息状态），底部状态区（数据来源 / 最近更新时间 / 网络状态）。
- 快捷问题固定为：今天适合通勤吗？/ 明天适合跑步吗？/ 明天需要带伞吗？
- 温度只在展示层按 `AppSettings.Unit` 换算；时间按 `Location.Timezone` 格式化，缺失回退本地时区；浅色 / 深色 / 跟随系统三主题，切换立即生效并持久化。

---

## 12. 最终验收清单（主 Agent 结束任务前的自检）

**功能**：搜索并选择城市；展示当前 / 逐小时 / 7 天预报；收藏与取消收藏；最近访问与启动恢复；重启后收藏与缓存仍在；断网或接口失败显示缓存 + 更新时间 + 重试；无 Key 可用演示模式；配置 DeepSeek 后 Agent 调工具回答出行问题；缺城市 / 日期时 Agent 追问而非猜测。

**工程**：`dotnet restore` / `dotnet build` / `dotnet test` 全通过；GitHub Actions restore + build + test 通过；仓库与日志中无 API Key 等敏感信息；README 能指导面试官从干净环境完成克隆、运行、配置、测试。

**体验**：首屏可见当前城市、天气状态、数据来源与更新时间；加载 / 空 / 错误 / 缓存状态可区分；AI 长文本可滚动且不遮挡天气内容；浅色与深色主题下文字与控件清晰可读，窗口缩放时主要区域不重叠。

**交付**：`doc/tasks/progress.md` 中 M0 至 M12 全部勾选；`README.md` 含待确认项 / 实现假设章节；截图与架构说明反映最终实现。

---

## 13. 禁止事项

- 不要向用户提问或等待确认（本文档第 3 节已给出全部决策）。
- 不要跳过或弱化测试来让构建变绿；不要写恒真断言或只镜像实现的测试。
- 不要伪造天气数据，不要在缓存不可用时代填实时结果。
- 不要让 ViewModel 直接调用 `HttpClient`、EF Core 或文件系统。
- 不要把 API Key、Authorization 头、用户问题原文写进日志、异常、测试快照、Git。
- 不要为省事合并模块职责（例如让 Agent 直接调用 Open-Meteo 绕过工具层）。
- 不要在没有失败原文的情况下让子 Agent 再试一次。

---

## 14. 实现期新增假设（执行过程中追加，需同步到 README「待确认项」）

- 2026-10-02，M1：QuestionAnalyzer 保持纯函数；M6 调用前通过 IClock.TodayIn(AgentContext.UtcOffset) 填充 AgentContext.Today。没有 Today 时只解析 ISO 日期，中文相对日期保留为缺失，避免读取系统时间。
- 2026-10-02，M1：周几解析为最近的将来日期；当天的星期视为今天。工具日期有效范围为本地今天至第 6 天（含），AgentParameterInput.UtcOffset 用于按城市时区校验。
- 2026-10-02，M1：天气建议采用可测试阈值：降水概率不低于 40%、风速不低于 30 km/h、最低温度不高于 5°C 时分别提示带伞、防风和保暖；有明显降水的户外跑步建议改为室内。城市识别仅抽取天气问题中靠近天气意图词的地点文本，未识别时返回空并由 M6 追问或按其流程处理当前城市。
- 2026-10-02，M3：Domain 天气快照通过可选 `QueryDate` 记录逐小时裁剪日期；未请求到的可选、非 nullable 数值字段按领域类型默认值 0 映射，nullable 字段（能见度、UV、日出日落）保留 null，不因此使整份天气响应失败。
- 2026-10-02，M3：`RefreshAsync` 按 Location.Id 管理进行中的刷新；同城新刷新取消旧刷新，不同城市互不影响。该协调由 Domain 查询服务完成，不依赖 UI。
- 2026-10-08，M10-M12 收尾：设置面板以内嵌方式提供，API Key 输入使用密码框并只展示脱敏值；温度单位仅在展示层换算，城市时区用于更新时间格式化；启动初始化读取 `IAppStateStore` 恢复最后城市。最终质量门禁以 152 个离线测试、解决方案构建和 CI 工作流为验收证据。
