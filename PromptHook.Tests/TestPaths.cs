using System.Text;

namespace PromptHook.Tests;

internal static class TestPaths
{
    public static string CreateTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "prompt-hook-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        return dir;
    }

    public static string WriteFile(string dir, string relativePath, string content, bool withBom = false)
    {
        var path = Path.Combine(dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content, new UTF8Encoding(withBom));
        return path;
    }

    public static string WriteBytes(string dir, string relativePath, byte[] bytes)
    {
        var path = Path.Combine(dir, relativePath);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    public static void Cleanup(string? dir)
    {
        if (dir is null)
            return;
        try
        {
            Directory.Delete(dir, recursive : true);
        }
        catch (IOException)
        {
        }
    }
}