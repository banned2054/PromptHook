using System.Text;

namespace PromptHook.Tests;

public class ConfigTests : IDisposable
{
    private readonly string _dir = TestPaths.CreateTempDir();

    public void Dispose() => TestPaths.Cleanup(_dir);

    private HookConfig Load(string json)
    {
        var path = Path.Combine(_dir, "prompt-hook.json");
        File.WriteAllText(path, json, new UTF8Encoding(false));
        return ConfigLoader.Load(path);
    }

    private static PromptHookException LoadError(string json)
    {
        var dir = TestPaths.CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "prompt-hook.json");
            File.WriteAllText(path, json, new UTF8Encoding(false));
            return Assert.Throws<PromptHookException>(() => ConfigLoader.Load(path));
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void Parse_PlanExample()
    {
        // projects 键必须是当前平台的绝对路径，"C:/" 在 Linux CI 上不合法
        var projectKey = OperatingSystem.IsWindows() ? "C:/Code/.Net/PromptHook" : "/code/prompthook";
        var config = Load("""
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
                              "__PROJECT__": {
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
                          """
                          .Replace("__PROJECT__", projectKey));

        Assert.Equal("prompts/deepseek.md", config.Models!["deepseek-flash"]);
        Assert.Equal("prompts/deepseek-zcode.md", config.Agents!["zcode"].Models!["deepseek-flash"]);
        Assert.NotNull(config.Projects);
        var project = Assert.Single(config.Projects);
        Assert.Equal("prompts/prompthook-deepseek.md", project.Value.Models!["deepseek-flash"]);
        Assert.Equal("prompts/prompthook-zcode-deepseek.md", project.Value.Agents!["zcode"].Models!["deepseek-flash"]);
        Assert.Equal("prompts/review.md", config.Aliases!["review"]);
    }

    [Fact]
    public void OmittedBlocksAreNull()
    {
        var config = Load("{}");
        Assert.Null(config.Models);
        Assert.Null(config.Agents);
        Assert.Null(config.Projects);
        Assert.Null(config.Aliases);
    }

    [Fact]
    public void DuplicateKey_Rejected()
    {
        var ex = LoadError("""{"models": {"m1": "a.md", "m1": "b.md"}}""");
        Assert.Equal(ExitCodes.IoError, ex.ExitCode);
    }

    [Fact]
    public void UnknownField_Rejected()
    {
        var ex = LoadError("""{"models": {}, "unknownField": {}}""");
        Assert.Equal(ExitCodes.IoError, ex.ExitCode);
        Assert.Contains("unknownField", ex.Message);
    }

    [Fact]
    public void UnknownFieldInsideAgent_Rejected()
    {
        Assert.Equal(ExitCodes.IoError, LoadError("""{"agents": {"zcode": {"models": {}, "typo": 1}}}""").ExitCode);
    }

    [Fact]
    public void UnknownFieldInsideProjectAgent_Rejected()
    {
        Assert.Equal(ExitCodes.IoError,
                     LoadError("""{"projects": {"C:/x": {"agents": {"zcode": {"models": {}, "typo": 1}}}}}""")
                        .ExitCode);
    }

    [Fact]
    public void WrongFieldType_Rejected()
    {
        Assert.Equal(ExitCodes.IoError, LoadError("""{"models": "x"}""").ExitCode);
        Assert.Equal(ExitCodes.IoError, LoadError("""{"models": {"m1": 42}}""").ExitCode);
        Assert.Equal(ExitCodes.IoError, LoadError("""{"aliases": {"a": null}}""").ExitCode);
    }

    [Fact]
    public void EmptyOrBlankPathValue_Rejected()
    {
        Assert.Equal(ExitCodes.IoError, LoadError("""{"models": {"m1": ""}}""").ExitCode);
        Assert.Equal(ExitCodes.IoError, LoadError("""{"aliases": {"a": "   "}}""").ExitCode);
        Assert.Equal(ExitCodes.IoError, LoadError("""{"agents": {"zcode": {"models": {"m1": ""}}}}""").ExitCode);
    }

    [Fact]
    public void EmptyModelKey_Rejected()
    {
        Assert.Equal(ExitCodes.IoError, LoadError("""{"models": {"": "a.md"}}""").ExitCode);
    }

    [Fact]
    public void RelativeProjectKey_Rejected()
    {
        var ex = LoadError("""{"projects": {"relative/path": {"models": {}}}}""");
        Assert.Equal(ExitCodes.IoError, ex.ExitCode);
        Assert.Contains("绝对", ex.Message);
    }

    [Fact]
    public void AgentKeyNormalizedToLowercase()
    {
        // config 中 "ZCode" 键规范为小写，lookup 按 "zcode" 命中
        var config = Load("""{"agents": {"ZCode": {"models": {"m1": "a.md"}}}}""");
        Assert.NotNull(config.Agents);
        Assert.True(config.Agents.ContainsKey("zcode"));
        Assert.False(config.Agents.ContainsKey("ZCode"));
    }

    [Fact]
    public void DuplicateAgentKeysDifferingCase_Rejected()
    {
        Assert.Equal(ExitCodes.IoError, LoadError("""{"agents": {"zcode": {}, "ZCode": {}}}""").ExitCode);
    }

