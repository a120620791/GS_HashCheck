using System.Text;

namespace HashCheck.Hashing;

/// <summary>单个算法的结果：成功时 Hex 有值，失败时 UnsupportedReason 说明原因。</summary>
public sealed record HashValue(string Label, string? Hex, string? UnsupportedReason)
{
    public bool IsAvailable => Hex is not null;
}

/// <summary>一个文件的完整哈希结果。</summary>
public sealed record HashResult(
    string FileName,
    string FullPath,
    long Length,
    DateTime LastWriteTimeUtc,
    IReadOnlyList<HashValue> Values)
{
    public const string FileNameLabel = "文件名称";

    public HashValue? Find(string label) => Values.FirstOrDefault(v => v.Label == label);

    /// <summary>
    /// 文本框显示内容 == 导出 TXT 内容：
    /// 第 1 行“文件名称：xxx（带扩展名）”，随后每行“算法名：哈希值”。
    /// </summary>
    public string ToDisplayText()
    {
        var builder = new StringBuilder();
        builder.Append(FileNameLabel).Append('：').Append(FileName).Append("\r\n");

        foreach (var value in Values)
        {
            builder.Append(value.Label).Append('：')
                   .Append(value.Hex ?? $"不可用（{value.UnsupportedReason ?? "当前环境不支持"}）")
                   .Append("\r\n");
        }

        return builder.ToString().TrimEnd('\r', '\n');
    }
}