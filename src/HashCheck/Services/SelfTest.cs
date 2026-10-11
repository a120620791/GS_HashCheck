using System.Security.Cryptography;
using System.Text;
using HashCheck.Hashing;

namespace HashCheck.Services;

/// <summary>
/// 内置自检：用公开的标准测试向量校验 6 个算法的实现与集成是否正确，
/// 并验证“多算法单次读取”的管道、显示格式与导出命名规则。
///
/// 用法：HashCheck.exe --selftest [报告文件路径]      退出码 0 = 全部通过
/// 说明：无论成功还是中途抛异常，报告文件都会被写出，便于排查（GUI 程序没有控制台输出）。
/// </summary>
public static class SelfTest
{
    private static readonly (string Algo, string Text, string Expected)[] Vectors =
    [
        ("MD5",     "", "d41d8cd98f00b204e9800998ecf8427e"),
        ("SHA-1",   "", "da39a3ee5e6b4b0d3255bfef95601890afd80709"),
        ("SHA-256", "", "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855"),
        ("SHA-512", "", "cf83e1357eefb8bdf1542850d66d8007d620e4050b5715dc83f4a921d36ce9ce47d0d13c5d85f2b0ff8318d2877eec2f63b931bd47417a81a538327af927da3e"),
        ("SHA-3",   "", "a7ffc6f8bf1ed76651c14756a061d662f580ff4de43b49fa82d80a4b80f8434a"),
        ("BLAKE3",  "", "af1349b9f5f9a1a6a0404dea36dcc9499bcb25c9adc112b7cc9a93cae41f3262"),
        ("MD5",     "abc", "900150983cd24fb0d6963f7d28e17f72"),
        ("SHA-1",   "abc", "a9993e364706816aba3e25717850c26c9cd0d89d"),
        ("SHA-256", "abc", "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad"),
        ("SHA-512", "abc", "ddaf35a193617abacc417349ae20413112e6fa4e89a97ea20a9eeee64b55d39a2192992a274fc1a836ba3c23a3feebbd454d4423643ce80e2a9ac94fa54ca49f"),
        ("SHA-3",   "abc", "3a985da74fe225b2045c172d6bd390bd855f086e3e9d525b46bfe24511431532"),
        ("BLAKE3",  "abc", "6437b3ac38465133ffb63b75273a8db548c558465d79db03fd359c6cd5bd9d85"),
    ];

