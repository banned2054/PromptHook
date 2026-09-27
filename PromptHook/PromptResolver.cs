namespace PromptHook;

/// <summary>按四级优先级选择唯一一份 Markdown；只做规则选择，不做协议解释与文件 IO。</summary>
internal static class PromptResolver
{
    /// <summary>
    /// 配置层级优先于模型候选：先按①项目 agent → ②项目模型 → ③全局 agent → ④全局模型
    /// 逐层查找，层内再按候选模型顺序（完整名 → 去前缀形式）精确匹配。某层任一候选已
    /// 配置即选中，该层全部候选均未配置才进入下一层；返回配置登记的提示词路径（尚未按
    /// 配置目录解析）。无匹配或缺少模型时返回 null，由调用方按"正常结束、不输出注入
    /// 内容"处理。
    /// </summary>
    public static string? Select(
        HookConfig config, string agent, IReadOnlyList<string> models, string? project, bool globalOnly, string? alias)
    {
        // 显式别名直接查根节点 aliases；不存在报错，不回退到模型规则
        if (alias is not null)
        {
            if (config.Aliases is not null && config.Aliases.TryGetValue(alias, out var aliasPath))
                return aliasPath;
            throw new PromptHookException(ExitCodes.IoError, $"别名 '{alias}' 未在配置 aliases 中定义");
        }

        if (models.Count == 0)
            return null; // 缺少模型：正常结束，不注入

        var agentKey = agent.ToLowerInvariant();

        // 1. 当前项目的 agent + 模型；2. 当前项目的模型。-g 或无项目上下文时跳过
        if (!globalOnly && !string.IsNullOrEmpty(project) && config.Projects is not null)
        {
            var projectKey = ProjectPaths.NormalizeRuntime(project);
            if (config.Projects.TryGetValue(projectKey, out var projectConfig))
            {
                var projectAgentPrompt = MatchAgentModels(projectConfig.Agents, agentKey, models);
                if (projectAgentPrompt is not null)
                    return projectAgentPrompt;

                var projectPrompt = MatchModels(projectConfig.Models, models);
                if (projectPrompt is not null)
                    return projectPrompt;
            }
        }

        // 3. 全局的 agent + 模型；4. 全局的模型
        var agentPrompt = MatchAgentModels(config.Agents, agentKey, models);
        return agentPrompt ?? MatchModels(config.Models, models);
    }

    private static string? MatchAgentModels(
        Dictionary<string, AgentConfig>? agents, string agentKey, IReadOnlyList<string> models) =>
        agents is not null && agents.TryGetValue(agentKey, out var agentConfig)
            ? MatchModels(agentConfig.Models, models)
            : null;

    private static string? MatchModels(Dictionary<string, string>? map, IReadOnlyList<string> models)
    {
        if (map is null)
            return null;

        foreach (var model in models)
        {
            if (map.TryGetValue(model, out var prompt))
                return prompt;
        }

        return null;
    }

    // Markdown 相对路径统一相对于配置文件目录解析，不受 agent 启动目录影响
    public static string ResolvePromptPath(string configDirectory, string configuredPath) =>
        Path.GetFullPath(Path.IsPathFullyQualified(configuredPath)
                             ? configuredPath
                             : Path.Combine(configDirectory, configuredPath));
}