namespace PromptHook;

internal sealed record CliOptions(
    string  Agent,
    bool    Global,
    string? Alias,
    string? Model,
    string? Project,
    string? ConfigPath);

internal static class CliParser
{
    public static bool WantsHelp(string[] args) => Contains(args, "--help");

    public static bool WantsVersion(string[] args) => Contains(args, "--version");

    public static bool WantsDebug(string[] args) => Contains(args, "--debug");

    public static CliOptions Parse(string[] args)
    {
        string? agent      = null;
        var     global     = false;
        string? alias      = null;
        string? model      = null;
        string? project    = null;
        string? configPath = null;
        var     debug      = false;

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "-g" :
                case "--global" :
                    if (global)
                        throw new PromptHookException(ExitCodes.Usage, $"选项 {arg} 重复指定");
                    global = true;
                    break;
                case "--debug" :
                    if (debug)
                        throw new PromptHookException(ExitCodes.Usage, $"选项 {arg} 重复指定");
                    debug = true;
                    break;
                case "--alias" :
                    alias = TakeValue(args, ref i, "--alias", alias);
                    break;
                case "--model" :
                    model = TakeValue(args, ref i, "--model", model);
                    break;
                case "--project" :
                    project = TakeValue(args, ref i, "--project", project);
                    break;
                case "--config" :
                    configPath = TakeValue(args, ref i, "--config", configPath);
                    break;
                default :
                    if (arg.StartsWith('-'))
                        throw new PromptHookException(ExitCodes.Usage, $"未知参数 '{arg}'");
                    if (agent is not null)
                        throw new PromptHookException(ExitCodes.Usage, $"只能指定一个 agent，同时给出 '{agent}' 与 '{arg}'");
                    agent = arg;
                    break;
            }
        }

        if (agent is null)
            throw new PromptHookException(ExitCodes.Usage,
                                          "缺少 <agent> 参数。用法: prompt-hook <agent> [-g|--global] [--alias <name>] [--model <name>] [--project <path>] [--config <path>] [--debug]");

        // agent CLI 标识统一为小写
        agent = agent.ToLowerInvariant();
        return !AdapterRegistry.IsKnown(agent)
            ? throw new PromptHookException(ExitCodes.Usage, $"未知 agent '{agent}'，当前支持: {AdapterRegistry.KnownNames}")
            : new CliOptions(agent, global, alias, model, project, configPath);
    }

    private static bool Contains(string[] args, string token) => args.Any(arg => arg == token);

    private static string TakeValue(string[] args, ref int index, string name, string? existing)
    {
        if (existing is not null)
            throw new PromptHookException(ExitCodes.Usage, $"选项 {name} 重复指定");
        if (index + 1 >= args.Length)
            throw new PromptHookException(ExitCodes.Usage, $"选项 {name} 缺少参数值");

        var value = args[++index];
        if (value.Length == 0)
            throw new PromptHookException(ExitCodes.Usage, $"选项 {name} 的参数值不能为空");
        return value.StartsWith('-')
            ? throw new PromptHookException(ExitCodes.Usage, $"选项 {name} 缺少参数值: '{value}' 看起来是另一个选项")
            : value;
    }
}