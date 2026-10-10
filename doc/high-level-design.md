# WeatherDesk 概要设计文档

## 1. 文档目的

本文基于 `doc/proposal.md`，说明 WeatherDesk MVP 的模块边界、模块关系、核心流程、数据边界和可靠性设计，为 Avalonia 桌面应用的实现、测试和交付提供结构依据。

本文只覆盖需求文档已经确认的范围，不扩展账号、云同步、通用聊天、插件市场或真实硬件接入。

## 2. 总体设计

WeatherDesk 采用四层结构：

```text
+----------------------+     +----------------------+
| WeatherDesk.App      |     | WeatherDesk.Tests    |
| Views / ViewModels   |     | Domain / Agent /     |
| Styles / Converters  |     | Cache / Service      |
+----------+-----------+     +----------------------+
           | 依赖应用服务和领域接口
+----------v-----------+
| WeatherDesk.Domain   |
| Models / Rules /     |
| Interfaces           |
+----------^-----------+
           | 接口实现
+----------+-----------+
| WeatherDesk.Infrastructure |
| Open-Meteo / DeepSeek      |
| Agent / EF Core / Config   |
+----------------------------+
```

依赖规则：

- `WeatherDesk.Domain` 不依赖 UI、HTTP、SQLite 或具体 AI SDK。
- `WeatherDesk.App` 通过领域接口和应用服务访问业务能力，不直接使用 `HttpClient` 或 EF Core。
- `WeatherDesk.Infrastructure` 实现 Domain 定义的接口，负责外部服务、持久化和配置访问。
- `WeatherDesk.Tests` 通过接口替身测试领域规则、应用流程和基础设施适配行为。

## 3. 模块划分

### 3.1 桌面展示模块（WeatherDesk.App）

职责：

- 提供主窗口、设置页和主题样式。
- 展示城市搜索、当前天气、逐小时预报、7 天预报、收藏、最近访问和 AI 对话。
- 维护页面级异步状态：`Idle`、`Loading`、`Success`、`Empty`、`Error`、`Stale`。
- 将用户操作转换为应用服务调用，并将结果映射为界面状态。
- 提供刷新、重试、切换城市、发送问题和切换演示模式等交互入口。

主要组件：

- `MainWindowViewModel`：协调当前城市、天气内容、收藏/最近访问和 AI 面板。
- `WeatherViewModel`：维护天气快照及当前/逐小时/7 天展示状态。
- `CitySearchViewModel`：处理关键词输入、搜索结果和城市选择。
- `CityListViewModel`：处理收藏城市和最近访问城市。
- `ChatViewModel`：维护当前会话消息、快捷问题、发送中状态和错误重试。
- `SettingsViewModel`：维护 DeepSeek 配置、温度单位和主题设置。

展示模块不承担天气解析、Agent 决策、缓存策略或数据库访问。

### 3.2 领域模型与规则模块（WeatherDesk.Domain）

职责：

- 定义城市、天气快照、逐小时预报、每日预报、Agent 上下文和回答等模型。
- 定义天气、缓存、配置和 Agent 的接口契约。
- 定义与 UI 无关的业务规则，例如日期参数完整性、工具参数有效性和缓存新鲜度判断。
- 定义统一的可表达错误类型或结果结构，供上层展示和测试使用。

建议模型边界：

| 模型 | 作用 |
|---|---|
| `Location` | 城市名称、经纬度及地理编码结果 |
| `WeatherSnapshot` | 某城市某次成功查询的完整天气结果、查询时间、数据版本和来源 |
| `HourlyForecast` | 逐小时天气字段 |
| `DailyForecast` | 未来 7 天每日天气字段 |
| `AgentContext` | 当前城市、可用天气上下文和会话信息 |
| `AgentReply` | 助手回答、依据日期/城市及处理状态 |
| `AppSettings` | AI、单位和主题配置 |

### 3.3 城市与天气查询模块

该模块由应用服务编排，由 Infrastructure 提供外部 API 实现。

职责：

