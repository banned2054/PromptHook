using System.Text.Json;

namespace PromptHook;

/// <summary>读取宿主通过 stdin 传入的 hook 输入 JSON；这是公共协议层，字段解释交给适配器。</summary>
internal static class HookInput
{
    public const int MaxBytes = 1024 * 1024; // 首版 stdin 上限 1 MiB，避免无界读取

    public static JsonDocument? Read()
    {
        if (!Console.IsInputRedirected)
            return null; // 交互终端没有重定向输入时不等待用户输入

        int    total;
        byte[] buffer;
        try
        {
            using var stdin = Console.OpenStandardInput();
            buffer = new byte[MaxBytes + 1];
            total  = 0;
            int read;
            while ((read = stdin.Read(buffer, total, buffer.Length - total)) > 0)
            {
                total += read;
                if (total > MaxBytes)
                    throw new PromptHookException(ExitCodes.IoError, $"stdin 输入超过 {MaxBytes} 字节上限");
            }
        }
        catch (IOException ex)
        {
            throw new PromptHookException(ExitCodes.IoError, $"读取 stdin 失败: {ex.Message}");
        }

        if (total == 0)
            return null;

        var start = total >= 3 && buffer[0] == 0xEF && buffer[1] == 0xBB && buffer[2] == 0xBF ? 3 : 0;
        if (total - start == 0)
            return null; // 只有 BOM 或完全为空：视为未提供输入

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(buffer.AsMemory(start, total - start));
        }
        catch (JsonException ex)
        {
            throw new PromptHookException(ExitCodes.IoError, $"stdin JSON 解析失败: {ex.Message}");
        }

        if (document.RootElement.ValueKind == JsonValueKind.Object) return document;
        document.Dispose();
        throw new PromptHookException(ExitCodes.IoError, "stdin JSON 顶层必须是对象");
    }
}