    public static async Task<int> RunAsync(string? reportPath)
    {
        var report = new StringBuilder();
        var failures = 0;

        var tempDir = Path.Combine(Path.GetTempPath(), "HashCheck.SelfTest");
        Directory.CreateDirectory(tempDir);

        var target = string.IsNullOrWhiteSpace(reportPath)
            ? Path.Combine(tempDir, "selftest-report.txt")
            : reportPath;

        var targetDir = Path.GetDirectoryName(target);
        if (!string.IsNullOrEmpty(targetDir))
            Directory.CreateDirectory(targetDir);

        report.AppendLine("== Hash 校验工具 自检报告 ==");
        report.AppendLine("时间   : " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
        report.AppendLine("系统   : " + Environment.OSVersion.VersionString);
        report.AppendLine("运行时 : " + Environment.Version);
        report.AppendLine("程序   : " + (Environment.ProcessPath ?? "(未知)"));
        report.AppendLine("临时区 : " + tempDir);
        report.AppendLine("SHA-3  : " + (SHA3_256.IsSupported ? "操作系统支持" : "操作系统不支持"));
        report.AppendLine();

        try
        {
            report.AppendLine("-- 1) 标准测试向量（空文件 / \"abc\"）--");
            foreach (var group in Vectors.GroupBy(v => v.Text.Length == 0 ? "空文件" : "内容 \"abc\""))
            {
                var content = group.First().Text;
                var file = Path.Combine(tempDir, content.Length == 0 ? "empty.bin" : "abc.bin");
                File.WriteAllBytes(file, Encoding.ASCII.GetBytes(content));

                var computation = await HashEngine.ComputeAsync(file);
                report.AppendLine("[" + group.Key + "] " + Path.GetFileName(file));

                foreach (var vector in group)
                {
                    var actual = computation.Result.Find(vector.Algo)?.Hex;
                    var pass = string.Equals(actual, vector.Expected, StringComparison.OrdinalIgnoreCase);
                    if (!pass)
                        failures++;

                    report.AppendLine("  " + (pass ? "PASS" : "FAIL") + " " + vector.Algo.PadRight(7) +
                                      " 期望 " + vector.Expected);
                    if (!pass)
                        report.AppendLine("        实际 " + (actual ?? "(空)"));
                }
            }
            report.AppendLine();

            report.AppendLine("-- 2) 多块路径（5120 字节，跨 BLAKE3 的 1024 字节 chunk 边界）--");
            var bigData = new byte[5120];
            for (var i = 0; i < bigData.Length; i++)
                bigData[i] = (byte)(i % 251);

            var bigFile = Path.Combine(tempDir, "big.bin");
            File.WriteAllBytes(bigFile, bigData);
            var big = await HashEngine.ComputeAsync(bigFile);

            Check(report, ref failures, "BLAKE3 流式 == 一次性",
                big.Result.Find("BLAKE3")?.Hex, Blake3.Hasher.Hash(bigData).ToString().ToLowerInvariant());
            Check(report, ref failures, "SHA-256 流式 == 一次性",
                big.Result.Find("SHA-256")?.Hex, Convert.ToHexString(SHA256.HashData(bigData)).ToLowerInvariant());
            Check(report, ref failures, "SHA-3 流式 == 一次性",
                big.Result.Find("SHA-3")?.Hex, Convert.ToHexString(SHA3_256.HashData(bigData)).ToLowerInvariant());
            Check(report, ref failures, "SHA-512 摘要长度（128 位十六进制）",
                (big.Result.Find("SHA-512")?.Hex?.Length ?? 0).ToString(), "128");
            Check(report, ref failures, "BLAKE3 摘要长度（32 字节 = 64 位十六进制）",
                (big.Result.Find("BLAKE3")?.Hex?.Length ?? 0).ToString(), "64");
            report.AppendLine();

            report.AppendLine("-- 3) 显示格式与导出命名 --");
            var lines = big.Result.ToDisplayText().Split(new[] { "\r\n" }, StringSplitOptions.None);
            Check(report, ref failures, "行数（文件名 + 6 个算法）", lines.Length.ToString(), "7");
            Check(report, ref failures, "第 1 行", lines.Length > 0 ? lines[0] : null, "文件名称：big.bin");
            Check(report, ref failures, "第 2 行算法名", LineLabel(lines, 1), "MD5");
            Check(report, ref failures, "第 3 行算法名", LineLabel(lines, 2), "SHA-1");
            Check(report, ref failures, "第 7 行算法名", LineLabel(lines, 6), "BLAKE3");
            Check(report, ref failures, "导出文件名（单扩展名）",
                ExportService.BuildTargetPath(@"C:\tmp\setup.exe"), @"C:\tmp\setup_Hash校验结果.txt");
            Check(report, ref failures, "导出文件名（多扩展名）",
                ExportService.BuildTargetPath(@"C:\tmp\a.tar.gz"), @"C:\tmp\a.tar_Hash校验结果.txt");
            report.AppendLine();
        }
        catch (Exception ex)
        {
            failures++;
            report.AppendLine("!! 自检过程中抛出异常：");
            report.AppendLine(ex.ToString());
            report.AppendLine();
        }

        report.AppendLine(failures == 0 ? "== 全部通过 ==" : "== 失败 " + failures + " 项 ==");

        var text = report.ToString();

        // 先落盘：报告才是产出；控制台输出是尽力而为（GUI 程序可能没有控制台）
        File.WriteAllText(target, text, new UTF8Encoding(true));

        try
        {
            Console.WriteLine(text);
            Console.WriteLine("报告文件：" + target);
        }
        catch
        {
            // 无控制台时忽略
        }

        return failures == 0 ? 0 : 1;
    }

    private static string? LineLabel(string[] lines, int index)
    {
        if (index < 0 || index >= lines.Length)
            return null;

        var parts = lines[index].Split('：');
        return parts.Length > 0 ? parts[0] : null;
    }

    private static void Check(StringBuilder report, ref int failures, string title, string? actual, string expected)
    {
        var pass = string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        if (!pass)
            failures++;

        report.AppendLine((pass ? "PASS" : "FAIL") + " " + title);
        report.AppendLine("     期望 " + expected);
        if (!pass)
            report.AppendLine("     实际 " + (actual ?? "(空)"));
    }
}
