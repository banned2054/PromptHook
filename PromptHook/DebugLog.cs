using System.Text;

namespace PromptHook;

/// <summary>将调试诊断追加到 exe 同目录；写日志失败不影响 hook 结果。</summary>
internal sealed class DebugLog
{
    private readonly string _path         = Path.Combine(AppContext.BaseDirectory, "prompt-hook.log");
    private readonly string _invocationId = Guid.NewGuid().ToString("N")[..8];
    private          int    _writeFailureReported;

    public void Write(string message)
    {
        try
        {
            var line = $"{DateTimeOffset.Now:O} [{_invocationId}] {SingleLine(message)}{Environment.NewLine}";
            File.AppendAllText(_path, line, new UTF8Encoding(encoderShouldEmitUTF8Identifier : false));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            if (Interlocked.Exchange(ref _writeFailureReported, 1) == 0)
                Console.Error.WriteLine($"prompt-hook: 无法写入 debug 日志 '{_path}': {ex.Message}");
        }
    }

    private static string SingleLine(string value) => value.Replace('\r', ' ').Replace('\n', ' ');
}