- 接收城市关键词并调用 Open-Meteo Geocoding API。
- 将用户选择的 `Location` 传给天气查询服务。
- 调用 Open-Meteo Forecast API 获取当前、逐小时和未来 7 天数据。
- 将外部 API 响应转换为领域模型，补充数据来源和更新时间。
- 将超时、取消、HTTP 错误、响应格式异常转换为可处理的失败结果。

接口：

```csharp
IGeocodingService.SearchAsync(string keyword, CancellationToken ct)
IWeatherService.GetForecastAsync(Location location, DateOnly? date, CancellationToken ct)
```

`OpenMeteoClient` 只负责 HTTP 请求和响应映射，不决定界面显示和 Agent 回答。

### 3.4 城市管理模块

职责：

- 收藏和取消收藏城市。
- 记录最近访问城市。
- 保存并恢复上次选择的城市。
- 向主窗口提供按用户操作更新后的城市列表。

持久化由仓储接口隔离：

- `FavoriteCityRepository`
- `RecentCityRepository`
- `AppStateRepository`（保存上次选择城市等应用状态）

该模块只保存城市及访问关系，不把展示状态或完整对话消息写入 SQLite，除非后续需求明确扩展持久化范围。

### 3.5 天气缓存与离线模块

职责：

- 保存最近一次成功的 `WeatherSnapshot`。
- 记录城市、查询时间、数据版本和天气结果。
- 网络请求失败时读取最近缓存。
- 判断缓存是否可用，并将结果标记为实时成功或 `Stale`。
- 向 UI 提供最近更新时间和手动重试所需信息。

接口：

```csharp
IWeatherCache.GetLatestAsync(Location location, CancellationToken ct)
IWeatherCache.SaveAsync(WeatherSnapshot snapshot, CancellationToken ct)
```

缓存降级规则：

1. 先请求最新数据。
2. 请求成功：展示最新数据并异步或同步保存缓存。
3. 请求失败：读取最近缓存。
4. 有缓存：展示缓存并标记 `Stale`，显示最近更新时间和重试入口。
5. 无缓存：展示 `Error` 或 `Empty`，不伪造天气数据。
6. 缓存读写失败：继续内存运行，并提供可理解的提示。

### 3.6 Agent 编排模块

职责：

- 接收自然语言问题和当前应用上下文。
- 判断问题是否需要天气数据。
- 提取并校验城市、日期和查询意图。
- 缺少城市或日期时生成追问，不自行猜测。
- 在需要时调用唯一首版工具 `GetWeatherForecast(city, date, range)`。
- 将结构化天气结果、用户问题和上下文交给回答生成器。
- 校验工具参数、工具失败和模型返回格式，并输出 `AgentReply`。

内部协作：

```text
IWeatherAgent
  -> QuestionAnalyzer       识别天气需求、城市、日期、意图
  -> AgentParameterValidator 校验必要参数和范围
  -> WeatherToolAdapter     调用 IWeatherService / IWeatherCache
  -> IAnswerGenerator       调用 DeepSeek 或演示模式
  -> AgentReplyMapper       组装回答、依据和状态
```

Agent 层不直接依赖 Avalonia 控件，也不允许模型任意访问外部工具。工具返回结构化数据，Agent 负责把结构化结果组装为回答依据。

### 3.7 AI 适配模块

提供统一的回答生成能力，并屏蔽两种运行模式的差异：

- `DeepSeekClient`：调用 DeepSeek OpenAI 兼容 API。
- `DemoAnswerGenerator`：未配置 API Key 或用户切换演示模式时，根据结构化天气结果生成可演示回答。
- `IAnswerGenerator`：供 Agent 使用的统一接口。

配置规则：

- 未配置 API Key 时自动选择演示模式。
- 配置完成后可选择真实 AI 模式。
- API Key 只写入本机配置，不进入 Git 或日志。
- 请求只携带完成回答所需的天气数据和对话内容。
- AI 不可用或返回格式异常时，允许重试或切换演示模式；必要时降级为普通文本错误提示。

