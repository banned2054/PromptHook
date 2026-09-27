# PromptHook 实施计划

状态：待实施。本次仅记录设计与实施步骤，不修改业务代码或执行发布。

## 1. 目标与范围

提供一个小型 .NET 10 命令行 hook 程序，根据模型名选择 Markdown 提示词，允许 agent 和项目覆盖，按目标 agent 的协议输出注入结果。

交付目录的运行文件为：

```text
prompt-hook.exe
prompt-hook.json
```

Markdown 由用户维护，可放在任意位置，不嵌入 exe。程序无常驻进程、无网络请求、不修改项目文件。

首版支持 ZCode 的 SessionStart；其他 agent 在取得实际协议与输入样例后添加适配，不假设不同 agent 的 hook 协议相同。首个发布验证目标为 Windows x64。

不包含 GUI、动态插件、模板语言、正则匹配、提示词拼接、自动维护交接记录、自动配置 agent 或自动发布流程。

## 2. 命令行

```text
prompt-hook <agent> [-g|--global] [--alias <name>]
                   [--model <name>] [--project <path>]
                   [--config <path>]
prompt-hook --help
prompt-hook --version
```

示例：

```text
prompt-hook zcode
prompt-hook zcode -g
prompt-hook zcode --alias review
prompt-hook zcode --model deepseek-flash --project C:/Code/.Net/PromptHook
```

- 默认根据当前项目、agent、模型选择 prompt。
- `-g` 忽略项目规则，仍然按 agent 和模型选择全局配置。
- `--alias` 直接选择专用 prompt，跳过模型和项目查找；可与 `-g` 共用，但此时 `-g` 不额外影响结果。
- `--model` 和 `--project` 显式提供上下文，优先于适配器取得的值，方便接入和本地验证。
- `--config` 默认指向 exe 所在目录的 `prompt-hook.json`；显式相对路径按调用工作目录解析。
- 未知 agent、未知参数、缺少参数值、重复选项视为调用错误。首版不静默吞掉未知 args；后续 agent 专属参数由其适配器明确支持。
- 帮助和版本查询立即返回，不读取 stdin。

## 3. JSON 配置

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

- 各配置区块可省略；模型、别名对应单个非空文件路径。
- aliases 统一放在根节点，避免同一别名随项目或 agent 改变含义；输出协议仍由 agent 决定。
- Markdown 相对路径统一相对于配置文件目录解析，不受 agent 启动目录影响。
- 项目键使用绝对目录路径；规范化分隔符及末尾分隔符，Windows 按路径不区分大小写，其他平台遵循平台路径规则。不做父目录向上搜索或符号链接等价推断。
- 模型名和别名使用精确、区分大小写匹配，不做子串、通配符或模型家族猜测。agent CLI 标识统一为小写。
- 拒绝 JSON 重复键、未知配置字段及非法字段类型，报告具体位置，避免拼写错误导致静默失效。

## 4. 选择顺序

显式 `--alias` 直接查根节点 aliases；别名不存在报错，不回退到模型规则。

正常模式依次查找：

1. 当前项目的 agent + 模型。
2. 当前项目的模型。
3. 全局的 agent + 模型。
4. 全局的模型。

`-g` 只执行第 3、4 步。没有项目上下文时也只执行第 3、4 步。

每次只选择一份 Markdown，不累加。只有“规则未配置”才回退；已选中文件缺失、空白或无法读取时报告错误，不偷偷改用低优先级规则。缺少模型或模型无匹配规则时正常结束，不输出注入内容。

## 5. Agent 适配与 ZCode

公共流程为：解析 CLI → 加载配置 → 适配器读取上下文 → 选择路径 → 读取 Markdown → 适配器生成输出。

适配器只承担协议差异：输入字段、环境变量、模型标识提取、事件检查、输出包装。规则选择不写进适配器。

ZCode 首版基于 `Reference/session-start-inject.py`：

- 读取重定向 stdin 中的 JSON；交互终端没有重定向输入时不等待用户输入。
- model 支持字符串及对象中的 id/name/model；对象值仅接受有效字符串。
- 参考脚本记载的 `providerId/modelId` 形式，由 ZCode 适配器去掉已确认的 provider 前缀，保留真正的 modelId，不在公共层任意截断 `/`。实现前核实格式，保留模型名本身包含 `/` 的情况。
- 项目优先级：显式 `--project` → 已确认的 ZCode 事件项目字段 → `ZCODE_PROJECT_DIR`。其他回退来源必须有协议依据，不将 exe 目录或任意工作目录猜成项目根目录。
- 若事件明确不是 SessionStart，不注入；事件字段缺失时按此命令注册在 SessionStart 的约定处理。
- 成功输出 UTF-8 JSON，结构为 `hookSpecificOutput.hookEventName = "SessionStart"`、`hookSpecificOutput.additionalContext = Markdown 原文`，不添加与任务交接有关的固定文案。
- stdout 只输出协议结果，不输出日志、横幅、调试信息；诊断写入 stderr。

