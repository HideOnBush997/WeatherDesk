# M7 AI 适配（DeepSeek 与演示模式）

## 目标

提供默认可用的演示回答，以及可配置的 DeepSeek OpenAI 兼容 API 适配。

## 子任务

- [x] 实现 `DemoAnswerGenerator`，用天气代码与建议规则生成稳定、注明演示模式的回答。
- [x] 实现 DeepSeek `/chat/completions` 请求，注入 HttpClient、设置 Bearer Key、system/user 消息、temperature、max_tokens 和非流式参数。
- [x] 实现 30 秒超时和 CancellationToken 传播。
- [x] 解析 `choices[0].message.content`；缺失或空内容返回 AiMalformed。
- [x] 将 401/403 映射为 AiUnauthorized；网络、超时、408、429、5xx 映射为可重试 AiUnavailable；区分取消。
- [x] 实现 AnswerGeneratorSelector：Demo、DeepSeek 无 Key、有 Key、Auto 有/无 Key 的选择行为。
- [x] 仅 Auto 模式在 DeepSeek 失败时回退 Demo，并清楚标记回答来源；显式 DeepSeek 模式返回错误。
- [x] 确保日志不包含 API Key、Authorization、请求/响应正文或用户问题原文。
- [x] 编写 Stub HTTP 测试覆盖正常请求/响应、授权失败、服务失败、畸形响应、取消和模式选择；验证无敏感信息泄漏。

## 完成验收

- [x] 未配置 Key 时仍可演示完整 Agent 流程。
- [x] AI 适配测试无需真实 API Key 或外网。
