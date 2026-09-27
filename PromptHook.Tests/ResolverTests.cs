namespace PromptHook.Tests;

public class ResolverTests
{
    private static HookConfig Cfg(
        Dictionary<string, string>?        models   = null,
        Dictionary<string, AgentConfig>?   agents   = null,
        Dictionary<string, ProjectConfig>? projects = null,
        Dictionary<string, string>?        aliases  = null)
    {
        var config = new HookConfig { Models = models, Agents = agents, Projects = projects, Aliases = aliases };
        ConfigNormalization.Apply(config);
        return config;
    }

    private static string TempProjectPath()
        => Path.TrimEndingDirectorySeparator(Path.Combine(Path.GetTempPath(),
                                                          "ph-resolver-" + Guid.NewGuid().ToString("N")));

    [Fact]
    public void FourLevelPriority_ProjectAgentWinsFirst()
    {
        var project = TempProjectPath();
        var config = Cfg(models : new Dictionary<string, string> { ["m1"] = "g-model.md" },
                         agents : new Dictionary<string, AgentConfig>
                         {
                             ["zcode"] = new() { Models = new Dictionary<string, string> { ["m1"] = "g-agent.md" } }
                         },
                         projects : new Dictionary<string, ProjectConfig>
                         {
                             [project] = new ProjectConfig
                             {
                                 Models = new Dictionary<string, string> { ["m1"] = "p-model.md" },
                                 Agents = new Dictionary<string, AgentConfig>
                                 {
                                     ["zcode"] = new()
                                         { Models = new Dictionary<string, string> { ["m1"] = "p-agent.md" } }
                                 },
                             },
                         });

        Assert.Equal("p-agent.md",
                     PromptResolver.Select(config, "zcode", ["m1"], project, globalOnly : false, alias : null));

        // 2. 项目的模型
        config.Projects![project].Agents!.Clear();
        Assert.Equal("p-model.md",
                     PromptResolver.Select(config, "zcode", ["m1"], project, globalOnly : false, alias : null));

        // 3. 全局的 agent + 模型
        config.Projects!.Clear();
        Assert.Equal("g-agent.md",
                     PromptResolver.Select(config, "zcode", ["m1"], project, globalOnly : false, alias : null));

        // 4. 全局的模型
        config.Agents!.Clear();
        Assert.Equal("g-model.md",
                     PromptResolver.Select(config, "zcode", ["m1"], project, globalOnly : false, alias : null));
    }

    [Fact]
    public void ProjectLevelAgentKeyNormalizedToLowercase()
    {
        // 回归：项目级 agent 键 "ZCode" 必须命中，而不是静默落到全局规则
        var project = TempProjectPath();
        var config = Cfg(models : new Dictionary<string, string> { ["m1"] = "g.md" },
                         projects : new Dictionary<string, ProjectConfig>
                         {
                             [project] = new()
                             {
                                 Agents = new Dictionary<string, AgentConfig>
                                 {
                                     ["ZCode"] = new() { Models = new Dictionary<string, string> { ["m1"] = "p.md" } }
                                 },
                             },
                         });

        Assert.Equal("p.md", PromptResolver.Select(config, "zcode", ["m1"], project, globalOnly : false, alias : null));
    }

    [Fact]
    public void GlobalMode_IgnoresProjectRules()
    {
        var project = TempProjectPath();
        var config = Cfg(models : new Dictionary<string, string> { ["m1"] = "g-model.md" },
                         agents : new Dictionary<string, AgentConfig>
                         {
                             ["zcode"] = new() { Models = new Dictionary<string, string> { ["m1"] = "g-agent.md" } }
                         },
                         projects : new Dictionary<string, ProjectConfig>
                         {
                             [project] = new() { Models = new Dictionary<string, string> { ["m1"] = "p-model.md" } }
                         });

        Assert.Equal("g-agent.md",
                     PromptResolver.Select(config, "zcode", ["m1"], project, globalOnly : true, alias : null));
    }

    [Fact]
    public void NoProjectContext_OnlyGlobalLevels()
    {
        var config = Cfg(models : new Dictionary<string, string> { ["m1"] = "g-model.md" },
                         agents : new Dictionary<string, AgentConfig>
                         {
                             ["zcode"] = new() { Models = new Dictionary<string, string> { ["m1"] = "g-agent.md" } }
                         });

        Assert.Equal("g-agent.md",
                     PromptResolver.Select(config, "zcode", ["m1"], project : null, globalOnly : false, alias : null));
    }

