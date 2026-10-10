# M3 天气查询与编排

## 目标

获取并解析 Open-Meteo 当前天气、逐小时和 7 天预报，并以缓存实现失败降级。

## 子任务

- [x] 实现 Forecast URL 构造，包含当前、逐小时、每日字段、能见度尝试、timezone=auto 和 forecast_days=7。
- [x] 实现 10 秒超时、取消传播和统一 HTTP 失败映射。
- [x] 实现纯函数 `OpenMeteoResponseParser`，将响应映射到天气领域模型。
- [x] 对数组字段按最短长度安全截断；可选数值缺失不导致整体失败，必需 current 字段缺失返回 DataFormat。
- [x] 处理 `utc_offset_seconds`、`current.time` 到 UTC 的换算；缺失数据时间时回退检索时间。
- [x] 支持指定日期的逐小时结果裁剪，同时保留完整 7 日 Daily 预报供 Agent 使用。
- [x] 定义 `WeatherQueryResult` 并实现 `WeatherQueryService.LoadAsync`、`RefreshAsync` 的最新优先与缓存降级流程。
- [x] 上游成功后保存缓存、最近城市和最后选择城市；旁路持久化失败仅记录日志，不改变天气主结果。
- [x] 上游失败时读取缓存；按 TTL 返回新鲜或 Stale 结果，无有效缓存时保留上游失败类型。
- [x] 编写解析器测试及编排 Fake 测试：成功、缓存新鲜/过期、无缓存、写入失败、取消及不写缓存路径。
- [x] 注册 `IWeatherService` 和天气查询应用服务。

## 完成验收

- [x] 天气页面与 Agent 可复用同一查询链路。
- [x] 测试不依赖真实网络、数据库或 UI。
