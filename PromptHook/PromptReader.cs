namespace PromptHook;

internal static class PromptReader
{
    public const int MaxBytes = 64 * 1024; // 首版 Markdown 上限，按实际字节数检查

    public static string Read(string path)
    {
        byte[] bytes;
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
                throw new FileNotFoundException(path);
            if (info.Length > MaxBytes)
                throw new PromptHookException(ExitCodes.IoError,
                                              $"已选中的提示词文件超过 {MaxBytes} 字节上限: '{path}'（{info.Length} 字节）");
            bytes = File.ReadAllBytes(path);
        }
        catch (PromptHookException)
        {
            throw;
        }
        catch (Exception ex) when (ex is FileNotFoundException
                                      or DirectoryNotFoundException
                                      or UnauthorizedAccessException
                                      or IOException)
        {
            throw new PromptHookException(ExitCodes.IoError, $"无法读取已选中的提示词文件 '{path}': {ex.Message}");
        }

        switch (bytes.Length)
        {
            case > MaxBytes :
                throw new PromptHookException(ExitCodes.IoError,
                                              $"已选中的提示词文件超过 {MaxBytes} 字节上限: '{path}'（{bytes.Length} 字节）");
            case 0 :
                throw new PromptHookException(ExitCodes.IoError, $"已选中的提示词文件为空: '{path}'");
        }

        var text = Utf8Text.Decode(bytes, $"提示词文件 '{path}'");
        return string.IsNullOrWhiteSpace(text)
            ? throw new PromptHookException(ExitCodes.IoError, $"已选中的提示词文件内容为空白: '{path}'")
            : text;
    }
}