### 3.8 配置与本地持久化模块

职责：

- 创建和迁移 SQLite 数据库。
- 保存收藏城市、最近访问、天气缓存和必要的应用状态。
- 保存 DeepSeek Base URL、API Key、模型名称、温度单位和主题设置。
- 将数据库及配置访问封装在 Infrastructure 内。

建议实体边界：

| 实体 | 关键内容 |
|---|---|
| `FavoriteCityEntity` | 城市标识、名称、经纬度、排序或更新时间 |
| `RecentCityEntity` | 城市标识、名称、经纬度、最近访问时间 |
| `WeatherCacheEntity` | 城市标识、查询时间、数据版本、序列化天气结果 |
| `AppStateEntity` | 上次选择城市等应用状态 |
| `AppSettings` | AI 配置、单位和主题；API Key 不进入日志和源码 |

实体和领域模型之间通过映射转换，避免 EF Core 类型泄漏到 Domain 和 App。

## 4. 模块关系

```text
用户
  |
  v
WeatherDesk.App / ViewModels
  |-- 搜索城市 ------------------> IGeocodingService --> OpenMeteoClient --> Geocoding API
  |-- 选择/刷新城市 --------------> WeatherQueryService
  |                                  |--> IWeatherService --> OpenMeteoClient --> Forecast API
  |                                  |--> IWeatherCache --> SQLite / 内存降级
  |                                  |--> City repositories --> SQLite
  |-- 发送自然语言问题 ------------> IWeatherAgent
                                     |--> 参数分析与校验
                                     |--> GetWeatherForecast
                                     |     |--> IWeatherService
                                     |     `--> IWeatherCache
                                     `--> IAnswerGenerator
                                           |--> DeepSeekClient --> DeepSeek API
                                           `--> DemoAnswerGenerator

SettingsViewModel --> ConfigurationService --> 本地配置 / SQLite
```

关键约束：

- 天气页面和 Agent 共用天气服务、缓存和领域模型，避免两套天气数据链路产生不一致。
- Agent 不能绕过工具直接编造天气数据；所有天气依据必须来自结构化工具结果或明确的失败状态。
- ViewModel 不能直接访问外部服务和数据库。
- 所有网络调用支持取消令牌和超时。

## 5. 核心流程

### 5.1 搜索并展示城市天气

```text
用户输入关键词
  -> CitySearchViewModel 校验非空
  -> IGeocodingService 搜索
  -> 无结果：展示 Empty
  -> 用户选择 Location
  -> WeatherQueryService 请求 Forecast
  -> 成功：映射 WeatherSnapshot、保存缓存、更新 Success
  -> 失败：读取缓存
       -> 有缓存：更新 Stale，显示更新时间和重试
       -> 无缓存：更新 Error，显示重试
  -> 成功选择后更新最近访问和当前城市
```

### 5.2 收藏与启动恢复

```text
启动
  -> AppStateRepository 读取上次城市
  -> 读取收藏和最近访问列表
  -> 有上次城市：加载该城市天气
  -> 无上次城市：等待用户搜索

收藏/取消收藏
  -> CityListViewModel
  -> FavoriteCityRepository 写入 SQLite
  -> 更新左侧城市列表
```

### 5.3 Agent 天气问答

```text
用户发送问题
  -> ChatViewModel 保存用户消息
  -> IWeatherAgent 分析问题
  -> 缺少城市或日期：返回追问
  -> 不需要天气工具：按 Agent 规则返回受控回答
  -> 需要天气工具：校验 city/date/range
  -> GetWeatherForecast 获取结构化天气结果
  -> 工具失败：返回明确错误和重试建议
  -> IAnswerGenerator 生成回答
  -> ChatViewModel 展示回答及城市、日期依据
```

### 5.4 设置与模式切换

```text
用户修改设置
  -> SettingsViewModel 校验字段
  -> ConfigurationService 保存本地配置
  -> API Key 为空：选择 DemoAnswerGenerator
  -> API Key 有效配置：允许 DeepSeekClient
  -> 保存温度单位和主题
