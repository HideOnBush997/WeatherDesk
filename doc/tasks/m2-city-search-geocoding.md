# M2 城市搜索与地理编码

## 目标

通过 Open-Meteo Geocoding API 将关键词解析为领域 Location 列表。

## 子任务

- [x] 实现 `OpenMeteoGeocodingClient`，通过构造函数注入 HttpClient 与 ILogger。
- [x] 对空白关键词返回 Input 失败，不发送 HTTP 请求。
- [x] 按契约构造编码后的查询 URL：count=10、language=zh、format=json。
- [x] 实现 10 秒超时与 CancellationToken 传播，并区分主动取消和超时。
- [x] 实现 HTTP 状态、网络异常和 JSON 异常到统一 Failure 的映射。
- [x] 解析 results；缺少必需字段的条目跳过，空/缺失结果返回成功空列表。
- [x] 映射城市名、行政区、国家、经纬度、时区和 UTC 偏移等 Location 字段。
- [x] 编写 HttpMessageHandler Stub 测试：字段映射、空结果、500、超时、取消、非法 JSON、空白关键词及请求参数编码。
- [x] 将 Domain 的 `IGeocodingService` 注册到该实现。

## 完成验收

- [x] 模块测试不访问真实网络且全部通过。
- [x] 所有失败均以 Result 返回，不把业务异常抛给调用方。
