# Changelog

记录各版本变更;发布产物与说明以对应 [Release 页面](https://github.com/banned2054/PromptHook/releases)为准。

## v0.0.1 - 2026-09-27

首个预览版本。PromptHook 是一个单文件命令行 hook 程序:在 agent 会话启动时按模型名选择一份 Markdown 提示词,从 stdin 读取宿主事件输入,再按目标 agent 的 hook 协议将提示词原文注入会话。无常驻进程、无网络请求、无运行时依赖(NativeAOT 自包含)。

### 功能

- **四层级提示词选择**:项目 × agent → 项目 → 全局 × agent → 全局,每次恰好选中一份 Markdown,不累加;仅"未配置"才回退,已选文件缺失或为空时报错而非静默换用低优先级规则。
- **模型名候选匹配**:宿主上报 `provider/model` 形式时,每层先按完整名、再按去前缀名匹配,命中与否完全由配置决定。
- **别名直取**:`prompt-hook zcode --alias review` 直接输出 `aliases` 配置的提示词,跳过模型与项目匹配。
- **ZCode 适配(SessionStart)**:读取 stdin 中的事件 JSON,输出 `hookSpecificOutput.additionalContext` 注入内容;stdout 只有协议结果,诊断走 stderr。
- **完整命令行**:`--model`/`--project`/`--config`/`--alias`/`--global`/`--debug`,帮助与版本查询不读取 stdin。
- **严格配置校验**:拒绝 JSON 重复键、未知字段与类型错误,报错带行列或路径;Markdown 相对路径统一相对配置文件目录解析。
- **明确退出码**:0 = 注入成功或正常无匹配;1 = 配置/输入/文件错误;2 = 参数错误。
- 97 个 xUnit 测试覆盖 CLI、配置、选择器、适配器与端到端流程。

### 安装

从 [v0.0.1 Release](https://github.com/banned2054/PromptHook/releases/tag/v0.0.1) 下载对应平台的压缩包,解压得到 `prompt-hook`(`.exe`) 与 `prompt-hook.json` 两个文件,放入任意目录即可运行:

| 包 | 平台 |
| --- | --- |
| `prompt-hook-v0.0.1-linux-x64.tar.gz` | Linux x64 |
| `prompt-hook-v0.0.1-win-x64.zip` | Windows x64 |
| `prompt-hook-v0.0.1-osx-arm64.tar.gz` | macOS Apple Silicon |

Linux/macOS 首次使用需要 `chmod +x prompt-hook`。配置文件写法、选择优先级与 ZCode 接入示例见 [README](https://github.com/banned2054/PromptHook/blob/master/README.md)。各包 SHA-256 校验和见 Release 附件 `SHA256SUMS.txt`。

### 已知状态

- 当前支持的 agent 仅 `zcode`。
- ZCode 宿主侧接入(设置文件字段、事件 `cwd` 字段、退出码处理)依据参考脚本与 Claude Code 兼容惯例实现,**尚未在真实宿主中验证**——本版本的主要目的正是接入验证,欢迎反馈。
- 每个构建只对应一个平台,其他架构请从源码构建,方法见 README。
