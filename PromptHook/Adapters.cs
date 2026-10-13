using System.Text.Encodings.Web;
using System.Text.Json;

namespace PromptHook;

internal readonly record struct AdapterContext(IReadOnlyList<string> Models, string? ProjectRoot, bool Proceed)
{
    public static AdapterContext Skipped { get; } = new([], null, false);
}

/// <summary>适配器只承担协议差异：输入字段、环境变量、模型标识提取、事件检查、输出包装。</summary>
internal interface IAgentAdapter
{
    string Name { get; }

    /// <summary>从 hook 输入中提取模型与项目上下文；事件不属于目标时返回 Skipped。</summary>
    AdapterContext ResolveContext(JsonDocument? input);

    /// <summary>把选中的 Markdown 按宿主协议写入 stdout。</summary>
    void WriteOutput(Stream output, string markdown);
}

/// <summary>适配器显式注册，不做反射扫描。</summary>
internal static class AdapterRegistry
{
    private static readonly Dictionary<string, IAgentAdapter> Adapters = new(StringComparer.Ordinal)
    {
        ["zcode"] = new ZCodeAdapter(),
        ["codex"] = new CodexAdapter()
    };

    // 排序输出，不依赖字典的枚举顺序
    public static string KnownNames => string.Join(", ", Adapters.Keys.Order());

    public static bool IsKnown(string agent) => Adapters.ContainsKey(agent);

    public static IAgentAdapter Get(string agent)
        => Adapters.TryGetValue(agent, out var adapter)
            ? adapter
            : throw new PromptHookException(ExitCodes.Usage, $"未知 agent '{agent}'，当前支持: {KnownNames}");
}

/// <summary>各宿主 hook 输入的公共字段解析，供适配器共用。</summary>
internal static class HookPayload
{
    // model 支持字符串及对象中的 id/name/model；对象值仅接受有效字符串
    public static string? ExtractModel(JsonElement root)
    {
        if (!root.TryGetProperty("model", out var model))
            return null;

        switch (model.ValueKind)
        {
            case JsonValueKind.String :
                return NullIfEmpty(model.GetString());
            case JsonValueKind.Object :
                foreach (var key in (ReadOnlySpan<string>)["id", "name", "model"])
                    if (model.TryGetProperty(key, out var value) &&
                        value.ValueKind == JsonValueKind.String  &&
                        NullIfEmpty(value.GetString()) is { } candidate)
                        return candidate;

                return null;
            default :
                return null; // 非字符串非对象（数字、null 等）视为未提供模型
        }
    }

    // ZCode 实测格式为 ${providerId}/${modelId}（如 804526c5-.../deepseek-flash），Codex 的 model slug
    // 同样可能携带 provider 前缀；但无法假定宿主始终携带前缀，也不能排除模型名本身包含 '/'。
    // 因此输出有序候选：完整模型名优先，其次才是去掉首个 '/' 前缀的形式；
    // 是否命中完全由配置的精确匹配决定，不在公共层任意截断。
    public static List<string> ModelCandidates(string? model)
    {
        if (model is null)
            return [];

        var candidates = new List<string>(2) { model };
        var index      = model.IndexOf('/');
        if (index < 0) return candidates;
        var stripped = model[(index + 1)..];
        if (stripped.Length > 0)
            candidates.Add(stripped);

        return candidates;
    }

    public static string? TryGetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static string? NullIfEmpty(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}

/// <summary>ZCode 与 Codex 共用的输出信封：Claude Code 系的 additionalContext 包装，两宿主逐字节一致。</summary>
internal static class HookOutput
{
    public static void WriteContextEnvelope(Stream output, string eventName, string markdown)
    {
        using var writer = new Utf8JsonWriter(output, new JsonWriterOptions
        {
            // 只按 JSON 规范转义，中文等非 ASCII 字符直接输出 UTF-8
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });

        writer.WriteStartObject();
        writer.WriteStartObject("hookSpecificOutput");
        writer.WriteString("hookEventName", eventName);
        writer.WriteString("additionalContext", markdown);
        writer.WriteEndObject();
        writer.WriteEndObject();
    }
}

/// <summary>ZCode SessionStart 适配器，基于 Reference/session-start-inject.py 确认的协议。</summary>
internal sealed class ZCodeAdapter : IAgentAdapter
{
    public const string EventName = "SessionStart";

    public string Name => "zcode";

    public AdapterContext ResolveContext(JsonDocument? input)
    {
        if (input is null)
            return new AdapterContext(HookPayload.ModelCandidates(null), EnvironmentProjectDir(), true);

        var root = input.RootElement;

        // 事件字段缺失时按本命令注册在 SessionStart 的约定继续处理
        var eventName = HookPayload.TryGetString(root, "hook_event_name") ??
                        HookPayload.TryGetString(root, "hookEventName");
        if (eventName is not null && !string.Equals(eventName, EventName, StringComparison.Ordinal))
            return AdapterContext.Skipped;

        var models  = HookPayload.ModelCandidates(HookPayload.ExtractModel(root));
        var project = ExtractProject(root);
        return new AdapterContext(models, project, true);
    }

    public void WriteOutput(Stream output, string markdown)
        => HookOutput.WriteContextEnvelope(output, EventName, markdown);

    // 项目优先级中的事件字段：cwd（依据 Claude Code 兼容 hook 输入惯例，未由实际样例确认）→ ZCODE_PROJECT_DIR
    private static string? ExtractProject(JsonElement root) =>
        HookPayload.NullIfEmpty(HookPayload.TryGetString(root, "cwd")) ?? EnvironmentProjectDir();

    private static string? EnvironmentProjectDir() =>
        HookPayload.NullIfEmpty(Environment.GetEnvironmentVariable("ZCODE_PROJECT_DIR"));
}

/// <summary>
///     Codex CLI SessionStart 适配器。输入字段（hook_event_name/model/cwd）与输出信封均已对照官方
///     hooks schema（codex-rs/hooks/schema/generated/session-start.command.*.json）逐字段核实，尚未经真实宿主样例联调。
/// </summary>
internal sealed class CodexAdapter : IAgentAdapter
{
    public const string EventName = "SessionStart";

    public string Name => "codex";

    public AdapterContext ResolveContext(JsonDocument? input)
    {
        if (input is null)
            return new AdapterContext(HookPayload.ModelCandidates(null), null, true);

        var root = input.RootElement;

        // Codex 只上报 snake_case 的 hook_event_name，无 camelCase 变体；
        // 事件字段缺失时按本命令注册在 SessionStart 的约定继续处理
        var eventName = HookPayload.TryGetString(root, "hook_event_name");
        if (eventName is not null && !string.Equals(eventName, EventName, StringComparison.Ordinal))
            return AdapterContext.Skipped;

        // 项目上下文只认事件 cwd 字段：Codex 未定义项目目录环境变量
        return new AdapterContext(HookPayload.ModelCandidates(HookPayload.ExtractModel(root)),
                                  HookPayload.NullIfEmpty(HookPayload.TryGetString(root, "cwd")),
                                  true);
    }

    // 官方 schema 规定 SessionStart 的 stdout 按 JSON 解析，非 JSON 输出会被解析器丢弃；
    // 注入内容只认 hookSpecificOutput.additionalContext，信封与 ZCode 同构
    public void WriteOutput(Stream output, string markdown)
        => HookOutput.WriteContextEnvelope(output, EventName, markdown);
}