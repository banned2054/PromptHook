using System.Text.Json;
using System.Text.Json.Serialization;

namespace PromptHook;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class HookConfig
{
    [JsonPropertyName("models")]
    public Dictionary<string, string>? Models { get; set; }

    [JsonPropertyName("agents")]
    public Dictionary<string, AgentConfig>? Agents { get; set; }

    [JsonPropertyName("projects")]
    public Dictionary<string, ProjectConfig>? Projects { get; set; }

    [JsonPropertyName("aliases")]
    public Dictionary<string, string>? Aliases { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class AgentConfig
{
    [JsonPropertyName("models")]
    public Dictionary<string, string>? Models { get; set; }
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
internal sealed class ProjectConfig
{
    [JsonPropertyName("models")]
    public Dictionary<string, string>? Models { get; set; }

    [JsonPropertyName("agents")]
    public Dictionary<string, AgentConfig>? Agents { get; set; }
}

[JsonSerializable(typeof(HookConfig))]
internal partial class ConfigJsonContext : JsonSerializerContext
{
}

/// <summary>把反序列化得到的配置整理成规范化查找结构：agent 键（含项目级）小写、项目键规范化。</summary>
internal static class ConfigNormalization
{
    public static void Apply(HookConfig config)
    {
        if (config.Agents is not null)
            config.Agents = NormalizeAgentMap(config.Agents, "$.agents");

        if (config.Projects is null) return;
        // Windows 按路径不区分大小写，其他平台遵循平台路径规则（按区分大小写处理）
        var comparer   = OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var normalized = new Dictionary<string, ProjectConfig>(config.Projects.Count, comparer);
        foreach (var (key, value) in config.Projects)
        {
            // 项目级 agent 键与全局 agents 同样统一为小写，避免配置 "ZCode" 时静默错过项目规则
            if (value.Agents is not null)
                value.Agents = NormalizeAgentMap(value.Agents, $"$.projects[\"{key}\"].agents");

            var normalizedKey = ProjectPaths.NormalizeConfigKey(key);
            if (!normalized.TryAdd(normalizedKey, value))
                throw new PromptHookException(ExitCodes.IoError, $"配置 projects 存在规范化后重复的键: '{key}'");
        }

        config.Projects = normalized;
    }

    private static Dictionary<string, AgentConfig> NormalizeAgentMap(
        Dictionary<string, AgentConfig> agents, string path)
    {
        var normalized = new Dictionary<string, AgentConfig>(agents.Count, StringComparer.Ordinal);
        foreach (var (key, value) in agents)
        {
            if (string.IsNullOrWhiteSpace(key))
                throw new PromptHookException(ExitCodes.IoError, $"配置 {path} 存在空键");
            var lower = key.ToLowerInvariant();
            if (!normalized.TryAdd(lower, value))
                throw new PromptHookException(ExitCodes.IoError, $"配置 {path} 存在仅大小写不同的重复键: '{key}'");
        }

        return normalized;
    }
}

internal static class ProjectPaths
{
    // 配置中的项目键必须是绝对目录路径
    public static string NormalizeConfigKey(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new PromptHookException(ExitCodes.IoError, "配置 projects 存在空键");
        return !Path.IsPathFullyQualified(path)
            ? throw new PromptHookException(ExitCodes.IoError, $"配置 projects 键 '{path}' 必须是绝对目录路径")
            : TrimTrailingSeparator(Path.GetFullPath(path));
    }

    // 运行期得到的项目路径；相对路径按当前工作目录解析
    public static string NormalizeRuntime(string path)
    {
        try
        {
            return TrimTrailingSeparator(Path.GetFullPath(path));
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or NotSupportedException)
        {
            throw new PromptHookException(ExitCodes.IoError, $"项目路径无效 '{path}': {ex.Message}");
        }
    }

    private static string TrimTrailingSeparator(string path)
    {
        if (path.Length < 2) return path;
        var last = path[^1];
        if (last != Path.DirectorySeparatorChar && last != Path.AltDirectorySeparatorChar) return path;
        var trimmed = path[..^1];
        return trimmed[^1] != Path.VolumeSeparatorChar // 保留 "C:\" 这类盘符根
            ? trimmed
            : path;
    }
}

internal static class ConfigLoader
{
    public static HookConfig Load(string path)
    {
        byte[] bytes;
        try
        {
            bytes = File.ReadAllBytes(path);
        }
        catch (Exception ex) when (ex is FileNotFoundException
                                      or DirectoryNotFoundException
                                      or UnauthorizedAccessException
                                      or IOException)
        {
            throw new PromptHookException(ExitCodes.IoError, $"无法读取配置文件 '{path}': {ex.Message}");
        }

        var text = Utf8Text.Decode(bytes, $"配置文件 '{path}'");

        JsonDocument document;
        try
        {
            // 重复键在此处拒绝；JsonException 自带行列位置
            document = JsonDocument.Parse(text, new JsonDocumentOptions { AllowDuplicateProperties = false });
        }
        catch (JsonException ex)
        {
            throw new PromptHookException(ExitCodes.IoError, $"配置 JSON 解析失败（{path}）: {ex.Message}");
        }

        using (document)
        {
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                throw new PromptHookException(ExitCodes.IoError, $"配置 JSON 顶层必须是对象（{path}）");

            HookConfig config;
            try
            {
                // 未知字段由 JsonUnmappedMemberHandling(Disallow) 拒绝，错误信息带 JSON 路径
                config = document.RootElement.Deserialize(ConfigJsonContext.Default.HookConfig)
                      ?? throw new PromptHookException(ExitCodes.IoError, $"配置 JSON 不能为 null（{path}）");
            }
            catch (JsonException ex)
            {
                throw new PromptHookException(ExitCodes.IoError, $"配置字段结构或类型错误（{path}）: {ex.Message}");
            }

            ValidateStringMaps(document.RootElement);
            ConfigNormalization.Apply(config);
            return config;
        }
    }

    // 反序列化无法给出友好空值诊断，这里按 JSON 路径补检各层节点与模型/别名的键值
    private static void ValidateStringMaps(JsonElement root)
    {
        if (root.TryGetProperty("models", out var models))
            ValidateStringMap(models, "$.models");

        if (root.TryGetProperty("aliases", out var aliases))
            ValidateStringMap(aliases, "$.aliases");

        if (root.TryGetProperty("agents", out var agents))
        {
            RejectNullBlock(agents, "$.agents");
            if (agents.ValueKind == JsonValueKind.Object)
            {
                foreach (var agent in agents.EnumerateObject())
                {
                    RejectNullNode(agent.Value, $"$.agents.{JsonKey(agent.Name)}");
                    if (agent.Value.ValueKind != JsonValueKind.Object)
                        continue; // 其他结构错误由反序列化报告
                    if (agent.Value.TryGetProperty("models", out var agentModels))
                        ValidateStringMap(agentModels, $"$.agents.{JsonKey(agent.Name)}.models");
                }
            }
        }

        if (!root.TryGetProperty("projects", out var projects)) return;

        RejectNullBlock(projects, "$.projects");
        if (projects.ValueKind != JsonValueKind.Object) return;
        foreach (var project in projects.EnumerateObject())
        {
            var projectPath = $"$.projects.{JsonKey(project.Name)}";
            RejectNullNode(project.Value, projectPath);
            if (project.Value.ValueKind != JsonValueKind.Object)
                continue;
            if (project.Value.TryGetProperty("models", out var projectModels))
                ValidateStringMap(projectModels, projectPath + ".models");
            if (!project.Value.TryGetProperty("agents", out var projectAgents)) continue;
            RejectNullBlock(projectAgents, $"{projectPath}.agents");
            if (projectAgents.ValueKind != JsonValueKind.Object)
                continue;
            foreach (var agent in projectAgents.EnumerateObject())
            {
                RejectNullNode(agent.Value, $"{projectPath}.agents.{JsonKey(agent.Name)}");
                if (agent.Value.ValueKind != JsonValueKind.Object)
                    continue;
                if (agent.Value.TryGetProperty("models", out var agentModels))
                    ValidateStringMap(agentModels, $"{projectPath}.agents.{JsonKey(agent.Name)}.models");
            }
        }
    }

    private static void ValidateStringMap(JsonElement element, string path)
    {
        RejectNullBlock(element, path);
        if (element.ValueKind != JsonValueKind.Object)
            return; // 非对象的结构错误已由反序列化报告

        foreach (var item in element.EnumerateObject())
        {
            if (string.IsNullOrWhiteSpace(item.Name))
                throw new PromptHookException(ExitCodes.IoError, $"配置 {path} 存在空键");
            if (item.Value.ValueKind != JsonValueKind.String || string.IsNullOrWhiteSpace(item.Value.GetString()))
                throw new PromptHookException(ExitCodes.IoError, $"配置 {path}.{JsonKey(item.Name)} 的值必须是非空文件路径");
        }
    }

    private static void RejectNullBlock(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Null)
            throw new PromptHookException(ExitCodes.IoError, $"配置 {path} 不能为 null（如需省略请移除该字段）");
    }

    private static void RejectNullNode(JsonElement element, string path)
    {
        if (element.ValueKind == JsonValueKind.Null)
            throw new PromptHookException(ExitCodes.IoError, $"配置 {path} 不能为 null");
    }

    private static string JsonKey(string name) => $"[\"{name}\"]";
}