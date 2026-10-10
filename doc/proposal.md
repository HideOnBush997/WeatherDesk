# WeatherDesk 产品需求文档

## 1. 文档信息

- 产品名称：WeatherDesk
- 产品类型：跨平台桌面天气查询与 AI 助手应用
- 当前版本：MVP / 5 天冲刺版
- 首要运行平台：Windows
- 目标岗位背景：AI Agent 开发
- 文档状态：需求确认稿

## 2. 产品概述

WeatherDesk 是一个面向桌面用户的天气查询与出行决策助手。应用左侧展示城市天气数据，右侧提供 AI 对话面板。用户可以搜索城市、查看当前天气、逐小时预报和未来 7 天预报，也可以用自然语言询问“明天上海适合跑步吗”等问题。

AI 助手不是泛聊天机器人，而是一个受控的天气 Agent：它识别用户问题中的城市和日期，在需要时调用天气工具获取数据，再基于结构化结果生成穿衣、运动、通勤等出行建议。

产品默认无需任何密钥即可运行。天气数据使用 Open-Meteo；AI 默认使用内置演示模式，也支持配置 DeepSeek 的 OpenAI 兼容 API 启用真实模型能力。

## 3. 背景与目标

### 3.1 背景

本项目用于元新择成公司的 AI Agent 开发岗位面试作业。元新择成公开信息显示其业务聚焦半导体行业智能设备研发，因此作品需要体现桌面端工程能力、外部服务集成、可靠性设计和 Agent 工具调用，而不是停留在简单页面展示。

### 3.2 产品目标

1. 提供一个可编译、可运行、可演示的 Avalonia 桌面应用。
2. 展示清晰的 MVVM 分层、服务抽象和可测试设计。
3. 完成天气数据查询、缓存、错误处理和本地持久化闭环。
4. 通过受控工具调用展示 AI Agent 的核心工作流。
5. 让面试官无需配置天气 API Key 即可克隆并运行项目。

### 3.3 非目标

- 不实现通用聊天、联网搜索或任意插件市场。
- 不实现账号体系、云端同步和多用户协作。
- 不实现真实硬件传感器接入；设备环境数据不属于本版本范围。
- 不追求复杂动态插画和重型视觉特效。

## 4. 目标用户与典型场景

### 4.1 目标用户

- 需要快速查看天气和安排出行的桌面用户。
- 需要通过自然语言获得天气解释和出行建议的用户。
- 评估桌面应用、API 集成、缓存和 Agent 设计的技术面试官。

### 4.2 典型场景

1. 用户搜索“上海”，查看当前天气、未来 7 天和逐小时预报。
2. 用户收藏上海和武汉，在应用启动后快速切换城市。
3. 用户询问“明天上午上海适合跑步吗”，Agent 调用天气工具并给出基于数据的建议。
4. 网络异常时，应用展示最近一次成功数据、数据时间和重试入口。
5. 用户配置 DeepSeek API 后，将演示模式切换为真实 AI 模式。

## 5. 功能需求

### 5.1 天气查询

- 支持城市关键词搜索。
- 使用 Open-Meteo Geocoding API 将城市名称解析为经纬度。
- 使用 Open-Meteo Forecast API 获取：
  - 当前天气
  - 逐小时预报
  - 未来 7 天预报
- 展示温度、体感温度、天气状况、湿度、风速、气压和能见度等核心字段。
- 展示数据更新时间和数据来源。
- 支持加载中、无结果、请求失败和空数据状态。

### 5.2 城市管理

- 支持收藏城市。
- 支持取消收藏。
- 支持最近访问城市列表。
- 支持启动时恢复上次选择的城市。
- 收藏和最近访问数据保存到本地 SQLite 数据库。

### 5.3 缓存与离线体验

- 缓存最近一次成功获取的天气数据。
- 缓存应包含城市、查询时间、数据版本和天气结果。
- 网络请求失败时优先展示未过期或最近一次缓存数据。
- 缓存数据必须明确标注“最近更新时间”，避免用户误认为是实时数据。
- 提供手动重试按钮。

