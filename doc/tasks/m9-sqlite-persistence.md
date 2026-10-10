# M9 本地持久化（EF Core SQLite）

## 目标

建立 SQLite 数据库、表结构和生命周期管理，为缓存、城市管理与应用状态提供持久化实现。

## 子任务

- [x] 实现 `AppDbContext` 与 FavoriteCities、RecentCities、WeatherCaches、AppStates 实体及字段映射。
- [x] 配置 LocationId 唯一索引、Recent VisitedAtUtc 索引及缓存/状态主键。
- [x] 实现 SQLite 连接工厂与短命 DbContext 生命周期，命令超时 5 秒。
- [x] 实现数据库目录创建和初始化；按设计采用 EnsureCreated 与 user_version 策略。
- [x] 为无法打开数据库、建库或单次读写故障提供统一 Storage Result，不阻断网络天气和 AI 功能。
- [x] 实现 EF 存储适配器供 M4/M5 使用，EF Entity 不暴露到 Domain 和 App。
- [x] 编写真实临时 SQLite PersistenceTests：四表初始化、重复启动保留数据、索引、缓存 Upsert、最近城市上限、LastCity 读写。
- [x] 提供应用数据不可用时的内存存储/降级接线，并在状态区可报告本次数据不会保存。

## 完成验收

- [x] 持久化测试对每个用例使用独立临时数据库并清理资源。
- [x] App/Domain 不引用 EF Core 实体或 DbContext。
