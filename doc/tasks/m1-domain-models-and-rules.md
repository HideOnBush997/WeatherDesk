# M1 领域模型与规则

## 目标

定义无 UI、网络和数据库依赖的领域契约、模型、结果类型及纯业务规则。

## 子任务

- [x] 定义 `FailureKind`、`Failure`、`Result`、`Result<T>` 和 `AsyncStatus`，覆盖输入、取消、超时、网络、上游、数据、存储和 AI 失败。
- [x] 定义 Location、天气快照/当前天气/逐小时/每日预报及来源模型，统一领域单位和 UTC 时间语义。
- [x] 定义 Agent 问答、意图、回复、上下文、聊天消息和提示词所需领域模型。
- [x] 定义 AppSettings、温度单位、主题和 AI 模式模型；API Key 不进入普通设置快照。
- [x] 定义 `IGeocodingService`、`IWeatherService`、`IWeatherCache`、`ICityStore`、`IAppStateStore`、`ISettingsStore`、`IAnswerGenerator`、`IWeatherAgent`、`IClock` 等接口。
- [x] 实现 `WeatherCodeCatalog`，覆盖 Open-Meteo 代码中文描述、分组和图标键，未知值安全回退。
- [x] 实现 `CacheFreshnessPolicy`，通过显式 now 和 TTL 判定新鲜度，不读取系统时间。
- [x] 实现 `DateResolver`，解析今天、明天、后天、周几及 ISO 日期，并拒绝无法识别的输入。
- [x] 实现 `AgentParameterValidator` 和 `QuestionAnalyzer`，抽取城市、日期、意图并报告缺失/非法字段。
- [x] 实现 `UnitConverter` 与 `AdviceRuleSet`，覆盖温度换算和基于天气数据的规则建议。
- [x] 为领域纯函数编写 xUnit 测试，覆盖边界、未知值和代表性正常输入。

## 完成验收

- [x] Domain 可独立构建，且不引用 Avalonia、HTTP、EF Core 或其他外部包。
- [x] 所有纯规则测试通过。
