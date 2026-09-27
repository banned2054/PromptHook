using System.Text;
using System.Text.Json;

namespace PromptHook.Tests;

public class ZCodeAdapterTests : IDisposable
{
    private readonly string? _savedEnv = Environment.GetEnvironmentVariable("ZCODE_PROJECT_DIR");

    public ZCodeAdapterTests() => Environment.SetEnvironmentVariable("ZCODE_PROJECT_DIR", null);

    public void Dispose() => Environment.SetEnvironmentVariable("ZCODE_PROJECT_DIR", _savedEnv);

    private static AdapterContext Resolve(string json)
    {
        using var doc = JsonDocument.Parse(json);
        return new ZCodeAdapter().ResolveContext(doc);
    }

    [Fact]
    public void ModelString_SingleCandidate()
    {
        Assert.Equal(["deepseek-flash"], Resolve("""{"model":"deepseek-flash"}""").Models);
    }

    [Fact]
    public void ProviderPrefix_ProducesOrderedCandidates()
    {
        // 参考脚本实测格式 ${providerId}/${modelId}：完整名优先，去前缀形式兜底
        Assert.Equal(["804526c5-abc-uuid/deepseek-flash", "deepseek-flash"],
                     Resolve("""{"model":"804526c5-abc-uuid/deepseek-flash"}""").Models);
        // modelId 本身包含 '/' 时完整名仍是第一候选
        Assert.Equal(["prov/a/b", "a/b"], Resolve("""{"model":"prov/a/b"}""").Models);
        // 无前缀时只有一个候选
        Assert.Equal(["deepseek-flash"], Resolve("""{"model":"deepseek-flash"}""").Models);
        // 前缀后为空时不产生空候选
        Assert.Equal(["prov/"], Resolve("""{"model":"prov/"}""").Models);
    }

    [Fact]
    public void ModelObject_UsesIdThenNameThenModel()
    {
        Assert.Equal(["by-id"], Resolve("""{"model":{"id":"by-id","name":"by-name","model":"by-model"}}""").Models);
        Assert.Equal(["by-name"], Resolve("""{"model":{"name":"by-name","model":"by-model"}}""").Models);
        Assert.Equal(["by-model"], Resolve("""{"model":{"model":"by-model"}}""").Models);
    }

    [Fact]
    public void ModelObject_OnlyAcceptsValidStrings()
    {
        Assert.Empty(Resolve("""{"model":{"id":123,"name":true}}""").Models);
        Assert.Empty(Resolve("""{"model":{"id":"","name":"  "}}""").Models);
        Assert.Empty(Resolve("""{"model":{}}""").Models);
        Assert.Equal(["ok"], Resolve("""{"model":{"id":123,"name":"ok"}}""").Models);
    }

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
    public void CamelCaseEventField_AlsoChecked()
    {
        Assert.False(Resolve("""{"hookEventName":"PreToolUse","model":"m1"}""").Proceed);
    }

    [Fact]
    public void CwdField_UsedAsProject()
    {
        Assert.Equal(@"C:\some\dir", Resolve("""{"cwd":"C:\\some\\dir"}""").ProjectRoot);
    }

    [Fact]
    public void ProjectEnv_UsedWhenEventFieldMissing()
    {
        var dir = TestPaths.CreateTempDir();
        try
        {
            Environment.SetEnvironmentVariable("ZCODE_PROJECT_DIR", dir);
            Assert.Equal(dir, Resolve("{}").ProjectRoot);
            // 事件字段优先于环境变量
            Assert.Equal("C:\\evt", Resolve("""{"cwd":"C:\\evt"}""").ProjectRoot);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ZCODE_PROJECT_DIR", null);
            TestPaths.Cleanup(dir);
        }
    }

    [Fact]
    public void EmptyCwd_FallsBackToEnv()
    {
        try
        {
            Environment.SetEnvironmentVariable("ZCODE_PROJECT_DIR", "C:\\env-proj");
            Assert.Equal("C:\\env-proj", Resolve("""{"cwd":""}""").ProjectRoot);
            Assert.Equal("C:\\env-proj", Resolve("""{"cwd":42}""").ProjectRoot);
        }
        finally
        {
            Environment.SetEnvironmentVariable("ZCODE_PROJECT_DIR", null);
        }
    }

    [Fact]
    public void NullInput_NoModels_ProjectFromEnv()
    {
        var context = new ZCodeAdapter().ResolveContext(null);
        Assert.Empty(context.Models);
        Assert.True(context.Proceed);
    }

    [Fact]
    public void WriteOutput_ProtocolShapeAndVerbatimContent()
    {
        const string markdown = "# 会话交接\n\n- 中文 \"引号\" <标签> & 符号\n";
        using var    output   = new MemoryStream();
        new ZCodeAdapter().WriteOutput(output, markdown);

        using var doc  = JsonDocument.Parse(Encoding.UTF8.GetString(output.ToArray()));
        var       hook = doc.RootElement.GetProperty("hookSpecificOutput");
        Assert.Equal("SessionStart", hook.GetProperty("hookEventName").GetString());
        Assert.Equal(markdown, hook.GetProperty("additionalContext").GetString());
        Assert.Single(doc.RootElement.EnumerateObject()); // 顶层只有 hookSpecificOutput
    }
}