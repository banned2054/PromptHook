# PromptHook

[**English**](https://github.com/banned2054/PromptHook/blob/master/README.md) | 简体中文

[![GitHub release](https://img.shields.io/github/v/release/banned2054/PromptHook)](https://github.com/banned2054/PromptHook/releases)[![License](https://img.shields.io/badge/license-Apache_2.0-green)](../LICENSE.txt)[![.NET](https://img.shields.io/badge/.NET-10-512BD4)](https://dotnet.microsoft.com/)

**PromptHook** 是一个小型 .NET 10 命令行 hook 程序:在 agent 会话启动时按模型名选择一份 Markdown 提示词,允许按 agent 和项目覆盖,并按目标 agent 的 hook 协议输出注入内容。

运行文件只有两个,Markdown 由用户自行维护、可放在任意位置:

```text
prompt-hook.exe
prompt-hook.json
```

程序无常驻进程、无网络请求、不修改项目文件;不包含 GUI、模板语言或插件机制。

## 功能特点

- **按模型精确匹配**:模型名与别名精确、区分大小写匹配,不做子串、通配符或模型家族猜测。
- **层级覆盖**:项目规则覆盖全局规则,agent 专属规则覆盖通用规则,`--alias` 直接指定提示词。
- **单文件 NativeAOT**:自包含发布,无运行时依赖,每个平台一个可执行文件。
- **严格配置校验**:拒绝 JSON 重复键、未知字段和非法类型,并报告具体位置。
- **无副作用**:无常驻进程、无网络请求、不修改项目文件、不覆盖已有配置。
- **可诊断**:`--debug` 记录调用过程,退出码语义明确。

## 安装

从 [Releases](https://github.com/banned2054/PromptHook/releases) 下载对应平台的发布包(win-x64 为 zip,linux-x64 与 osx-arm64 为 tar.gz,附 SHA256SUMS),解压到任意目录,再编辑同目录的 `prompt-hook.json` 指向你的 Markdown 提示词即可。程序不提供自动安装或更新,也不会覆盖已有配置。

如需自行构建,参见[构建与测试](#构建与测试)。

## 命令行

```text
prompt-hook <agent> [-g|--global] [--alias <name>]
                   [--model <name>] [--project <path>]
                   [--config <path>] [--debug]
prompt-hook --help
prompt-hook --version
```

| 参数 | 说明 |
| --- | --- |
| `<agent>` | 目标 agent 标识,统一小写,当前支持 `zcode` |
| `-g`, `--global` | 忽略项目规则,仅按全局配置选择 |
| `--alias <name>` | 直接使用根节点 `aliases` 中的提示词,跳过模型与项目匹配 |
| `--model <name>` | 显式指定模型名,优先于 agent 上报的值 |
| `--project <path>` | 显式指定项目目录,优先于 agent 上报的值 |
| `--config <path>` | 配置文件路径;默认指向 exe 同目录的 `prompt-hook.json` |
| `--debug` | 将调用过程追加记录到 exe 同目录的 `prompt-hook.log` |

```text
prompt-hook zcode
prompt-hook zcode -g
prompt-hook zcode --alias review
prompt-hook zcode --model deepseek-flash --project C:/Code/.Net/PromptHook
prompt-hook zcode --debug
```

调试日志包含调用结果、模型与项目上下文、命中的提示词路径等诊断信息,不会记录 stdin 原文或 Markdown 正文。日志不可写时会向 stderr 提示,但不改变 hook 的执行结果。

- 帮助和版本查询立即返回,不读取 stdin。
- 未知 agent、未知参数、缺少参数值、重复选项均视为调用错误(退出码 2)。
- `--alias` 可与 `-g` 共用,但此时 `-g` 不额外影响结果。
- 各选项的参数值不能为空,也不能以 `-` 开头(会被识别为缺少参数值,退出码 2)。

## 配置文件

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

- 各配置区块均可省略;模型、别名对应单个非空文件路径。
- `aliases` 统一放在根节点,避免同一别名随项目或 agent 改变含义。
- Markdown 相对路径统一相对于配置文件所在目录解析,不受 agent 启动目录影响。
- 项目键使用绝对目录路径;规范化分隔符及末尾分隔符,Windows 按路径不区分大小写,其他平台按区分大小写处理。不做父目录向上搜索。
- 模型名和别名使用精确、区分大小写匹配,不做子串、通配符或模型家族猜测。
- 配置校验是严格的:拒绝 JSON 重复键、未知字段和非法字段类型,并报告具体位置;agent 键统一按小写匹配。

## 选择优先级

显式 `--alias` 直接查根节点 `aliases`;别名不存在报错,不回退到模型规则。

正常模式依次查找,每次只选择一份 Markdown,不累加:

1. 当前项目的 agent + 模型
2. 当前项目的模型
3. 全局的 agent + 模型
4. 全局的模型

`-g` 只执行第 3、4 步;没有项目上下文时同样只执行第 3、4 步。

**配置层级优先于模型候选顺序**:当宿主上报 `provider/model` 形式的模型名时(见 ZCode 接入),适配器给出"完整模型名 → 去前缀形式"两个候选;先在当前层级内依次尝试,任一候选已配置即选中,该层级所有候选均未配置才进入下一层级。因此项目内的去前缀规则优先于全局的完整名规则,agent 专属的去前缀规则也优先于通用的完整名规则;同一层级同时配置两个候选时,完整名优先。

只有"规则未配置"才回退;已选中的文件缺失、为空、内容空白或无法读取时报告错误,不偷偷改用低优先级规则。缺少模型或模型无匹配规则时正常结束(退出码 0),不输出注入内容。显式 `--model` 只按给定名字精确匹配,不生成去前缀候选。

## ZCode 接入(SessionStart)

ZCode 适配器基于 `Reference/session-start-inject.py` 确认的协议:

- 读取重定向 stdin 中的 JSON;交互终端没有重定向输入时不等待用户输入。
- `model` 支持字符串及对象中的 `id`/`name`/`model`(仅接受有效字符串);对象值不是字符串时视为未提供模型。
- 宿主上报的模型名格式为 `${providerId}/${modelId}`,`modelId` 保留原大小写(实测 `GLM-5.3-Flash`)。适配器不无条件剥离前缀:每层先按完整模型名精确匹配,未命中再尝试去掉首个 `/` 前缀的形式;是否命中完全由配置决定,因此裸模型名(包含 `/`)也能命中同名配置。
- 项目上下文优先级:`--project` → 事件 `cwd` 字段(未经真实宿主核实) → `ZCODE_PROJECT_DIR` 环境变量。
- 事件明确不是 `SessionStart`(检查 `hook_event_name`/`hookEventName`)时不注入;事件字段缺失时按本命令注册在 SessionStart 的约定处理。
- 成功输出 UTF-8 JSON:`hookSpecificOutput.hookEventName = "SessionStart"`、`hookSpecificOutput.additionalContext = Markdown 原文`,不添加任何固定文案。
- stdout 只输出协议结果;诊断写入 stderr。

### 注册 hook(已实测)

注册位置是用户级 `~/.zcode/cli/config.json` 的顶层 `hooks` 节点:

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

实测要点:

- `type: "process"` 不经过 shell 启动:`command` 必须是可执行文件的裸路径,不能内嵌引号(否则 ENOENT,宿主侧毫秒级失败);参数放在 `args` 数组中。
- 模型匹配区分大小写:宿主上报的 `modelId` 保留原大小写(如 `GLM-5.3-Flash`),配置键必须大小写完全一致,否则永远静默无注入。
- 宿主对退出码的解释:`0` 通过(stdout 为空 + 0 属正常无注入);`2` 视为拦截(block);其他非零视为错误,记 `hook.run.failed`。
- 排查:宿主结构化日志在 `~/.zcode/cli/log/zcode-YYYY-MM-DD.jsonl`,失败记 `hook.run.failed`(warn,含 hookEventName/hookIndex/source/durationMs),成功没有事件、stderr 不入日志;注入成功与否可查 `~/.zcode/cli/rollout/model-io-sess_*.jsonl` 中是否出现提示词文本。

`-g` 仅表示忽略配置中的项目规则、只按全局层级选择,与设置文件或配置文件放在哪里无关。

## 资源边界

- Markdown 上限 64 KiB(按实际字节数检查),超过报错而不截断。
- stdin 上限 1 MiB。
- Markdown 按 UTF-8 读取,支持 BOM;非法编码报错。
- 错误时不输出半截 hook JSON;stderr 简洁说明字段或路径,不回显整份输入。

## 更新日志

见 [CHANGELOG](CHANGELOG.md)。

## 许可证

本项目采用 Apache-2.0 许可证。详情请参见 [LICENSE](https://github.com/banned2054/PromptHook/blob/master/LICENSE.txt) 文件。

## 贡献

欢迎提交 Issue 和 Pull Request!

## 支持

如果你在使用过程中遇到任何问题,请创建 Issue。
