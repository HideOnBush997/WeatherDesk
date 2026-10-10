# M0 工程骨架与依赖注入

## 目标

建立可编译的 .NET 8/Avalonia 解决方案与约定目录，为后续模块提供稳定项目边界。

## 子任务

- [x] 创建 `WeatherDesk.sln`，包含 Domain、Infrastructure、App、Tests 四个项目。
- [x] 配置 .NET 8、Avalonia UI 11、CommunityToolkit.Mvvm、EF Core SQLite、Microsoft.Extensions.* 和 xUnit 所需依赖。
- [x] 建立详细设计约定的源码目录和测试目录；Domain 不引用外部 NuGet 包，App 不直接依赖 HTTP/EF 实现。
- [x] 创建最小可启动的 Avalonia 主窗口与入口，保证 `dotnet build` 可通过。
- [x] 在 App 中创建 `ServiceRegistration` 组装根；Program 只负责启动，具体映射留待服务模块逐项注册。
- [x] 配置可注入的 `HttpClient` 工厂、`IDbContextFactory` 注册位置和瞬时 ViewModel 注册约定。
- [x] 加入基础 `.gitignore`，排除构建输出、本地设置、数据库、凭据和 API Key 文件。

## 完成验收

- [x] 干净检出后 `dotnet restore`、`dotnet build` 成功。
- [x] App 可启动并显示主窗口。
- [x] 项目引用方向符合 M1–M10 依赖矩阵。
