namespace PromptHook.Tests;

public class ReaderTests : IDisposable
{
    private readonly string _dir = TestPaths.CreateTempDir();

    public void Dispose() => TestPaths.Cleanup(_dir);

    [Fact]
    public void Read_ReturnsContentVerbatim()
    {
        var content = "# 规则\n\n- 中文与 \"引号\" <标签>\n";
        var path    = TestPaths.WriteFile(_dir, "p.md", content);
        Assert.Equal(content, PromptReader.Read(path));
    }

    [Fact]
    public void Read_StripsUtf8Bom()
    {
        var path = TestPaths.WriteFile(_dir, "bom.md", "# 标题", withBom : true);
        Assert.Equal("# 标题", PromptReader.Read(path));
    }

    [Fact]
    public void Read_EmptyFile_Errors()
    {
        var path = TestPaths.WriteFile(_dir, "empty.md", "");
        var ex   = Assert.Throws<PromptHookException>(() => PromptReader.Read(path));
        Assert.Equal(ExitCodes.IoError, ex.ExitCode);
    }

    [Fact]
    public void Read_WhitespaceOnlyFile_Errors()
    {
        var path = TestPaths.WriteFile(_dir, "blank.md", "   \n\t\n");
        Assert.Equal(ExitCodes.IoError, Assert.Throws<PromptHookException>(() => PromptReader.Read(path)).ExitCode);
    }

    [Fact]
    public void Read_OversizedFile_ErrorsWithoutTruncation()
    {
        var path = TestPaths.WriteFile(_dir, "big.md", new string('a', PromptReader.MaxBytes + 1));
        var ex   = Assert.Throws<PromptHookException>(() => PromptReader.Read(path));
        Assert.Equal(ExitCodes.IoError, ex.ExitCode);
        Assert.Contains("上限", ex.Message);
    }

    [Fact]
    public void Read_ExactlyAtLimit_Ok()
    {
        var path = TestPaths.WriteFile(_dir, "edge.md", new string('a', PromptReader.MaxBytes));
        Assert.Equal(PromptReader.MaxBytes, PromptReader.Read(path).Length);
    }

    [Fact]
    public void Read_InvalidUtf8_Errors()
    {
        var path = TestPaths.WriteBytes(_dir, "bad.md", [0x23, 0x20, 0xFF, 0xFE, 0x41]);
        var ex   = Assert.Throws<PromptHookException>(() => PromptReader.Read(path));
        Assert.Equal(ExitCodes.IoError, ex.ExitCode);
        Assert.Contains("UTF-8", ex.Message);
    }

    [Fact]
    public void Read_MissingFile_Errors()
    {
        var path = Path.Combine(_dir, "not-exist.md");
        Assert.Equal(ExitCodes.IoError, Assert.Throws<PromptHookException>(() => PromptReader.Read(path)).ExitCode);
    }
}