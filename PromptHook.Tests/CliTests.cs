namespace PromptHook.Tests;

public class CliTests
{
    private static PromptHookException Error(string[] args)
        => Assert.Throws<PromptHookException>(() => CliParser.Parse(args));

    [Fact]
    public void Parse_AllOptions()
    {
        var options = CliParser.Parse([
            "zcode", "-g", "--alias", "review", "--model", "m1", "--project", "C:/p", "--config", "c.json"
        ]);
        Assert.Equal("zcode", options.Agent);
        Assert.True(options.Global);
        Assert.Equal("review", options.Alias);
        Assert.Equal("m1", options.Model);
        Assert.Equal("C:/p", options.Project);
        Assert.Equal("c.json", options.ConfigPath);
    }

    [Fact]
    public void Parse_LongGlobalForm()
    {
        Assert.True(CliParser.Parse(["zcode", "--global"]).Global);
    }

    [Fact]
    public void Parse_AgentLowercased()
    {
        Assert.Equal("zcode", CliParser.Parse(["ZCode"]).Agent);
    }

    [Fact]
    public void Parse_UnknownAgent_Exits2()
    {
        var ex = Error(["claude"]);
        Assert.Equal(ExitCodes.Usage, ex.ExitCode);
        Assert.Contains("claude", ex.Message);
    }

    [Fact]
    public void Parse_UnknownArg_Exits2()
    {
        Assert.Equal(ExitCodes.Usage, Error(["zcode", "--bogus"]).ExitCode);
        Assert.Equal(ExitCodes.Usage, Error(["zcode", "-x"]).ExitCode);
    }

    [Fact]
    public void Parse_MissingValue_Exits2()
    {
        Assert.Equal(ExitCodes.Usage, Error(["zcode", "--model"]).ExitCode);
        Assert.Equal(ExitCodes.Usage, Error(["zcode", "--alias"]).ExitCode);
        Assert.Equal(ExitCodes.Usage, Error(["zcode", "--project"]).ExitCode);
        Assert.Equal(ExitCodes.Usage, Error(["zcode", "--config"]).ExitCode);
    }

    [Fact]
    public void Parse_DuplicateOption_Exits2()
    {
        Assert.Equal(ExitCodes.Usage, Error(["zcode", "--model", "a", "--model", "b"]).ExitCode);
        Assert.Equal(ExitCodes.Usage, Error(["zcode", "-g", "--global"]).ExitCode);
        Assert.Equal(ExitCodes.Usage, Error(["zcode", "--alias", "a", "--alias", "b"]).ExitCode);
    }

    [Fact]
    public void Parse_TwoAgents_Exits2()
    {
        Assert.Equal(ExitCodes.Usage, Error(["zcode", "claude"]).ExitCode);
    }

    [Fact]
    public void Parse_NoAgent_Exits2()
    {
        Assert.Equal(ExitCodes.Usage, Error([]).ExitCode);
        Assert.Equal(ExitCodes.Usage, Error(["--model", "m1"]).ExitCode);
    }

    [Fact]
    public void Parse_EmptyValue_Exits2()
    {
        Assert.Equal(ExitCodes.Usage, Error(["zcode", "--model", ""]).ExitCode);
    }

    [Fact]
    public void WantsHelpAndVersion()
    {
        Assert.True(CliParser.WantsHelp(["--help"]));
        Assert.True(CliParser.WantsHelp(["zcode", "--help"]));
        Assert.False(CliParser.WantsHelp(["zcode"]));
        Assert.True(CliParser.WantsVersion(["--version"]));
        Assert.False(CliParser.WantsVersion(["zcode", "-g"]));
    }
}