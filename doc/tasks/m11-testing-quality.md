# M11 测试与质量验证

## 目标

完成跨模块回归和工程质量门禁；各模块的单元测试仍在各自任务文件中实施。

## 子任务

- [x] 建立可重复运行的测试命令与测试项目分组，默认测试不依赖真实网络、真实 DeepSeek Key 或 UI。
- [x] 为关键边界准备可复用 Fake/Stub：天气、地理编码、缓存、城市存储、设置、回答生成器和时钟。
- [x] 补齐外部接口异常路径测试：取消、超时、非 2xx、畸形 JSON、存储错误和 AI 异常内容。
- [x] 执行 Domain、Service、Agent、Cache、Persistence、App 全部自动化测试并修复回归。
- [x] 执行 `dotnet restore`、`dotnet build`、`dotnet test`，确认解决方案级命令通过。
- [x] 检查仓库和日志测试数据，确认没有 API Key、Authorization Header 或不必要的用户内容。
- [x] 在 Windows 环境执行干净构建和运行冒烟测试，检查首次启动、无网络和无 DeepSeek 配置路径。

## 完成验收

- [x] 全量测试与构建命令通过。
- [x] 关键成功、降级、取消和失败路径均有自动化验证。
