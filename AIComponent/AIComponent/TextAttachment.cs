using System.IO;
using System.Text;

namespace AIComponent;

/// <summary>An in-memory snapshot; sending a message never rereads the original file.</summary>
public sealed record TextAttachment
{
    public const int MaxFileBytes = 256 * 1024;
    public const int MaxTotalBytes = 2 * 1024 * 1024;
    public const int MaxFiles = 128;
    public string Name { get; }
    public string Content { get; }
    public int ByteLength { get; }
    internal string FullPath { get; }

    private TextAttachment(string path, string content, int byteLength)
    {
        FullPath = path;
        Name = Path.GetFileName(path);
        Content = content;
        ByteLength = byteLength;
    }

    public static async Task<TextAttachment> ReadAsync(string path, bool allowOtherTextFiles = false, string? rootDirectory = null)
    {
        var fullPath = Path.GetFullPath(path);
        if (!allowOtherTextFiles && Path.GetExtension(fullPath).ToLowerInvariant() is not (".txt" or ".text" or ".md" or ".markdown"))
            throw new InvalidDataException("仅支持 TXT 和 Markdown 文件。");

        await using var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read,
            4096, FileOptions.Asynchronous | FileOptions.SequentialScan);
        if (stream.Length > MaxFileBytes) throw new InvalidDataException("单个文件不能超过 256 KiB。");
        // Bound the read even if another process grows the file after the length check.
        var bytes = new byte[MaxFileBytes + 1];
        var length = 0;
        while (length < bytes.Length)
        {
            var read = await stream.ReadAsync(bytes.AsMemory(length));
            if (read == 0) break;
            length += read;
        }
        if (length > MaxFileBytes) throw new InvalidDataException("单个文件不能超过 256 KiB。");

        Encoding encoding = new UTF8Encoding(false, true);
        var offset = 0;
        if (length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
        { encoding = new UTF32Encoding(false, false, true); offset = 4; }
        else if (length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        { encoding = new UTF32Encoding(true, false, true); offset = 4; }
        else if (length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        { encoding = new UnicodeEncoding(false, false, true); offset = 2; }
        else if (length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        { encoding = new UnicodeEncoding(true, false, true); offset = 2; }
        else if (length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
            offset = 3;
        string content;
        try { content = encoding.GetString(bytes, offset, length - offset); }
        catch (DecoderFallbackException)
        { throw new InvalidDataException("无法解码文本，请将文件另存为 UTF-8 后重试。"); }
        if (content.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t' or '\f')))
            throw new InvalidDataException("文件包含二进制内容，无法作为文本读取。");
        return new(fullPath, content, length) { RelativeName = rootDirectory is null ? null : Path.GetRelativePath(rootDirectory, fullPath) };
    }

    private string? RelativeName { get; init; }
    public string DisplayName => RelativeName ?? Name;
}