### 5.4 AI 对话面板

- 主界面采用左侧天气内容、右侧 AI 对话面板布局。
- 支持输入自然语言问题。
- 保存当前会话中的用户问题和助手回答。
- 提供至少三个快捷问题示例：
  - 今天适合通勤吗？
  - 明天适合跑步吗？
  - 明天需要带伞吗？
- AI 回答需要引用当前查询城市和目标日期。
- 当缺少城市或日期时，Agent 应主动追问，而不是猜测。
- AI 服务不可用时，显示明确错误，并允许重试或切换演示模式。

### 5.5 轻量 Agent 与工具调用

Agent 采用自定义轻量实现，核心流程如下：

1. 接收用户问题。
2. 判断是否需要天气数据。
3. 提取城市、日期和查询意图。
4. 调用 `GetWeatherForecast` 天气工具。
5. 将结构化天气结果和用户问题交给 DeepSeek 或演示模式生成器。
6. 返回带有依据和建议的回答。

首版工具定义：

```text
GetWeatherForecast(city, date, range)
```

工具返回结构化数据，不直接返回自然语言。Agent 层负责工具选择、参数校验、失败处理和上下文组装。

### 5.6 配置

- 支持在设置页配置 DeepSeek API Base URL、API Key 和模型名称。
- API Key 只保存在本机配置中，不写入 Git 仓库和日志。
- 未配置 API Key 时自动使用演示模式。
- 支持设置温度单位：摄氏度为默认值，华氏度为可选值。
- 支持设置主题：浅色、深色和跟随系统。

## 6. 页面与交互

### 6.1 主窗口

- 顶部：应用名称、城市搜索、当前城市、刷新按钮和设置入口。
- 左侧主区域：当前天气、逐小时预报、7 天预报、收藏和最近城市。
- 右侧区域：AI 对话面板、快捷问题和消息状态。
- 底部或状态区：数据来源、最近更新时间、网络状态。

### 6.2 状态设计

所有异步区域至少支持以下状态：

- Idle：尚未发起请求。
- Loading：正在加载。
- Success：成功展示数据。
- Empty：没有搜索结果或没有可展示数据。
- Error：请求失败并提供重试。
- Stale：展示缓存数据并提示数据可能已过期。

## 7. 技术方案

### 7.1 技术选型

- UI：Avalonia UI 11
- 运行时：.NET 8 LTS
- 架构：MVVM
- MVVM 工具：CommunityToolkit.Mvvm
- HTTP：`HttpClient`
- JSON：`System.Text.Json`
- 数据库：SQLite + Entity Framework Core
- AI：DeepSeek OpenAI 兼容 API，可切换内置演示模式
- 测试：xUnit
- 持续集成：GitHub Actions

### 7.2 分层结构

```text
WeatherDesk.App
  Views / ViewModels / Styles / Converters

WeatherDesk.Domain
  WeatherModels / AgentModels / BusinessRules / Interfaces

WeatherDesk.Infrastructure
  OpenMeteoClient / DeepSeekClient / AgentService
  AppDbContext / Repositories / Configuration

WeatherDesk.Tests
  Domain tests / Agent tests / Cache tests / Service tests
```

### 7.3 关键接口

```csharp
public interface IGeocodingService
{
    Task<IReadOnlyList<Location>> SearchAsync(string keyword, CancellationToken ct);
}

public interface IWeatherService
{
    Task<WeatherSnapshot> GetForecastAsync(
        Location location, DateOnly? date, CancellationToken ct);
}

public interface IWeatherCache
{
    Task<WeatherSnapshot?> GetLatestAsync(Location location, CancellationToken ct);
    Task SaveAsync(WeatherSnapshot snapshot, CancellationToken ct);
}

public interface IWeatherAgent
{
    Task<AgentReply> AskAsync(
        string question, AgentContext context, CancellationToken ct);
}
```

### 7.4 依赖方向

- `Domain` 不依赖 UI、HTTP 或数据库实现。
- `App` 只依赖领域接口和应用服务。
- `Infrastructure` 实现领域定义的接口。
- ViewModel 不直接调用 `HttpClient` 或 EF Core。
- 所有外部请求都支持取消、超时和可测试替换。

