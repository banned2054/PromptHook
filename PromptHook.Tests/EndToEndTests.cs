using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace PromptHook.Tests;

/// <summary>
/// 以子进程方式运行 prompt-hook，验证退出码、stdout/stderr 协议边界。
/// 运行于主项目的 Debug（或测试配置同名）构建产物上。
/// </summary>
public class EndToEndTests
{
    private static readonly Lazy<(string FileName, string[] PrefixArgs)> Host = new(ResolveHost);

    private static (string FileName, string[] PrefixArgs) ResolveHost()
    {
        var testBin    = Path.TrimEndingDirectorySeparator(AppContext.BaseDirectory);
        var repoRoot   = Path.GetFullPath(Path.Combine(testBin, "..", "..", "..", ".."));
        var configName = Path.GetFileName(Path.GetDirectoryName(testBin)); // Debug / Release
        var exe        = Path.Combine(repoRoot, "PromptHook", "bin", configName, "net10.0", "prompt-hook.exe");
        if (File.Exists(exe))
            return (exe, []);

        var dll = typeof(global::PromptHook.Program).Assembly.Location;
        Assert.False(string.IsNullOrEmpty(dll), "未找到主项目构建产物");
        return ("dotnet", ["exec", dll]);
    }

    private static (int ExitCode, string Stdout, string Stderr) Run(
        string[] args,
        string?  stdinText  = null,
        byte[]?  stdinBytes = null,
        string?  workingDir = null,
        string?  projectEnv = "")
    {
        var start = new ProcessStartInfo
        {
            FileName               = Host.Value.FileName,
            UseShellExecute        = false,
            RedirectStandardInput  = true,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding  = Encoding.UTF8,
            StandardInputEncoding  = Encoding.UTF8,
            WorkingDirectory       = workingDir ?? AppContext.BaseDirectory,
            EnvironmentVariables =
            {
                // 默认显式清空，避免测试宿主环境干扰子进程的项目解析
                ["ZCODE_PROJECT_DIR"] = projectEnv ?? string.Empty
            }
        };

        foreach (var prefix in Host.Value.PrefixArgs)
            start.ArgumentList.Add(prefix);
        foreach (var arg in args)
            start.ArgumentList.Add(arg);

        using var process = Process.Start(start)!;

        var stdinTask = Task.Run(() =>
        {
            try
            {
                if (stdinBytes is not null)
                    process.StandardInput.BaseStream.Write(stdinBytes, 0, stdinBytes.Length);
                else if (!string.IsNullOrEmpty(stdinText))
                    process.StandardInput.Write(stdinText); // 空 stdin 不写字节，仅关闭以模拟空输入
            }
            catch (IOException)
            {
            }
            finally
            {
                try
                {
                    process.StandardInput.Close();
                }
                catch (IOException)
                {
                }
            }
        });
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit(60_000))
        {
            try
            {
                process.Kill(entireProcessTree : true);
            }
            catch (InvalidOperationException)
            {
            }

            throw new TimeoutException("prompt-hook 子进程未在 60 秒内退出");
        }