```

## 6. 状态与错误设计

天气、城市搜索和 AI 对话分别维护自己的异步状态，互不覆盖。状态至少包括：

| 状态 | 行为 |
|---|---|
| `Idle` | 未发起请求，显示默认空状态 |
| `Loading` | 禁止同一操作重复提交，显示加载反馈 |
| `Success` | 展示最新结果 |
| `Empty` | 展示无结果或无可展示数据 |
| `Error` | 展示可理解错误和重试入口 |
| `Stale` | 展示缓存结果、最近更新时间和数据可能过期提示 |

错误分类：

- 输入错误：城市关键词为空、日期缺失或工具参数非法。
- 网络错误：超时、取消、连接失败或非成功 HTTP 状态。
- 数据错误：响应结构不符合预期或字段缺失。
- 持久化错误：SQLite 读写失败。
- AI 错误：API 不可用、认证失败、模型返回异常或回答超时。

错误处理遵循“可恢复优先”：网络和 AI 错误提供重试；天气请求失败优先尝试缓存；缓存不可用时不伪造实时结果；应用不因单个区域失败而崩溃。

## 7. 数据与安全边界

- Open-Meteo 响应只在 Infrastructure 内解析，再转换为 Domain 模型。
- SQLite 保存收藏、最近访问、天气缓存和必要的应用状态。
- 对话消息默认只保存在当前会话内，需求未要求跨重启恢复。
- API Key 不写入 Git、日志、异常文本或测试快照。
- 日志不得输出完整 `Authorization` Header 或用户隐私信息。
- DeepSeek 请求只发送回答所需的天气数据和对话内容。

## 8. 可测试性设计

测试通过接口替身隔离网络、时间、数据库和 AI：

- Domain tests：参数校验、缓存新鲜度、状态转换和业务规则。
- Agent tests：缺少城市/日期时追问、工具参数校验、工具失败和演示模式回答。
- Cache tests：成功保存、读取最近数据、过期标记和存储异常降级。
- Service tests：Open-Meteo 响应映射、超时/取消、空结果和异常响应。
- App tests：ViewModel 对 Loading、Success、Empty、Error、Stale 的状态映射。

所有外部请求支持 `CancellationToken`、超时和可替换客户端，以便测试不依赖真实网络。

## 9. 交付与验证边界

交付物包含：

- Avalonia .NET 8 解决方案及四个项目。
- SQLite 初始化与本地配置能力。
- Open-Meteo 天气链路、缓存和离线展示。
- 演示模式与 DeepSeek Agent 工具调用链路。
- 单元测试、README、截图和架构说明。
- GitHub Actions 的 restore、build、test 流程。

验收重点：`dotnet build`、`dotnet test`、Windows 干净环境运行、无 API Key 演示模式、真实 API 配置后的工具调用、断网缓存降级以及缺少城市/日期时主动追问。

## 10. 后续扩展约束

后续增加多城市对比、天气预警、个性化建议、本地 Ollama、设备传感器或 Linux/macOS 打包时，应优先通过新增接口实现，保持 Domain 与 App 的依赖方向不变，并避免把新外部依赖直接引入 ViewModel。

## 11. 待确认项

以下内容在需求文档中没有给出确定规则，本文保留为实现参数，不在概要设计中擅自确定：

- “未过期缓存”的具体时长，以及不同天气数据类型是否采用不同有效期。
- 最近访问城市的最大保留数量、去重规则和排序规则。
- API Key 的本机存储方式（普通配置文件、系统凭据存储或其他方式）。
- 用户提出非天气问题时，Agent 的固定拒答文案和是否允许继续普通文本对话。
- DeepSeek 真实模式与演示模式是否提供显式切换开关，还是仅根据 API Key 自动选择。
- AI 对话是否需要在应用重启后恢复；当前设计只保证当前会话内保存。
- SQLite 数据库文件和本地配置文件的具体路径及迁移策略细节。