## 8. 非功能需求

### 8.1 可运行性

- Windows 环境可通过标准 .NET SDK 编译运行。
- 默认不要求天气 API Key。
- 未配置 DeepSeek 时仍可进入演示模式。

### 8.2 可靠性

- 请求必须设置超时时间和取消令牌。
- 网络异常不得导致应用崩溃。
- 缓存读写异常需要降级为内存运行并显示可理解的提示。
- AI 返回格式异常时需要安全降级为普通文本回答或错误提示。

### 8.3 安全性

- 禁止将 API Key 提交到 Git。
- 日志不得输出完整 API Key、Authorization Header 或用户隐私信息。
- DeepSeek 请求只发送完成回答所需的天气数据和对话内容。

### 8.4 可维护性

- 业务逻辑和外部依赖通过接口隔离。
- 关键服务拥有单元测试。
- README 说明安装、配置、运行、测试和常见故障处理。
- GitHub Actions 至少执行 restore、build 和 test。

## 9. 5 天交付范围

### Day 1：工程骨架与主界面

- 创建解决方案和 Avalonia 项目。
- 建立 Domain、App、Infrastructure、Tests 分层。
- 完成主窗口布局、导航区域和基础主题。

### Day 2：Open-Meteo 天气链路

- 完成城市搜索。
- 完成当前天气、逐小时和 7 天预报模型。
- 完成加载、成功、空数据和错误状态。

### Day 3：SQLite、收藏与缓存

- 接入 EF Core SQLite。
- 完成收藏城市和最近访问。
- 完成天气缓存、离线展示和重试。

### Day 4：DeepSeek Agent

- 完成演示模式。
- 完成 OpenAI 兼容 API 客户端。
- 完成天气工具调用、参数校验和出行建议。
- 完成 AI 对话面板和快捷问题。

### Day 5：质量与提交

- 补充单元测试和错误路径测试。
- 完善主题、配置页和空状态。
- 编写 README、截图和架构说明。
- 配置 GitHub Actions。
- 完成 Windows 编译运行和干净环境验证。

## 10. 验收标准

### 功能验收

- 能搜索城市并选择结果。
- 能展示当前天气、逐小时预报和未来 7 天预报。
- 能收藏城市、取消收藏并恢复最近访问。
- 重启应用后收藏和缓存仍然存在。
- 断网或接口失败时能显示缓存数据、更新时间和重试按钮。
- 未配置 DeepSeek API 时能使用演示模式。
- 配置 DeepSeek 后，Agent 能调用天气工具并回答出行问题。
- 缺少城市或日期时，Agent 会追问而不是自行猜测。

### 工程验收

- `dotnet build` 成功。
- `dotnet test` 成功。
- GitHub Actions 构建成功。
- 仓库中没有 API Key 或其他敏感信息。
- README 能指导面试官完成克隆、运行、配置和测试。

### 体验验收

- 首屏能够理解当前城市、天气状态和更新时间。
- 加载、空数据、错误和缓存状态有明显区别。
- AI 回答不会遮挡天气内容，长文本可以滚动查看。
- 浅色和深色主题下文字、按钮和状态提示均清晰可读。

## 11. 风险与取舍

- Open-Meteo 的外部网络可用性不可控，因此必须优先实现缓存和演示数据路径。
- DeepSeek API 的模型输出不稳定，因此 Agent 必须限制工具范围，并对参数和异常结果做校验。
- Avalonia 初学成本较高，因此首版控制页面数量，优先保证主窗口和设置页完成度。
- 5 天交付不追求复杂动画、账号体系和跨设备同步，把时间投入到 Agent 闭环、可靠性和工程文档。

## 12. 后续可扩展方向

- 多城市天气对比。
- 天气预警和极端天气提醒。
- 基于用户偏好的个性化出行建议。
- 本地 Ollama 模型适配。
- 设备传感器或实验室环境数据接入。
- Linux/macOS 打包和自动发布。