        Task.WaitAll(stdinTask, stdoutTask, stderrTask);
        return (process.ExitCode, stdoutTask.Result, stderrTask.Result);
    }

    private static string Serialize(object value) => JsonSerializer.Serialize(value);

    private static void AssertExit(int expected, int actual, string stdout, string stderr)
        => Assert.True(expected == actual,
                       $"exit={actual} (expected {expected}){Environment.NewLine}stderr: {stderr}{Environment.NewLine}stdout: {stdout}");

    [Fact]
    public void Success_GlobalModelRule_OutputsHookJson()
    {
        var dir = TestPaths.CreateTempDir();
        try
        {
            const string markdown = "# 会话交接\n\n- 中文 \"引号\" <标签> & 符号\n- 最后一行";
            var prompt = TestPaths.WriteFile(dir, "prompts/g.md", markdown);
            var config = TestPaths.WriteFile(dir, "prompt-hook.json", Serialize(new { models = new { m1 = prompt } }));

            var (code, stdout, stderr) =
                Run(["zcode", "--config", config], stdinText : Serialize(new { model = "m1" }));

            Assert.Equal(0, code);
            Assert.Equal(string.Empty, stderr);
            using var doc  = JsonDocument.Parse(stdout);
            var       hook = doc.RootElement.GetProperty("hookSpecificOutput");
            Assert.Equal("SessionStart", hook.GetProperty("hookEventName").GetString());
            Assert.Equal(markdown, hook.GetProperty("additionalContext").GetString());
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void ProviderPrefixAndModelObject_Routed()
    {
        var dir = TestPaths.CreateTempDir();
        try
        {
            var prompt = TestPaths.WriteFile(dir, "p.md", "PROVIDER OK");
            var config = TestPaths.WriteFile(dir, "prompt-hook.json", Serialize(new { models = new { m1 = prompt } }));

            foreach (var stdin in new[]
                     {
                         Serialize(new { model = "804526c5-uuid/m1" }),
                         Serialize(new { model = new { id    = "m1" } }),
                         Serialize(new { model = new { name  = "m1" } }),
                         Serialize(new { model = new { model = "m1" } }),
                     })
            {
                var (code, stdout, stderr) = Run(["zcode", "--config", config], stdinText : stdin);
                Assert.Equal(0, code);
                Assert.Equal(string.Empty, stderr);
                using var doc = JsonDocument.Parse(stdout);
                Assert.Equal("PROVIDER OK",
                             doc.RootElement.GetProperty("hookSpecificOutput").GetProperty("additionalContext")
                                .GetString());
            }
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void MissingModel_NoOutput_ExitZero()
    {
        var dir = TestPaths.CreateTempDir();
        try
        {
            var config = TestPaths.WriteFile(dir, "prompt-hook.json", """{"models": {"m1": "p.md"}}""");
            var (code, stdout, stderr) = Run(["zcode", "--config", config], stdinText : """{"session_id":"abc"}""");
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, stdout);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void UnknownModel_NoOutput_ExitZero()
    {
        var dir = TestPaths.CreateTempDir();
        try
        {
            var config = TestPaths.WriteFile(dir, "prompt-hook.json", """{"models": {"m1": "p.md"}}""");
            var (code, stdout, stderr) = Run(["zcode", "--config", config], stdinText : """{"model":"other"}""");
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, stdout);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void EmptyStdin_NoOutput_ExitZero()
    {
        var dir = TestPaths.CreateTempDir();
        try
        {
            var config = TestPaths.WriteFile(dir, "prompt-hook.json", "{}");
            var (code, stdout, stderr) = Run(["zcode", "--config", config], stdinText : "");
            AssertExit(0, code, stdout, stderr);
            Assert.Equal(string.Empty, stdout);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void InvalidJson_ExitOne_StderrOnly()
    {
        var dir = TestPaths.CreateTempDir();
        try
        {
            var config = TestPaths.WriteFile(dir, "prompt-hook.json", "{}");
            var (code, stdout, stderr) = Run(["zcode", "--config", config], stdinText : "{oops");
            Assert.Equal(1, code);
            Assert.Equal(string.Empty, stdout);
            Assert.Contains("stdin", stderr);
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void OversizedStdin_ExitOne()
    {
        var dir = TestPaths.CreateTempDir();
        try
        {
            var config = TestPaths.WriteFile(dir, "prompt-hook.json", "{}");
            var bytes  = Encoding.ASCII.GetBytes("{\"model\":\"" + new string('a', HookInput.MaxBytes) + "\"}");
            Assert.True(bytes.Length > HookInput.MaxBytes);
            var (code, stdout, stderr) = Run(["zcode", "--config", config], stdinBytes : bytes);
            Assert.Equal(1, code);
            Assert.Equal(string.Empty, stdout);
            Assert.Contains("上限", stderr);
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void NonTargetEvent_NoOutput_ExitZero()
    {
        var dir = TestPaths.CreateTempDir();
        try
        {
            var config = TestPaths.WriteFile(dir, "prompt-hook.json", """{"models": {"m1": "p.md"}}""");
            foreach (var stdin in new[]
                     {
                         """{"hook_event_name":"UserPromptSubmit","model":"m1"}""",
                         """{"hookEventName":"PreToolUse","model":"m1"}"""
                     })
            {
                var (code, stdout, stderr) = Run(["zcode", "--config", config], stdinText : stdin);
                Assert.Equal(0, code);
                Assert.Equal(string.Empty, stdout);
                Assert.Equal(string.Empty, stderr);
            }
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void MatchedPromptFileBroken_NoFallbackToGlobal()
    {
        // 关键语义：项目规则命中但文件缺失时必须报错，不得偷偷改用低优先级规则
        var dir = TestPaths.CreateTempDir();
        try
        {
            var project = TestPaths.CreateTempDir();
            var valid   = TestPaths.WriteFile(dir, "valid.md", "GLOBAL CONTENT");
            var config = TestPaths.WriteFile(dir, "prompt-hook.json", Serialize(new
            {
                models = new { m1 = valid },
                projects = new Dictionary<string, object>
                {
                    [project] = new { models = new { m1 = Path.Combine(dir, "missing.md") } },
                },
            }));

            var (code, stdout, stderr) = Run(["zcode", "--config", config],
                                             stdinText : Serialize(new { model = "m1", cwd = project }));

            Assert.Equal(1, code);
            Assert.Equal(string.Empty, stdout);
            Assert.Contains("提示词", stderr);
            TestPaths.Cleanup(project);
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void ProjectRuleBeatsGlobal_GlobalModeIgnoresProject()
    {
        var dir = TestPaths.CreateTempDir();
        try
        {
            var project       = TestPaths.CreateTempDir();
            var globalPrompt  = TestPaths.WriteFile(dir, "g.md", "GLOBAL");
            var projectPrompt = TestPaths.WriteFile(dir, "p.md", "PROJECT");
            var config = TestPaths.WriteFile(dir, "prompt-hook.json", Serialize(new
            {
                models   = new { m1                                   = globalPrompt },
                projects = new Dictionary<string, object> { [project] = new { models = new { m1 = projectPrompt } } },
            }));

            var stdin = Serialize(new { model = "m1", cwd = project });

            var (code, stdout, stderr) = Run(["zcode", "--config", config], stdinText : stdin);
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, stderr);
            using (var doc = JsonDocument.Parse(stdout))
                Assert.Equal("PROJECT",
                             doc.RootElement.GetProperty("hookSpecificOutput").GetProperty("additionalContext")
                                .GetString());

            var (gCode, gStdout, gStderr) = Run(["zcode", "-g", "--config", config], stdinText : stdin);
            Assert.Equal(0, gCode);
            Assert.Equal(string.Empty, gStderr);
            using (var doc = JsonDocument.Parse(gStdout))
                Assert.Equal("GLOBAL",
                             doc.RootElement.GetProperty("hookSpecificOutput").GetProperty("additionalContext")
                                .GetString());

            TestPaths.Cleanup(project);
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void AliasSelectsPrompt_MissingAliasErrors()
    {
        var dir = TestPaths.CreateTempDir();
        try
        {
            var prompt = TestPaths.WriteFile(dir, "r.md", "REVIEW RULES");
            var config = TestPaths.WriteFile(dir, "prompt-hook.json",
                                             Serialize(new { aliases = new { review = prompt } }));

            var (code, stdout, stderr) = Run(["zcode", "--alias", "review", "--config", config], stdinText : "");
            AssertExit(0, code, stdout, stderr);
            Assert.Equal(string.Empty, stderr);
            using (var doc = JsonDocument.Parse(stdout))
                Assert.Equal("REVIEW RULES",
                             doc.RootElement.GetProperty("hookSpecificOutput").GetProperty("additionalContext")
                                .GetString());

            var (errCode, errStdout, errStderr) = Run(["zcode", "--alias", "nope", "--config", config], stdinText : "");
            Assert.Equal(1, errCode);
            Assert.Equal(string.Empty, errStdout);
            Assert.Contains("nope", errStderr);
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void PromptFileProblems_Reported()
    {
        var dir = TestPaths.CreateTempDir();
        try
        {
            // 超过 64 KiB
            var big       = TestPaths.WriteFile(dir, "big.md", new string('a', PromptReader.MaxBytes + 1));
            var bigConfig = TestPaths.WriteFile(dir, "big.json", Serialize(new { models = new { m1 = big } }));
            var (bigCode, bigStdout, bigStderr) =
                Run(["zcode", "--config", bigConfig], stdinText : """{"model":"m1"}""");
            Assert.Equal(1, bigCode);
            Assert.Equal(string.Empty, bigStdout);
            Assert.Contains("上限", bigStderr);

            // 空文件
            var empty       = TestPaths.WriteFile(dir, "empty.md", "");
            var emptyConfig = TestPaths.WriteFile(dir, "empty.json", Serialize(new { models = new { m1 = empty } }));
            var (emptyCode, emptyStdout, emptyStderr) =
                Run(["zcode", "--config", emptyConfig], stdinText : """{"model":"m1"}""");
            Assert.Equal(1, emptyCode);
            Assert.Equal(string.Empty, emptyStdout);
            Assert.Contains("为空", emptyStderr);

            // 非法 UTF-8
            var bad       = TestPaths.WriteBytes(dir, "bad.md", [0xFF, 0xFE, 0x41]);
            var badConfig = TestPaths.WriteFile(dir, "bad.json", Serialize(new { models = new { m1 = bad } }));
            var (badCode, badStdout, badStderr) =
                Run(["zcode", "--config", badConfig], stdinText : """{"model":"m1"}""");
            Assert.Equal(1, badCode);
            Assert.Equal(string.Empty, badStdout);
            Assert.Contains("UTF-8", badStderr);
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void UsageErrors_Exit2_StderrOnly()
    {
        foreach (var args in new[]
                 {
                     new[] { "claude" }, new[] { "zcode", "--bogus" }, new[] { "zcode", "--model" },
                     Array.Empty<string>()
                 })
        {
            var (code, stdout, stderr) = Run(args, stdinText : "{}");
            Assert.Equal(2, code);
            Assert.Equal(string.Empty, stdout);
            Assert.NotEqual(string.Empty, stderr);
        }
    }

    [Fact]
    public void HelpAndVersion_ExitZero()
    {
        var (hCode, hStdout, hStderr) = Run(["--help"], stdinText : null);
        Assert.Equal(0, hCode);
        Assert.Contains("prompt-hook", hStdout);
        Assert.Contains("zcode", hStdout);
        Assert.Equal(string.Empty, hStderr);

        var (vCode, vStdout, vStderr) = Run(["--version"], stdinText : null);
        Assert.Equal(0, vCode);
        Assert.Contains("1.0.0", vStdout);
        Assert.Equal(string.Empty, vStderr);
    }

    [Fact]
    public void DefaultConfigReadFromExeDirectory_IndependentOfWorkingDir()
    {
        // 不传 --config，从任意工作目录启动：读取 exe 旁的 prompt-hook.json（示例配置为空映射）
        var dir = TestPaths.CreateTempDir();
        try
        {
            var (code, stdout, stderr) = Run(["zcode"], stdinText : """{"model":"anything"}""", workingDir : dir);
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, stdout);
            Assert.Equal(string.Empty, stderr);
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void RelativeConfigPath_ResolvedAgainstWorkingDir()
    {
        var dir = TestPaths.CreateTempDir();
        try
        {
            var prompt = TestPaths.WriteFile(dir, "p.md", "RELATIVE CONFIG");
            TestPaths.WriteFile(dir, "cfg.json", Serialize(new { models = new { m1 = prompt } }));

            var (code, stdout, stderr) = Run(["zcode", "--config", "cfg.json"], stdinText : """{"model":"m1"}""",
                                             workingDir : dir);
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, stderr);
            using var doc = JsonDocument.Parse(stdout);
            Assert.Equal("RELATIVE CONFIG",
                         doc.RootElement.GetProperty("hookSpecificOutput").GetProperty("additionalContext")
                            .GetString());
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void ProjectEnvVar_UsedAsProjectContext()
    {
        var dir = TestPaths.CreateTempDir();
        try
        {
            var project = TestPaths.CreateTempDir();
            var prompt  = TestPaths.WriteFile(dir, "p.md", "FROM ENV PROJECT");
            var config = TestPaths.WriteFile(dir, "prompt-hook.json", Serialize(new
            {
                models   = new { m1                                   = "unused.md" },
                projects = new Dictionary<string, object> { [project] = new { models = new { m1 = prompt } } },
            }));

            var (code, stdout, stderr) = Run(["zcode", "--config", config], stdinText : Serialize(new { model = "m1" }),
                                             projectEnv : project);
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, stderr);
            using var doc = JsonDocument.Parse(stdout);
            Assert.Equal("FROM ENV PROJECT",
                         doc.RootElement.GetProperty("hookSpecificOutput").GetProperty("additionalContext")
                            .GetString());
            TestPaths.Cleanup(project);
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void BareModelNameWithSlash_MatchesFullNameKeyFirst()
    {
        // 回归：裸模型名含 '/' 时不得被无条件剥离前缀
        var dir = TestPaths.CreateTempDir();
        try
        {
            var fullPrompt = TestPaths.WriteFile(dir, "full.md", "FULL NAME");
            var config = TestPaths.WriteFile(dir, "prompt-hook.json", Serialize(new
            {
                models = new Dictionary<string, string> { ["deepseek/deepseek-flash"] = fullPrompt },
            }));

            var (code, stdout, stderr) = Run(["zcode", "--config", config],
                                             stdinText : """{"model":"deepseek/deepseek-flash"}""");
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, stderr);
            using var doc = JsonDocument.Parse(stdout);
            Assert.Equal("FULL NAME",
                         doc.RootElement.GetProperty("hookSpecificOutput").GetProperty("additionalContext")
                            .GetString());
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void ProjectStrippedRule_BeatsGlobalFullNameRule_E2E()
    {
        // 回归（跨层级冲突）：宿主上报 provider 前缀模型名时，项目内去前缀规则
        // 必须赢过全局完整名规则——配置层级优先于模型候选顺序
        var dir = TestPaths.CreateTempDir();
        try
        {
            var project       = TestPaths.CreateTempDir();
            var globalPrompt  = TestPaths.WriteFile(dir, "g-full.md", "GLOBAL FULL");
            var projectPrompt = TestPaths.WriteFile(dir, "p-stripped.md", "PROJECT STRIPPED");
            var config = TestPaths.WriteFile(dir, "prompt-hook.json", Serialize(new
            {
                models = new Dictionary<string, string> { ["804526c5-uuid/m1"] = globalPrompt },
                projects = new Dictionary<string, object>
                {
                    [project] = new
                    {
                        agents = new
                            { zcode = new { models = new Dictionary<string, string> { ["m1"] = projectPrompt } } },
                    },
                },
            }));

            // 带 cwd（项目上下文）：层级① 去前缀规则命中
            var stdin = Serialize(new { model = "804526c5-uuid/m1", cwd = project });
            var (code, stdout, stderr) = Run(["zcode", "--config", config], stdinText : stdin);
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, stderr);
            using (var doc = JsonDocument.Parse(stdout))
                Assert.Equal("PROJECT STRIPPED",
                             doc.RootElement.GetProperty("hookSpecificOutput").GetProperty("additionalContext")
                                .GetString());

            // 无 cwd（无项目上下文）：只走全局层级，回退到全局完整名规则
            var (fCode, fStdout, fStderr) = Run(["zcode", "--config", config],
                                                stdinText : Serialize(new { model = "804526c5-uuid/m1" }));
            Assert.Equal(0, fCode);
            Assert.Equal(string.Empty, fStderr);
            using (var doc = JsonDocument.Parse(fStdout))
                Assert.Equal("GLOBAL FULL",
                             doc.RootElement.GetProperty("hookSpecificOutput").GetProperty("additionalContext")
                                .GetString());

            TestPaths.Cleanup(project);
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void ExplicitModel_ExactMatchOnly_NoStrippedCandidate()
    {
        // 显式 --model 只做精确匹配，不生成去前缀候选（语义不受层级/候选顺序调整影响）；
        // 对照：同样带前缀的模型由 stdin 上报时会生成去前缀候选并可命中
        var dir = TestPaths.CreateTempDir();
        try
        {
            var prompt = TestPaths.WriteFile(dir, "m1.md", "STRIPPED ONLY");
            var config = TestPaths.WriteFile(dir, "prompt-hook.json", Serialize(new
            {
                models = new Dictionary<string, string> { ["m1"] = prompt },
            }));

            var (code, stdout, stderr) = Run(["zcode", "--model", "804526c5-uuid/m1", "--config", config],
                                             stdinText : Serialize(new { model = "804526c5-uuid/m1" }));
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, stdout);
            Assert.Equal(string.Empty, stderr);

            var (hCode, hStdout, hStderr) = Run(["zcode", "--config", config],
                                                stdinText : Serialize(new { model = "804526c5-uuid/m1" }));
            Assert.Equal(0, hCode);
            Assert.Equal(string.Empty, hStderr);
            using var doc = JsonDocument.Parse(hStdout);
            Assert.Equal("STRIPPED ONLY",
                         doc.RootElement.GetProperty("hookSpecificOutput").GetProperty("additionalContext")
                            .GetString());
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void ProjectLevelAgentKeyCaseNormalized_E2E()
    {
        // 回归：项目级 agent 键配置为 "ZCode" 时必须命中项目规则，而不是静默注入全局提示词
        var dir = TestPaths.CreateTempDir();
        try
        {
            var project       = TestPaths.CreateTempDir();
            var globalPrompt  = TestPaths.WriteFile(dir, "g.md", "GLOBAL");
            var projectPrompt = TestPaths.WriteFile(dir, "p.md", "PROJECT AGENT");
            var config = TestPaths.WriteFile(dir, "prompt-hook.json", Serialize(new
            {
                models = new { m1 = globalPrompt },
                projects = new Dictionary<string, object>
                {
                    [project] = new { agents = new { ZCode = new { models = new { m1 = projectPrompt } } } },
                },
            }));

            var (code, stdout, stderr) = Run(["zcode", "--config", config],
                                             stdinText : Serialize(new { model = "m1", cwd = project }));
            Assert.Equal(0, code);
            Assert.Equal(string.Empty, stderr);
            using var doc = JsonDocument.Parse(stdout);
            Assert.Equal("PROJECT AGENT",
                         doc.RootElement.GetProperty("hookSpecificOutput").GetProperty("additionalContext")
                            .GetString());
            TestPaths.Cleanup(project);
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void OptionValueCannotSwallowFollowingOption()
    {
        // 回归：zcode --model -g 曾把 -g 当作模型名并静默退出 0
        var dir = TestPaths.CreateTempDir();
        try
        {
            var config = TestPaths.WriteFile(dir, "prompt-hook.json", "{}");
            var (code, stdout, stderr) = Run(["zcode", "--model", "-g", "--config", config],
                                             stdinText : """{"model":"m1"}""");
            Assert.Equal(2, code);
            Assert.Equal(string.Empty, stdout);
            Assert.Contains("缺少参数值", stderr);
        }
        finally
        {
            TestPaths.Cleanup(dir);
        }
    }
}