# Changelog

All notable changes to this project will be documented in this file.

This format is based on [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this project adheres to [Semantic Versioning](https://semver.org/). Release artifacts and notes for each version live on the [Releases](https://github.com/banned2054/PromptHook/releases) page.

## 📘 Versions

- [🚀 Release v0.0.1 — Initial Preview Release](#-release-v001--initial-preview-release)

## 🚀 Release v0.0.1 — Initial Preview Release

Release Date: 2026-09-27

This is the first preview release. PromptHook is a single-file command-line hook: when an agent session starts, it selects a Markdown prompt by model name, reads the host event input from stdin, and injects the raw prompt into the session following the target agent's hook protocol. No daemon, no network requests, no runtime dependencies (self-contained NativeAOT).

### ✨ Added

- **Four-layer prompt selection**: project × agent → project → global × agent → global. Exactly one Markdown file is selected per run, never merged; only "not configured" falls back — a selected file that is missing or empty is an error, never a silent downgrade to a lower-priority rule.
- **Model-name candidate matching**: when the host reports `provider/model`, each layer tries the full name first, then the prefix-stripped name; which one hits is decided entirely by the config.
- **Alias direct access**: `prompt-hook zcode --alias review` outputs the prompt configured under `aliases`, skipping model and project matching.
- **ZCode adapter (SessionStart)**: reads the event JSON from stdin and outputs `hookSpecificOutput.additionalContext`; stdout carries only the protocol result, diagnostics go to stderr.
- **Full command line**: `--model` / `--project` / `--config` / `--alias` / `--global` / `--debug`; help and version queries do not read stdin.
- **Strict config validation**: duplicate JSON keys, unknown fields, and type errors are rejected with line/column or path details; relative Markdown paths resolve against the config file's directory.
- **Well-defined exit codes**: 0 = injected or normal no-match; 1 = config/input/file error; 2 = argument error.

### 🧪 Tests

- 97 xUnit tests covering the CLI, config, resolver, adapter, and end-to-end flows.

### 📦 Installation

Download the package for your platform from the [v0.0.1 release](https://github.com/banned2054/PromptHook/releases/tag/v0.0.1) and unpack it anywhere — you get two files, `prompt-hook` (`.exe` on Windows) and `prompt-hook.json`:

| Package | Platform |
| --- | --- |
| `prompt-hook-v0.0.1-linux-x64.tar.gz` | Linux x64 |
| `prompt-hook-v0.0.1-win-x64.zip` | Windows x64 |
| `prompt-hook-v0.0.1-osx-arm64.tar.gz` | macOS Apple Silicon |

On Linux/macOS, run `chmod +x prompt-hook` before first use. Config format, selection priority, and ZCode integration are documented in the [README](https://github.com/banned2054/PromptHook/blob/master/README.md). SHA-256 checksums for all packages are in the `SHA256SUMS.txt` release asset.

### 📝 Notes

- The only supported agent is `zcode`.
- Host-side ZCode integration has been verified against a real host: user-level registration in `~/.zcode/cli/config.json`, exit-code semantics (0 passes, 2 is treated as a block, other non-zero values are recorded as `hook.run.failed`), and the `provider/modelId` name format with case-sensitive matching. Still unverified: the event `cwd` project field and project-scoped registration — feedback welcome.
- Each build targets exactly one platform; for other architectures, build from source as described in the README.