    [Fact]
    public void NoMatchingRule_ReturnsNull()
    {
        var config = Cfg(models : new Dictionary<string, string> { ["m1"] = "g.md" });
        Assert.Null(PromptResolver.Select(config, "zcode", ["unknown"], null, false, null));
    }

    [Fact]
    public void MissingModel_ReturnsNull()
    {
        var config = Cfg(models : new Dictionary<string, string> { ["m1"] = "g.md" });
        Assert.Null(PromptResolver.Select(config, "zcode", [], null, false, null));
        Assert.Null(PromptResolver.Select(config, "zcode", [""], null, false, null));
    }

    [Fact]
    public void ModelName_ExactCaseSensitiveMatch()
    {
        var config = Cfg(models : new Dictionary<string, string> { ["m1"] = "g.md" });
        Assert.Null(PromptResolver.Select(config, "zcode", ["M1"], null, false, null));
        Assert.Null(PromptResolver.Select(config, "zcode", ["m1 "], null, false, null));
        Assert.Null(PromptResolver.Select(config, "zcode", ["m"], null, false, null));
    }

    [Fact]
    public void ModelCandidates_FullNameWinsOverStripped()
    {
        // 回归：同一层级同时配置两个候选时，完整名必须优先于去前缀形式
        //（两个候选同层竞争，缺一不可）
        var both = Cfg(models : new Dictionary<string, string>
        {
            ["deepseek/deepseek-flash"] = "full.md",
            ["deepseek-flash"]          = "stripped.md",
        });
        Assert.Equal("full.md",
                     PromptResolver.Select(both, "zcode", ["deepseek/deepseek-flash", "deepseek-flash"], null, false,
                                           null));

        // 未配置完整名时回退到去前缀形式（宿主携带 provider 前缀的场景，候选列表与适配器输出一致）
        var strippedOnly = Cfg(models : new Dictionary<string, string> { ["deepseek-flash"] = "stripped.md" });
        Assert.Equal("stripped.md",
                     PromptResolver.Select(strippedOnly, "zcode", ["deepseek/deepseek-flash", "deepseek-flash"], null,
                                           false, null));
    }

    [Fact]
    public void ProjectAgentStrippedRule_BeatsGlobalFullNameRule()
    {
        // 回归：层级优先于候选顺序——项目内 agent 去前缀规则必须赢过全局完整名规则，
        // 即使完整名候选在层内排序更靠前
        var project = TempProjectPath();
        var config = Cfg(models : new Dictionary<string, string> { ["804526c5-uuid/m1"] = "g-full.md" },
                         projects : new Dictionary<string, ProjectConfig>
                         {
                             [project] = new()
                             {
                                 Agents = new Dictionary<string, AgentConfig>
                                 {
                                     ["zcode"] = new()
                                         { Models = new Dictionary<string, string> { ["m1"] = "p-agent-stripped.md" } }
                                 },
                             },
                         });

        Assert.Equal(
                     "p-agent-stripped.md",
                     PromptResolver.Select(config, "zcode", ["804526c5-uuid/m1", "m1"], project, globalOnly : false,
                                           alias : null));
    }

    [Fact]
    public void ProjectModelStrippedRule_BeatsGlobalFullNameRule()
    {
        // 层级② 优先于层级④：项目级去前缀规则赢过全局完整名规则
        var project = TempProjectPath();
        var config = Cfg(models : new Dictionary<string, string> { ["804526c5-uuid/m1"] = "g-full.md" },
                         projects : new Dictionary<string, ProjectConfig>
                         {
                             [project] = new()
                                 { Models = new Dictionary<string, string> { ["m1"] = "p-model-stripped.md" } }
                         });

        Assert.Equal("p-model-stripped.md",
                     PromptResolver.Select(config, "zcode", ["804526c5-uuid/m1", "m1"], project, globalOnly : false,
                                           alias : null));
    }

    [Fact]
    public void AgentStrippedRule_BeatsGenericFullNameRule()
    {
        // 层级③ 优先于层级④：agent 专属去前缀规则赢过通用完整名规则
        var config = Cfg(models : new Dictionary<string, string> { ["804526c5-uuid/m1"] = "g-full.md" },
                         agents : new Dictionary<string, AgentConfig>
                         {
                             ["zcode"] = new()
                                 { Models = new Dictionary<string, string> { ["m1"] = "g-agent-stripped.md" } }
                         });

        Assert.Equal("g-agent-stripped.md",
                     PromptResolver.Select(config, "zcode", ["804526c5-uuid/m1", "m1"], null, false, null));
    }

