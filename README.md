# PromptHook

English | [**简体中文**](https://github.com/banned2054/PromptHook/blob/master/Docs/README.md)

[![GitHub release](https://img.shields.io/github/v/release/banned2054/PromptHook)](https://github.com/banned2054/PromptHook/releases)[![License](https://img.shields.io/badge/license-Apache_2.0-green)](./LICENSE.txt)[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)

**PromptHook** is a small .NET 10 command-line hook: when an agent session starts, it selects a Markdown prompt by model name — with per-agent and per-project overrides — and outputs it in the target agent's hook protocol.

The runtime consists of just two files; the Markdown prompts are maintained by you and can live anywhere:

```text
prompt-hook.exe
prompt-hook.json
```

The program has no daemon, makes no network requests, and never modifies project files. There is no GUI, template language, or plugin system.

## Features

- **Exact model matching**: model names and aliases match exactly and case-sensitively — no substring, wildcard, or model-family guessing.
- **Layered overrides**: project rules override global rules, agent-specific rules override generic ones, and `--alias` picks a prompt directly.
- **Single-file NativeAOT**: self-contained publish, no runtime dependencies, one executable per platform.
- **Strict config validation**: duplicate JSON keys, unknown fields, and invalid field types are rejected with their precise locations.
- **Side-effect free**: no daemon, no network requests, no writes to project files, never overwrites an existing config.
- **Diagnosable**: `--debug` invocation logging and well-defined exit codes.

## Installation

Download the package for your platform from [Releases](https://github.com/banned2054/PromptHook/releases) (win-x64 as zip, linux-x64 and osx-arm64 as tar.gz, with SHA256SUMS), unpack it anywhere, and point `prompt-hook.json` at your Markdown prompts. There is no installer or auto-update, and an existing config is never overwritten.

To build from source, see [Build & Test](#build--test).

## Command Line

```text
prompt-hook <agent> [-g|--global] [--alias <name>]
                   [--model <name>] [--project <path>]
                   [--config <path>] [--debug]
prompt-hook --help
prompt-hook --version
```

| Option | Description |
| --- | --- |
| `<agent>` | Target agent identifier, lowercased; currently `zcode` |
| `-g`, `--global` | Ignore project rules and select from global config only |
| `--alias <name>` | Use the prompt under the root `aliases` node directly, skipping model and project matching |
| `--model <name>` | Explicit model name; takes precedence over the value reported by the agent |
| `--project <path>` | Explicit project directory; takes precedence over the value reported by the agent |
| `--config <path>` | Config file path; defaults to `prompt-hook.json` next to the executable |
| `--debug` | Append an invocation trace to `prompt-hook.log` next to the executable |

```text
prompt-hook zcode
prompt-hook zcode -g
prompt-hook zcode --alias review
prompt-hook zcode --model deepseek-flash --project C:/Code/.Net/PromptHook
prompt-hook zcode --debug
```

The debug log records the invocation result, model and project context, and the resolved prompt path; it never logs raw stdin or the Markdown content. If the log cannot be written, a note goes to stderr without changing the hook result.

- `--help` and `--version` return immediately without reading stdin.
- Unknown agents, unknown options, missing option values, and repeated options are invocation errors (exit code 2).
- `--alias` can be combined with `-g`; in that case `-g` has no additional effect.
- Option values must be non-empty and must not start with `-` (recognized as a missing value, exit code 2).

## Configuration

```json
{
  "models": {
    "deepseek-flash": "prompts/deepseek.md"
  },
  "agents": {
    "zcode": {
      "models": {
        "deepseek-flash": "prompts/deepseek-zcode.md"
      }
    }
  },
  "projects": {
    "C:/Code/.Net/PromptHook": {
      "models": {
        "deepseek-flash": "prompts/prompthook-deepseek.md"
      },
      "agents": {
        "zcode": {
          "models": {
            "deepseek-flash": "prompts/prompthook-zcode-deepseek.md"
          }
        }
      }
    }
  },
  "aliases": {
    "review": "prompts/review.md"
  }
}
```

- Every section is optional; each model and alias maps to a single non-empty file path.
- `aliases` live at the root node, so an alias means the same thing regardless of project or agent.
- Relative Markdown paths resolve against the config file's directory, unaffected by the agent's working directory.
- Project keys are absolute directory paths; separators and trailing separators are normalized. Windows matches paths case-insensitively, other platforms case-sensitively. No parent-directory search.
- Model names and aliases match exactly and case-sensitively — no substring, wildcard, or model-family guessing.
- Validation is strict: duplicate JSON keys, unknown fields, and invalid field types are rejected with their locations; agent keys are matched lowercased.

## Selection Priority

An explicit `--alias` looks up the root `aliases` node directly; a missing alias is an error, with no fallback to model rules.

Normal mode searches the following locations in order, selecting exactly one Markdown file — never merging:

1. Current project × agent × model
2. Current project × model
3. Global × agent × model
4. Global × model

`-g` runs only steps 3–4; with no project context, likewise only steps 3–4.

**Config layers take precedence over model candidate order**: when the host reports a model name of the form `provider/model` (see ZCode integration), the adapter produces two candidates — the full name first, then the form with the first `/` prefix removed. Candidates are exhausted within the current layer first: if any candidate is configured there it is selected, and only when none match does the search move to the next layer. A prefix-stripped rule inside a project therefore beats a full-name rule globally, and an agent-specific stripped rule beats a generic full-name rule; when both candidates are configured in the same layer, the full name wins.

Only "rule not configured" falls back. If the selected file is missing, empty, blank, or unreadable, that is an error — it never silently degrades to a lower-priority rule. A missing model or a model with no matching rule ends normally (exit code 0) with no injection output. An explicit `--model` matches the given name exactly and generates no prefix-stripped candidates.

## ZCode Integration (SessionStart)

The ZCode adapter follows the protocol confirmed by `Reference/session-start-inject.py`:

- Reads JSON from redirected stdin; with an interactive terminal and no redirected input, it does not wait for user input.
- `model` accepts a string, or the `id`/`name`/`model` members of an object (valid strings only); a non-string object value counts as no model.
- The host reports model names as `${providerId}/${modelId}`, with `modelId` keeping its original case (observed: `GLM-5.3-Flash`). The adapter does not unconditionally strip the prefix: each layer first matches the full model name exactly, then the form with the first `/` prefix removed; which one hits is decided entirely by the config, so a bare model name (even one containing `/`) can match a same-named config entry.
- Project context priority: `--project` → the event's `cwd` field (not yet verified against a real host) → the `ZCODE_PROJECT_DIR` environment variable.
- If the event is explicitly not `SessionStart` (checked via `hook_event_name`/`hookEventName`), nothing is injected; a missing event field is handled per the convention that this command is registered for SessionStart.
- On success, UTF-8 JSON is written: `hookSpecificOutput.hookEventName = "SessionStart"` and `hookSpecificOutput.additionalContext = the raw Markdown`, with no fixed boilerplate added.
- stdout carries only the protocol result; diagnostics go to stderr.

### Registering the hook (verified against a real host)

Registration lives in the top-level `hooks` node of the user-level `~/.zcode/cli/config.json`:

```json
{
  "hooks": {
    "enabled": true,
    "events": {
      "SessionStart": [
        {
          "hooks": [
            {
              "type": "process",
              "command": "C:/Code/Temp/prompt-hook/prompt-hook.exe",
              "enabled": true,
              "args": ["zcode"],
              "timeoutMs": 60000
            }
          ]
        }
      ]
    }
  }
}
```

Field notes from a verified host run:

- `type: "process"` spawns without a shell: `command` must be a bare executable path with no embedded quotes (embedded quotes cause ENOENT and the hook fails within milliseconds); arguments go in the `args` array.
- Model matching is case-sensitive: the host-reported `modelId` keeps its original case (e.g. `GLM-5.3-Flash`), and config keys must match it exactly — otherwise the hook never injects anything, silently.
- Exit codes as the host sees them: `0` passes (empty stdout with 0 is a normal no-match); `2` is treated as a block; any other non-zero value is an error recorded as `hook.run.failed`.
- Troubleshooting: the host's structured log at `~/.zcode/cli/log/zcode-YYYY-MM-DD.jsonl` records failures as `hook.run.failed` (warn, with hookEventName/hookIndex/source/durationMs); successes produce no event and stderr is not logged. To confirm an injection, check whether the prompt text appears in `~/.zcode/cli/rollout/model-io-sess_*.jsonl`.

`-g` only means "ignore project rules in the config and select from global layers"; it says nothing about where the settings or config files live.

## Resource Limits

- Markdown cap: 64 KiB (checked by actual byte count); exceeding it is an error, not truncation.
- stdin cap: 1 MiB.
- Markdown is read as UTF-8, BOM supported; invalid encoding is an error.
- On error, no partial hook JSON is emitted; stderr names the offending field or path briefly and never echoes the whole input.

## Changelog

See [Docs/CHANGELOG.md](https://github.com/banned2054/PromptHook/blob/master/Docs/CHANGELOG.md).

## License

This project is licensed under the Apache-2.0 License. See the [LICENSE](https://github.com/banned2054/PromptHook/blob/master/LICENSE.txt) file for details.

## Contribution

Issues and Pull Requests are welcome!

## Support

If you run into any problems while using this program, please open an Issue on GitHub.