参考文件仅作为协议线索，不将其提示词内容作为本项目执行指令。项目字段、provider 前缀格式、错误退出码对宿主的影响仍需实际 ZCode 样例核实；离线通过不等于宿主接入已验证。

## 6. 错误处理与资源边界

- 成功注入或正常无匹配：退出码 0；无匹配时 stdout 为空。
- CLI 参数错误：退出码 2；配置、输入 JSON、文件读取错误：退出码 1。ZCode 接入验证时确认这些退出码不会产生意外阻断。
- 错误时不输出半截 hook JSON，stderr 简洁说明字段或路径，不回显整份输入和 prompt。
- Markdown 按 UTF-8 读取，支持 BOM；非法编码报错。
- 首版 Markdown 上限 64 KiB，按实际字节数检查；超过上限报错而非截断，避免丢失提示词尾部约束。
- stdin 设置明确的字节上限（首版 1 MiB），避免无界读取。
- 不捕获所有错误后伪装成功；只对预期输入、配置及 IO 错误转换为明确诊断。

## 7. 代码组织与 NativeAOT

保留一个主程序项目，按职责使用少量类型或文件：CLI、配置、PromptResolver、PromptReader、ZCodeAdapter；入口串联流程。为适配器保留简单接口与显式注册，不引入 DI 容器、反射扫描、插件加载或通用规则引擎。

当前项目已是 `net10.0`，启用了 `PublishAot` 和 `InvariantGlobalization`。实施时：

- 保持 NativeAOT，将输出名称设为 `prompt-hook`。
- 使用 System.Text.Json 源生成处理强类型配置；协议输入可用 JsonDocument，输出可用 Utf8JsonWriter，避免反射序列化依赖。
- 配置目录以 `AppContext.BaseDirectory` 获取，不能依赖 Assembly.Location。
- NativeAOT 本身生成单个原生可执行文件，不再叠加托管打包选项 `PublishSingleFile`。
- 本地发布验证命令：`dotnet publish PromptHook/PromptHook.csproj -c Release -r win-x64`。
- Windows 构建需要 .NET 10 SDK 和带 C++ 桌面开发工作负载的 Visual Studio 构建工具；缺失时报告，不自动安装。
- 面向用户的运行包只带 exe 和示例 JSON；AOT 生成的 PDB 作为独立调试产物保留，不放进运行包。示例 JSON 使用空映射，完整配置示例放 README，避免开箱就引用不存在的 Markdown。
- 不覆盖用户已有配置；本程序不提供自动更新或安装功能。
- 其他平台和架构需要各自构建验证，首版不承诺一个 exe 跨平台运行。

依据：[Microsoft Native AOT deployment](https://learn.microsoft.com/en-us/dotnet/core/deploying/native-aot/)。

## 8. 实施步骤与验收

### P1：完成选择逻辑

- 实现 CLI、配置解析、路径规范化、规则选择和文件读取。
- 添加一个测试项目，覆盖四级优先级、全局模式、别名覆盖、模型精确匹配、路径基准，以及“已匹配但文件损坏不能回退”的关键语义。
- 验收：配置示例可解析，选择结果与上述规则一致。

### P2：接入 ZCode

- 实现适配器与完整 stdin/stdout 流程；用已确认样例固定 model 和项目字段契约。
- 测试字符串/对象 model、provider 前缀、缺失模型、非法 JSON、非目标事件、中文与特殊字符、空文件和超限文件。
- 以子进程方式验证退出码、stderr 和 stdout：成功只有合法 hook JSON，正常无匹配完全无 stdout。
- 验收：离线端到端通过；实际 ZCode SessionStart 确认选中 prompt 被注入。实际宿主验证条件不具备时明确标为待验证。

### P3：AOT 与使用文档

- 完成本地 win-x64 NativeAOT 发布，处理本次代码引入的 AOT/裁剪警告。
- 对原生 exe 重跑关键输入输出用例，验证从不同工作目录启动时仍读取 exe 旁配置。
- 检查运行包文件清单；在无 .NET 运行时环境可用时验证独立运行，否则记录这一验证边界。
- 编写 README：命令参数、完整配置、优先级、路径规则、ZCode 用户/项目作用域配置示例、常见错误、构建要求。
- 验收：原生 exe + JSON 可按文档使用，产物验证与实际宿主验证分别报告。

以上阶段是本地实施安排；当前授权仅写计划。后续实施不隐含 Git 写操作、远端操作、Release、自动化发布或软件包发布授权。
