using System.Text;

namespace HashCheck.Services;

/// <summary>把校验结果导出成 TXT。</summary>
public static class ExportService
{
    public const string Suffix = "_Hash校验结果";
    public const string Extension = ".txt";

    /// <summary>UTF-8 带 BOM + CRLF：中文内容在记事本以及较老的工具里都不会乱码。</summary>
    public static readonly Encoding Utf8WithBom = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);

    /// <summary>
    /// 目标路径 = 源文件所在目录 + “源文件名去掉扩展名 + _Hash校验结果.txt”。
    /// 例：D:\iso\setup.exe → D:\iso\setup_Hash校验结果.txt
    /// </summary>
    public static string BuildTargetPath(string sourceFilePath)
    {
        var fullPath = Path.GetFullPath(sourceFilePath);
        var directory = Path.GetDirectoryName(fullPath);
        if (string.IsNullOrEmpty(directory))
            throw new InvalidOperationException($"无法确定文件所在目录：{fullPath}");

        var baseName = Path.GetFileNameWithoutExtension(fullPath);
        if (string.IsNullOrWhiteSpace(baseName))
            baseName = Path.GetFileName(fullPath);

        return Path.Combine(directory, baseName + Suffix + Extension);
    }

    /// <summary>同名文件已存在时，生成 “xxx (2).txt” 这样的可用路径。</summary>
    public static string MakeUnique(string targetPath)
    {
        if (!File.Exists(targetPath))
            return targetPath;

        var directory = Path.GetDirectoryName(targetPath) ?? string.Empty;
        var baseName = Path.GetFileNameWithoutExtension(targetPath);
        var extension = Path.GetExtension(targetPath);

        for (var i = 2; i < 1000; i++)
        {
            var candidate = Path.Combine(directory, baseName + " (" + i + ")" + extension);
            if (!File.Exists(candidate))
                return candidate;
        }

        throw new IOException("同名文件过多，无法生成新的文件名。");
    }

    public static void Write(string targetPath, string content) =>
        File.WriteAllText(targetPath, content, Utf8WithBom);
}