using System.Text;

namespace PromptHook;

internal static class Utf8Text
{
    // throwOnInvalidBytes: 非法编码报错而不是替换成 U+FFFD
    private static readonly UTF8Encoding Strict =
        new(encoderShouldEmitUTF8Identifier : false, throwOnInvalidBytes : true);

    /// <summary>按 UTF-8 严格解码，支持 BOM；解码失败按配置/文件读取错误处理。</summary>
    public static string Decode(byte[] bytes, string what)
    {
        var start = 0;
        if (bytes is [0xEF, 0xBB, 0xBF, ..])
            start = 3;

        try
        {
            return Strict.GetString(bytes, start, bytes.Length - start);
        }
        catch (DecoderFallbackException)
        {
            throw new PromptHookException(ExitCodes.IoError, $"{what} 不是有效的 UTF-8 编码");
        }
    }
}