    [Fact]
    public void ProjectKeyTrailingSeparatorNormalized()
    {
        if (!OperatingSystem.IsWindows())
            return; // 项目键规范化中的分隔符/大小写规则按平台差异断言

        var config  = Load("""{"projects": {"C:\\Code\\PromptHook\\": {"models": {"m1": "a.md"}}}}""");
        var runtime = ProjectPaths.NormalizeRuntime("C:/Code/PromptHook/");
        Assert.True(config.Projects!.ContainsKey(runtime));
    }

    [Fact]
    public void ProjectKeyCaseInsensitiveOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            return;

        var config  = Load("""{"projects": {"C:\\Code\\PromptHook": {"models": {"m1": "a.md"}}}}""");
        var runtime = ProjectPaths.NormalizeRuntime("c:\\code\\prompthook");
        Assert.True(config.Projects!.ContainsKey(runtime));
    }

    [Fact]
    public void InvalidUtf8Config_Rejected()
    {
        var dir = TestPaths.CreateTempDir();
        try
        {
            var path = Path.Combine(dir, "prompt-hook.json");
            File.WriteAllBytes(path, [0xFF, 0xFE, 0x7B, 0x7D]); // 非法 UTF-8 前缀 + "{}"
            var ex = Assert.Throws<PromptHookException>(() => ConfigLoader.Load(path));
            Assert.Equal(ExitCodes.IoError, ex.ExitCode);
            Assert.Contains("UTF-8", ex.Message);
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void MissingConfigFile_Rejected()
    {
        var path = Path.Combine(_dir, "not-exist.json");
        var ex   = Assert.Throws<PromptHookException>(() => ConfigLoader.Load(path));
        Assert.Equal(ExitCodes.IoError, ex.ExitCode);
    }

    [Fact]
    public void NullAgentValue_RejectedAtLoad()
    {
        // 回归：{"agents":{"zcode":null}} 曾通过加载并在选择阶段触发 NullReferenceException
        var ex = LoadError("""{"agents": {"zcode": null}}""");
        Assert.Equal(ExitCodes.IoError, ex.ExitCode);
        Assert.Contains("zcode", ex.Message);
        Assert.Contains("null", ex.Message);
    }

    [Fact]
    public void NullProjectValue_RejectedAtLoad()
    {
        var ex = LoadError("""{"projects": {"C:/Code/PromptHook": null}}""");
        Assert.Equal(ExitCodes.IoError, ex.ExitCode);
        Assert.Contains("C:/Code/PromptHook", ex.Message);
    }

    [Fact]
    public void NullProjectAgentValue_RejectedAtLoad()
    {
        var ex = LoadError("""{"projects": {"C:/Code/PromptHook": {"agents": {"zcode": null}}}}""");
        Assert.Equal(ExitCodes.IoError, ex.ExitCode);
        Assert.Contains("zcode", ex.Message);
    }

    [Fact]
    public void NullRootBlocks_RejectedAtLoad()
    {
        Assert.Equal(ExitCodes.IoError, LoadError("""{"models": null}""").ExitCode);
        Assert.Equal(ExitCodes.IoError, LoadError("""{"agents": null}""").ExitCode);
        Assert.Equal(ExitCodes.IoError, LoadError("""{"projects": null}""").ExitCode);
        Assert.Equal(ExitCodes.IoError, LoadError("""{"aliases": null}""").ExitCode);
    }

    [Fact]
    public void NullNestedModels_RejectedAtLoad()
    {
        Assert.Equal(ExitCodes.IoError, LoadError("""{"agents": {"zcode": {"models": null}}}""").ExitCode);
    }

    [Fact]
    public void ProjectLevelAgentKeyNormalizedToLowercase()
    {
        var project = TestPaths.CreateTempDir();
        try
        {
            var json = Serialize(new Dictionary<string, object>
            {
                ["projects"] = new Dictionary<string, object>
                {
                    [project] = new Dictionary<string, object>
                    {
                        ["agents"] = new Dictionary<string, object>
                        {
                            ["ZCode"] = new Dictionary<string, object>
                                { ["models"] = new Dictionary<string, string> { ["m1"] = "a.md" } },
                        },
                    },
                },
            });
            var config = Load(json);
            Assert.NotNull(config.Projects);
            var projectConfig = Assert.Single(config.Projects.Values);
            Assert.NotNull(projectConfig.Agents);
            Assert.True(projectConfig.Agents.ContainsKey("zcode"));
        }
        finally
        {
            TestPaths.Cleanup(project);
        }
    }

    [Fact]
    public void ProjectLevelAgentKeyDuplicateCaseDiffering_Rejected()
    {
        var project = TestPaths.CreateTempDir();
        try
        {
            // 用显式字典构造，匿名类型无法承载仅大小写不同的成员
            var json = Serialize(new Dictionary<string, object>
            {
                ["projects"] = new Dictionary<string, object>
                {
                    [project] = new Dictionary<string, object>
                    {
                        ["agents"] = new Dictionary<string, object>
                        {
                            ["zcode"] = new { },
                            ["ZCode"] = new { },
                        },
                    },
                },
            });
            Assert.Equal(ExitCodes.IoError, LoadError(json).ExitCode);
        }
        finally
        {
            TestPaths.Cleanup(project);
        }
    }

    private static string Serialize(object value) => System.Text.Json.JsonSerializer.Serialize(value);
}