# M4 天气缓存

## 目标

把天气快照可靠地序列化到持久化抽象，并在读取时安全处理版本和脏数据。

## 子任务

- [x] 定义缓存存储抽象 `ICacheStorage`，提供按城市读取、Upsert 和删除操作。
- [x] 实现 `EfWeatherCache`，把完整 WeatherSnapshot 序列化为 JSON 并以 LocationId Upsert。
- [x] 读取时校验 DataVersion；版本不支持或 JSON 损坏时视为无有效缓存并记录日志。
- [x] 确保读取快照标记为 Cache 来源，同时保留更新时间与位置快照信息。
- [x] 捕获存储故障并返回 Storage Result；不在缓存实现内部重试。
- [x] 使用 FakeCacheStorage 编写保存读取等价、覆盖更新、版本不匹配、脏 JSON、存储故障和 TTL 测试。
- [x] 通过 EF Core 持久化测试验证 SQLite 的唯一键与 Upsert 行为。

## 完成验收

- [x] 缓存接口实现符合 `IWeatherCache`，异常不会泄漏到调用方。
- [x] CacheTests 与相关 PersistenceTests 通过。
