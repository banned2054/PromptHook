using System.Text;
using System.Text.Json;

namespace PromptHook.Tests;

/// <summary>Codex 适配器协议：输入字段与输出信封对照官方 hooks schema 核实，尚未经真实宿主样例联调。</summary>
public class CodexAdapterTests
{
    private static AdapterContext Resolve(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return new CodexAdapter().ResolveContext(doc);
    }

    [Fact]
    public void ModelSlug_SingleCandidate()
    {
        // Codex 文档：model 为 "Active model slug" 字符串
        Assert.Equal(["gpt-5.3-codex"], Resolve("""{"model":"gpt-5.3-codex"}""").Models);
    }

    [Fact]
    public void ProviderPrefix_ProducesOrderedCandidates()
    {
        // slug 携带 '/' 时与 ZCode 同策略：完整名优先，去首个前缀形式兜底
        Assert.Equal(["openai/gpt-5.3-codex", "gpt-5.3-codex"],
                     Resolve("""{"model":"openai/gpt-5.3-codex"}""").Models);
        Assert.Equal(["a/b/c", "b/c"], Resolve("""{"model":"a/b/c"}""").Models);
        Assert.Equal(["prov/"], Resolve("""{"model":"prov/"}""").Models);
    }

    // 官方 schema 中 model 恒为字符串；对象形态的解析行为由共享的 HookPayload 承担，在 ZCodeAdapterTests 覆盖

    [Fact]
    public void ModelOtherKinds_TreatedAsMissing()
    {
        Assert.Empty(Resolve("""{"model":42}""").Models);
        Assert.Empty(Resolve("""{"model":null}""").Models);
        Assert.Empty(Resolve("""{"model":true}""").Models);
        Assert.Empty(Resolve("""{"session_id":"x"}""").Models);
    }

    [Fact]
    public void NonSessionStartEvent_Skipped()
    {
        var context = Resolve("""{"hook_event_name":"UserPromptSubmit","model":"m1"}""");
        Assert.False(context.Proceed);
    }

    [Fact]
    public void SessionStartEvent_Proceeds()
    {
        Assert.True(Resolve("""{"hook_event_name":"SessionStart","model":"m1"}""").Proceed);
    }

    [Fact]
    public void MissingEventField_ProceedsByConvention()
    {
        Assert.True(Resolve("""{"model":"m1"}""").Proceed);
    }

    [Fact]
    public void CamelCaseEventField_NotPartOfCodexProtocol()
    {
        // Codex 只上报 snake_case 事件字段；camelCase 变体不属于该协议，不作为过滤依据
        Assert.True(Resolve("""{"hookEventName":"PreToolUse","model":"m1"}""").Proceed);
    }

    [Fact]
    public void CwdField_UsedAsProject()
    {
        Assert.Equal(@"C:\some\dir", Resolve("""{"cwd":"C:\\some\\dir"}""").ProjectRoot);
    }

    [Fact]
    public void InvalidCwd_NoProjectContext()
    {
        // Codex 未定义项目目录环境变量，cwd 无效时项目上下文为空而不是回退猜测
        Assert.Null(Resolve("""{"cwd":""}""").ProjectRoot);
        Assert.Null(Resolve("""{"cwd":42}""").ProjectRoot);
        Assert.Null(Resolve("{}").ProjectRoot);
    }

    [Fact]
    public void NullInput_NoModelsNoProject()
    {
        var context = new CodexAdapter().ResolveContext(null);
        Assert.Empty(context.Models);
        Assert.Null(context.ProjectRoot);
        Assert.True(context.Proceed);
    }

    [Fact]
    public void WriteOutput_ProtocolShapeAndVerbatimContent()
    {
        // 官方 schema：stdout 按 JSON 解析，注入内容位于 hookSpecificOutput.additionalContext，与 ZCode 同构
        const string markdown = "# 会话交接\n\n- 中文 \"引号\" <标签> & 符号\n";
        using var    output   = new MemoryStream();
        new CodexAdapter().WriteOutput(output, markdown);

        using var doc  = JsonDocument.Parse(Encoding.UTF8.GetString(output.ToArray()));
        var       hook = doc.RootElement.GetProperty("hookSpecificOutput");
        Assert.Equal("SessionStart", hook.GetProperty("hookEventName").GetString());
        Assert.Equal(markdown, hook.GetProperty("additionalContext").GetString());
        Assert.Single(doc.RootElement.EnumerateObject()); // 顶层只有 hookSpecificOutput
    }
}