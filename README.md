# WeatherDesk

WeatherDesk 是一个基于 .NET 8 和 Avalonia 的 Windows 优先天气桌面应用，包含天气查询和天气出行 AI 助手。天气数据来自 Open-Meteo，默认使用本地 Demo 回答引擎，不配置 DeepSeek Key 也可以运行。

## 功能

- 中国大陆城市、省、市辖区和县的搜索联想
- 当前天气、未来 48 小时逐小时预报和未来 7 天预报
- 天气缓存和网络异常降级
- 收藏城市、最近访问和启动恢复
- AI 出行助手，支持天气、穿衣、跑步、通勤、旅游和路线天气问题
- Auto、Demo、DeepSeek 三种 AI 模式
- 温度单位、主题和 DeepSeek 配置
- SQLite 持久化失败时自动降级到进程内存储

## 构建和运行

在项目根目录执行：

```powershell
dotnet restore WeatherDesk.sln
dotnet build WeatherDesk.sln
dotnet test WeatherDesk.sln
```

Windows 下可以运行：

```powershell
src/WeatherDesk.App/bin/Debug/net8.0/WeatherDesk.App.exe
```

Release 版本：

```powershell
src/WeatherDesk.App/bin/Release/net8.0/WeatherDesk.App.exe
```

## AI 模式

- **Auto**：配置了 DeepSeek API Key 时调用 DeepSeek；没有 Key 或调用失败时使用本地 Demo 回答。
- **Demo**：完全使用本地规则和天气数据生成基础建议，不需要 AI Key。
- **DeepSeek**：强制调用 DeepSeek，需要在设置中填写有效 API Key。

应用设置保存在 `%LOCALAPPDATA%\\WeatherDesk`。API Key 会加密保存到 `credentials.dat`，不会写入 `settings.json`。

## 使用方式

启动应用后搜索城市并选择地点，左侧显示天气预报，右侧可以询问天气相关问题，例如：

- `武汉明天适合跑步吗`
- `我要去武汉旅游 7 天，需要注意什么`
- `武汉去北京，路上应该怎么穿衣`

## 自动化验证

GitHub Actions 会在每次 Push 和 Pull Request 时自动执行还原、Release 构建和完整测试。

当前本地验证结果：Release 构建 0 个警告、0 个错误，测试 `178/178` 通过。

## 实现说明

- 天气领域数据统一使用摄氏度，界面根据设置转换显示单位。
- 时间显示优先使用所选城市的时区偏移。
- 天气缓存、城市状态和最近城市状态在 SQLite 失败时降级到当前进程内存。
- 本地离线地名库优先用于搜索联想，找不到时再调用地理服务兜底。
