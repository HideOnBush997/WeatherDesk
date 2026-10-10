# WeatherDesk Prompt Completion Implementation Plan

**Goal:** 完成 `doc/prompt.md` 要求的 M10-M12 收尾，使 Avalonia UI、全量质量门禁和交付文档与实际实现一致。

**Architecture:** 保持 Domain/Infrastructure/App 分层和现有接口契约。先验证当前实现与测试基线，再补 UI 缺口和回归测试，最后更新任务清单、README 与 CI 验收说明。

**Tech Stack:** .NET 8, Avalonia 11, CommunityToolkit.Mvvm, EF Core SQLite, xUnit, PowerShell。

**Spec:** `doc/prompt.md`, `doc/proposal.md`, `doc/detailed-design.md`。

## Global Constraints

- Domain 不引用 Avalonia、HTTP、EF Core 或外部 NuGet 包。
- 所有跨模块失败使用 `Result` / `Result<T>`，不以异常表达业务失败。
- API Key、Authorization、完整用户问题和请求响应正文不得进入日志、测试快照或 Git。
- 时间通过 `IClock` 注入；异步方法传播 `CancellationToken`。
- 默认测试不依赖真实网络、真实 API Key 或 Avalonia UI 线程。

### Task 1: Establish Baseline

**Files:** existing solution and test projects.

- [ ] Run `dotnet restore WeatherDesk.sln`.
- [ ] Run `dotnet build WeatherDesk.sln` and record failures.
- [ ] Run `dotnet test WeatherDesk.sln --logger "console;verbosity=normal"` and group failures by subsystem.
- [ ] Scan remaining task checkboxes and current UI files to identify only missing behavior.

### Task 2: Complete M10 Desktop UI Gaps

**Files:** existing `src/WeatherDesk.App` ViewModels, Views, converters, styles; `src/WeatherDesk.Tests` AppTests.

- [ ] Add failing tests for temperature conversion, timezone formatting, startup last-city restore, and settings-page key masking.
- [ ] Run focused AppTests and confirm they fail for the missing behaviors.
- [ ] Implement the smallest changes using existing Domain services and `IClock`; do not add direct HTTP, EF, or file access to ViewModels.
- [ ] Add/complete settings view and runtime theme/unit bindings while preserving existing layout.
- [ ] Run focused AppTests and the App build.

### Task 3: M11 Quality Gate

**Files:** `src/WeatherDesk.Tests`, task checklist files, any production files exposed by failing tests.

- [ ] Add only meaningful regression tests for failures found in the baseline.
- [ ] Run solution restore, build, and test from a clean command sequence.
- [ ] Run constraint scans for direct clock access, `new HttpClient`, `new AppDbContext`, sensitive strings, and forbidden dependency references.
- [ ] Verify cancellation, stale-cache, malformed-response, storage-failure, and demo-mode paths remain covered.

### Task 4: M12 Documentation and Delivery

**Files:** `README.md`, `.github/workflows/dotnet.yml`, `doc/tasks/m10-desktop-ui.md`, `doc/tasks/m11-testing-quality.md`, `doc/tasks/m12-documentation-ci-delivery.md`, `doc/tasks/progress.md`.

- [ ] Update README to reflect actual UI, startup recovery, settings protection, demo mode, build/test commands, assumptions, and known limitations.
- [ ] Ensure CI restores, builds, and tests the solution on Windows with .NET 8.
- [ ] Mark only verified task items complete and leave unsupported claims unchecked.
- [ ] Run final build/test/security scans and report exact evidence.
