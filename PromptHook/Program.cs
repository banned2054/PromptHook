using System.Text;

namespace PromptHook;

internal static class ExitCodes
{
    public const int Success = 0;
    public const int IoError = 1; // 配置、输入 JSON、文件读取错误
    public const int Usage   = 2; // CLI 参数错误
}

/// <summary>预期错误：携带退出码与面向 stderr 的简洁诊断。</summary>
internal sealed class PromptHookException(int exitCode, string message) : Exception(message)
{
    public int ExitCode { get; } = exitCode;
}

internal static class Program
{
    private static int Main(string[] args)
    {
        // ZCode 等宿主按 UTF-8 读取捕获的流；Windows 重定向流默认跟随系统代码页，此处统一为 UTF-8
        UseUtf8ForRedirectedStreams();

        DebugLog? debugLog = null;
        try
        {
            if (CliParser.WantsDebug(args) && !CliParser.WantsHelp(args) && !CliParser.WantsVersion(args))
            {
                debugLog = new DebugLog();
                debugLog.Write("调用开始");
            }

            var exitCode = Run(args, debugLog);
            debugLog?.Write($"调用结束 exitCode={exitCode}");
            return exitCode;
        }
        catch (PromptHookException ex)
        {
            Console.Error.WriteLine($"prompt-hook: {ex.Message}");
            debugLog?.Write($"调用失败 exitCode={ex.ExitCode}: {ex.Message}");
            return ex.ExitCode;
        }
        catch (Exception ex)
        {
            // 意外错误也给出明确诊断，不伪装成功
            Console.Error.WriteLine($"prompt-hook: 意外错误 {ex.GetType().Name}: {ex.Message}");
            debugLog?.Write($"调用失败 exitCode={ExitCodes.IoError}: 意外错误 {ex.GetType().Name}: {ex.Message}");
            return ExitCodes.IoError;
        }
    }

    private static void UseUtf8ForRedirectedStreams()
    {
        // 只在流被重定向（宿主捕获）时替换写入器；交互终端保持系统控制台行为
        if (Console.IsErrorRedirected)
        {
            Console.SetError(new StreamWriter(Console.OpenStandardError(),
                                              new UTF8Encoding(encoderShouldEmitUTF8Identifier : false))
            {
                AutoFlush = true,
            });
        }

        if (Console.IsOutputRedirected)
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput(),
                                            new UTF8Encoding(encoderShouldEmitUTF8Identifier : false))
            {
                AutoFlush = true,
            });
        }
    }

    private static int Run(string[] args, DebugLog? debugLog)
    {
        // 帮助和版本查询立即返回，不读取 stdin
        if (CliParser.WantsHelp(args))
        {
            Console.Out.Write(HelpText + Environment.NewLine);
            return ExitCodes.Success;
        }

        if (CliParser.WantsVersion(args))
        {
            Console.Out.Write($"prompt-hook {VersionText}{Environment.NewLine}");
            return ExitCodes.Success;
        }

        var cli     = CliParser.Parse(args);
        var adapter = AdapterRegistry.Get(cli.Agent);
        debugLog?.Write($"参数解析成功 agent={cli.Agent}, global={cli.Global}");

        // 默认指向 exe 所在目录的 prompt-hook.json；显式相对路径按调用工作目录解析
        var configPath = Path.GetFullPath(cli.ConfigPath ?? Path.Combine(AppContext.BaseDirectory, "prompt-hook.json"));
        var config     = ConfigLoader.Load(configPath);
        debugLog?.Write($"配置读取成功 path={configPath}");

        using var input = HookInput.Read();
        debugLog?.Write(input is null ? "hook 输入为空" : "hook 输入 JSON 解析成功");
        var context = adapter.ResolveContext(input);
        if (!context.Proceed)
        {
            debugLog?.Write("当前事件不属于目标事件，跳过处理");
            return ExitCodes.Success;
        }

        // 显式 CLI 上下文优先于适配器取得的值
        var models  = cli.Model is null ? context.Models : [cli.Model];
        var project = cli.Project ?? context.ProjectRoot;
        debugLog?.Write($"上下文 agent={cli.Agent}, models=[{string.Join(", ", models)}], project={project ?? "(none)"}");

        var selected = PromptResolver.Select(config, cli.Agent, models, project, cli.Global, cli.Alias);
        if (selected is null)
        {
            debugLog?.Write("没有匹配到提示词规则，本次不输出注入内容");
            return ExitCodes.Success; // 无匹配或缺少模型：正常结束，stdout 为空
        }

        var configDirectory = Path.GetDirectoryName(configPath)!;
        var promptPath      = PromptResolver.ResolvePromptPath(configDirectory, selected);
        debugLog?.Write($"提示词规则命中 path={promptPath}");
        var markdown = PromptReader.Read(promptPath);
        debugLog?.Write($"提示词读取成功 chars={markdown.Length}");

        // stdout 只输出协议结果；直接写字节流，避免控制台代码页影响
        using var stdout = Console.OpenStandardOutput();
        adapter.WriteOutput(stdout, markdown);
        debugLog?.Write("hook 输出已写入 stdout");
        return ExitCodes.Success;
    }

    private static string VersionText => typeof(Program).Assembly.GetName().Version?.ToString(3) ?? "0.0.0";

    private const string HelpText = """
        prompt-hook <agent> [-g|--global] [--alias <name>] [--model <name>] [--project <path>] [--config <path>] [--debug]
        prompt-hook --help
        prompt-hook --version

        根据模型名选择一份 Markdown 提示词，按目标 agent 的 hook 协议输出注入内容。

        参数:
          <agent>            目标 agent 标识（小写），当前支持: zcode
          -g, --global       忽略项目规则，仅按全局配置选择
          --alias <name>     直接使用别名对应的提示词，跳过模型与项目匹配
          --model <name>     显式指定模型名，优先于 agent 上报的值
          --project <path>   显式指定项目目录，优先于 agent 上报的值
          --config <path>    配置文件路径，默认为 exe 同目录的 prompt-hook.json
          --debug            将调用过程追加记录到 exe 同目录的 prompt-hook.log
        """;
}