    [Fact]
    public void ProjectAgentLevel_FullNameBeatsStripped()
    {
        // 项目 agent 层级同时配置两个候选时，完整名优先
        var project = TempProjectPath();
        var config = Cfg(projects : new Dictionary<string, ProjectConfig>
        {
            [project] = new()
            {
                Agents = new Dictionary<string, AgentConfig>
                {
                    ["zcode"] = new()
                    {
                        Models = new Dictionary<string, string>
                        {
                            ["804526c5-uuid/m1"] = "p-agent-full.md",
                            ["m1"]               = "p-agent-stripped.md",
                        }
                    }
                },
            },
        });

        Assert.Equal("p-agent-full.md",
                     PromptResolver.Select(config, "zcode", ["804526c5-uuid/m1", "m1"], project, globalOnly : false,
                                           alias : null));
    }

    [Fact]
    public void HigherLevelUnmatched_FallsBackToLowerLevels()
    {
        // 高优先级层级存在但两个候选均未配置时，正确回退到低优先级层级
        var project = TempProjectPath();
        var config = Cfg(models : new Dictionary<string, string> { ["m1"] = "g-model.md" },
                         projects : new Dictionary<string, ProjectConfig>
                         {
                             [project] = new()
                             {
                                 Models = new Dictionary<string, string> { ["unrelated"] = "p-other.md" },
                                 Agents = new Dictionary<string, AgentConfig>
                                 {
                                     ["zcode"] = new()
                                     {
                                         Models = new Dictionary<string, string>
                                             { ["also-unrelated"] = "p-agent-other.md" }
                                     }
                                 },
                             },
                         });

        Assert.Equal("g-model.md",
                     PromptResolver.Select(config, "zcode", ["804526c5-uuid/m1", "m1"], project, globalOnly : false,
                                           alias : null));
    }

    [Fact]
    public void GlobalMode_GlobalFullNameRuleUsed_EvenWhenProjectStrippedWouldMatch()
    {
        // -g 语义不受层级/候选顺序调整影响：跳过项目层级，即使项目内去前缀规则可命中
        var project = TempProjectPath();
        var config = Cfg(models : new Dictionary<string, string> { ["804526c5-uuid/m1"] = "g-full.md" },
                         projects : new Dictionary<string, ProjectConfig>
                         {
                             [project] = new() { Models = new Dictionary<string, string> { ["m1"] = "p-stripped.md" } }
                         });

        Assert.Equal("g-full.md",
                     PromptResolver.Select(config, "zcode", ["804526c5-uuid/m1", "m1"], project, globalOnly : true,
                                           alias : null));
    }

    [Fact]
    public void Alias_OverridesModelAndProject()
    {
        var config = Cfg(models : new Dictionary<string, string> { ["m1"]      = "g.md" },
                         aliases : new Dictionary<string, string> { ["review"] = "r.md" });

        Assert.Equal("r.md", PromptResolver.Select(config, "zcode", ["m1"], null, false, "review"));
        Assert.Equal("r.md", PromptResolver.Select(config, "zcode", ["m1"], null, true, "review"));
    }

    [Fact]
    public void MissingAlias_ErrorsWithoutFallback()
    {
        var config = Cfg(models : new Dictionary<string, string> { ["m1"] = "g.md" });
        var ex = Assert.Throws<PromptHookException>(() => PromptResolver.Select(config, "zcode", ["m1"], null, false,
                                                                                    "nope"));
        Assert.Equal(ExitCodes.IoError, ex.ExitCode);
    }

    [Fact]
    public void ResolvePromptPath_RelativeUsesConfigDirectory()
    {
        var configDir = OperatingSystem.IsWindows() ? "C:\\cfg" : "/cfg";
        Assert.Equal(Path.GetFullPath(Path.Combine(configDir, "prompts/x.md")),
                     PromptResolver.ResolvePromptPath(configDir, "prompts/x.md"));
    }

    [Fact]
    public void ResolvePromptPath_AbsoluteUsedAsIs()
    {
        var absolute = OperatingSystem.IsWindows() ? "D:\\other\\p.md" : "/other/p.md";
        Assert.Equal(absolute, PromptResolver.ResolvePromptPath("C:\\cfg", absolute));
    }

    [Fact]
    public void ProjectRuntimePath_TrailingAndMixedSeparators()
    {
        if (!OperatingSystem.IsWindows())
            return;

        Assert.Equal(@"C:\Code\Proj", ProjectPaths.NormalizeRuntime("C:/Code/Proj/"));
        Assert.Equal(@"C:\Code\Proj", ProjectPaths.NormalizeRuntime(@"C:\Code\Proj\"));
        Assert.Equal(@"C:\Code\Proj", ProjectPaths.NormalizeRuntime(@"C:\Code\Proj"));
    }
}