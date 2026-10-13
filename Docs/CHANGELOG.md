# Changelog

All notable changes to this project will be documented in this file.

This format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/). Release artifacts and notes for each version live on the [Releases](https://github.com/banned2054/PromptHook/releases) page.

## 📘 Versions

- [v0.1.0](#-release-v010)
- [v0.0.1](#-release-v001)

## 🚀 Release v0.1.0

Release Date: 2026-10-13

[English](#v010-english) | [简体中文](#v010-zh-cn)

<a id="v010-english"></a>
### English

This release adds Codex CLI support alongside ZCode.

#### ✨ Added

- **Codex adapter (SessionStart)**: new `codex` agent targeting the Codex CLI hooks system (v0.114+). Reads `hook_event_name`/`model`/`cwd` from the hook stdin JSON, checks only the snake_case event field, and wraps the selected Markdown in the `hookSpecificOutput.additionalContext` JSON envelope on stdout (same shape as ZCode) — per the official schemas (`codex-rs/hooks/schema/generated/session-start.command.*.json`), SessionStart stdout is parsed strictly as JSON and non-JSON output is dropped. Registration sample and field notes are in the README's Codex integration section. Not yet verified against a real host run.
- The `--help` agent list is now generated from the adapter registry instead of being hardcoded.

#### 🧪 Tests

- 111 xUnit tests: 11 Codex adapter unit tests and 3 Codex end-to-end cases (JSON-envelope injection, `cwd` project routing, non-target event skipping) added; existing ZCode coverage is unchanged.

<a id="v010-zh-cn"></a>
### 简体中文

本版本在 ZCode 之外新增 Codex CLI 支持。

#### ✨ 新增

- **Codex 适配器（SessionStart）**：新增 `codex` agent，接入 Codex CLI hooks 体系（v0.114+）。从 stdin 的 hook JSON 读取 `hook_event_name`/`model`/`cwd`，只检查 snake_case 的事件字段，并把选中的 Markdown 包装进 `hookSpecificOutput.additionalContext` JSON 信封写入 stdout（与 ZCode 同构）——依据官方 schema（`codex-rs/hooks/schema/generated/session-start.command.*.json`），SessionStart 的 stdout 严格按 JSON 解析，非 JSON 输出会被丢弃。注册样例与字段说明见 README 的 Codex 接入一节。尚未经真实宿主运行验证。
- `--help` 的支持列表改为从适配器注册表生成，不再硬编码。

#### 🧪 测试

- 111 个 xUnit 用例：新增 Codex 适配器单元测试 11 个、端到端用例 3 个（JSON 信封注入、`cwd` 项目路由、非目标事件跳过）；既有 ZCode 覆盖不变。

## 🚀 Release v0.0.1

Release Date: 2026-09-27

[English](#v001-english) | [简体中文](#v001-zh-cn)

<a id="v001-english"></a>
### English

This is the first preview release. PromptHook is a single-file command-line hook: when an agent session starts, it selects a Markdown prompt by model name, reads the host event input from stdin, and injects the raw prompt into the session following the target agent's hook protocol. No daemon, no network requests, no runtime dependencies (self-contained NativeAOT).

#### ✨ Added

- **Four-layer prompt selection**: project × agent → project → global × agent → global. Exactly one Markdown file is selected per run, never merged; only "not configured" falls back — a selected file that is missing or empty is an error, never a silent downgrade to a lower-priority rule.
- **Model-name candidate matching**: when the host reports `provider/model`, each layer tries the full name first, then the prefix-stripped name; which one hits is decided entirely by the config.
- **Alias direct access**: `prompt-hook zcode --alias review` outputs the prompt configured under `aliases`, skipping model and project matching.
- **ZCode adapter (SessionStart)**: reads the event JSON from stdin and outputs `hookSpecificOutput.additionalContext`; stdout carries only the protocol result, diagnostics go to stderr.
- **Full command line**: `--model` / `--project` / `--config` / `--alias` / `--global` / `--debug`; help and version queries do not read stdin.
- **Strict config validation**: duplicate JSON keys, unknown fields, and type errors are rejected with line/column or path details; relative Markdown paths resolve against the config file's directory.
- **Well-defined exit codes**: 0 = injected or normal no-match; 1 = config/input/file error; 2 = argument error.

#### 🧪 Tests

- 97 xUnit tests covering the CLI, config, resolver, adapter, and end-to-end flows.

#### 📦 Installation

Download the package for your platform from the [v0.0.1 release](https://github.com/banned2054/PromptHook/releases/tag/v0.0.1) and unpack it anywhere — you get two files, `prompt-hook` (`.exe` on Windows) and `prompt-hook.json`:

| Package | Platform |
| --- | --- |
| `prompt-hook-v0.0.1-linux-x64.tar.gz` | Linux x64 |
| `prompt-hook-v0.0.1-win-x64.zip` | Windows x64 |
| `prompt-hook-v0.0.1-osx-arm64.tar.gz` | macOS Apple Silicon |

On Linux/macOS, run `chmod +x prompt-hook` before first use. Config format, selection priority, and ZCode integration are documented in the [README](https://github.com/banned2054/PromptHook/blob/master/README.md). SHA-256 checksums for all packages are in the `SHA256SUMS.txt` release asset.

#### 📝 Notes

- The only supported agent is `zcode`.
- Host-side ZCode integration has been verified against a real host: user-level registration in `~/.zcode/cli/config.json`, exit-code semantics (0 passes, 2 is treated as a block, other non-zero values are recorded as `hook.run.failed`), and the `provider/modelId` name format with case-sensitive matching. Still unverified: the event `cwd` project field and project-scoped registration — feedback welcome.
- Each build targets exactly one platform; for other architectures, build from source as described in the README.

<a id="v001-zh-cn"></a>
### 简体中文

首个预览版本。PromptHook 是单文件命令行 hook：agent 会话启动时按模型名选择一份 Markdown 提示词，从 stdin 读取宿主事件输入，并按目标 agent 的 hook 协议把提示词原文注入会话。无守护进程、无网络请求、无运行时依赖（自包含 NativeAOT）。

#### ✨ 新增

- **四层提示词选择**：项目 × agent → 项目 → 全局 × agent → 全局。每次运行恰好选择一个 Markdown 文件，绝不合并；只有"未配置"才降级——选中的文件缺失或为空视为错误，绝不静默回退到低优先级规则。
- **模型名候选匹配**：宿主上报 `provider/model` 时，每层先试完整名，再去首个 `/` 前缀形式；命中哪个完全由配置决定。
- **别名直取**：`prompt-hook zcode --alias review` 输出 `aliases` 下配置的提示词，跳过模型与项目匹配。
- **ZCode 适配器（SessionStart）**：从 stdin 读取事件 JSON，输出 `hookSpecificOutput.additionalContext`；stdout 只承载协议结果，诊断走 stderr。
- **完整命令行**：`--model` / `--project` / `--config` / `--alias` / `--global` / `--debug`；help 与版本查询不读 stdin。
- **严格配置校验**：重复 JSON 键、未知字段、类型错误以行/列或 JSON 路径明细拒绝；相对 Markdown 路径按配置文件所在目录解析。
- **明确的退出码**：0 = 已注入或正常未命中；1 = 配置/输入/文件错误；2 = 参数错误。

#### 🧪 测试

- 97 个 xUnit 用例，覆盖 CLI、配置、选择器、适配器与端到端流程。

#### 📦 安装

从 [v0.0.1 release](https://github.com/banned2054/PromptHook/releases/tag/v0.0.1) 下载对应平台的包并解压到任意位置——得到两个文件：`prompt-hook`（Windows 下为 `.exe`）和 `prompt-hook.json`：

| 包 | 平台 |
| --- | --- |
| `prompt-hook-v0.0.1-linux-x64.tar.gz` | Linux x64 |
| `prompt-hook-v0.0.1-win-x64.zip` | Windows x64 |
| `prompt-hook-v0.0.1-osx-arm64.tar.gz` | macOS Apple Silicon |

Linux/macOS 首次使用前执行 `chmod +x prompt-hook`。配置格式、选择优先级与 ZCode 接入见 [README](https://github.com/banned2054/PromptHook/blob/master/Docs/README.md)。所有包的 SHA-256 校验和见发布附件 `SHA256SUMS.txt`。

#### 📝 说明

- 唯一支持的 agent 是 `zcode`。
- ZCode 宿主接入已经真实宿主实测：用户级注册于 `~/.zcode/cli/config.json`，退出码语义（0 通过、2 视为拦截、其他非零记为 `hook.run.failed`），以及 `provider/modelId` 名称格式与区分大小写匹配。仍未核实：事件 `cwd` 项目字段与项目级注册——欢迎反馈。
- 每个构建只面向一个平台；其他架构请按 README 从源码构建。
