using System.Buffers;
using System.Diagnostics;

namespace HashCheck.Hashing;

/// <summary>进度信息（界面进度条 + 速度显示）。</summary>
public sealed record HashProgress(long BytesDone, long TotalBytes, double BytesPerSecond)
{
    public double Percent => TotalBytes <= 0 ? 100d : Math.Min(100d, BytesDone * 100d / TotalBytes);
}

/// <summary>计算结果，以及“文件在计算过程中是否被改动过”的标记。</summary>
public sealed record HashComputation(HashResult Result, bool FileChangedDuringHashing);

public static class HashEngine
{
    public const int BufferSize = 1024 * 1024;   // 1 MiB
    private const int ProgressIntervalMs = 60;

    /// <summary>
    /// 一次读取、同时算出全部算法：文件只从磁盘读一遍，而不是为 6 个算法读 6 遍。
    /// 用 FileShare.ReadWrite | Delete 打开，因此被其他程序占用的文件也能算。
    /// </summary>
    public static async Task<HashComputation> ComputeAsync(
        string path,
        IProgress<HashProgress>? progress = null,
        CancellationToken cancellationToken = default)
    {
        var fullPath = Path.GetFullPath(path);
        var before = new FileInfo(fullPath);
        if (!before.Exists)
            throw new FileNotFoundException($"文件不存在：{fullPath}", fullPath);

        var totalBytes = before.Length;
        var specs = HashCatalog.All;
        var steps = new IHashStep?[specs.Count];
        var reasons = new string?[specs.Count];

        for (var i = 0; i < specs.Count; i++)
        {
            try
            {
                steps[i] = specs[i].Factory();
            }
            catch (Exception ex)
            {
                // 例如某些机器上 FIPS 策略禁用了 MD5，或系统不支持 SHA-3
                reasons[i] = ex.Message;
            }
        }

        var buffer = ArrayPool<byte>.Shared.Rent(BufferSize);
        var stopwatch = Stopwatch.StartNew();
        long done = 0;
        long lastReportAt = 0;

        try
        {
            await using var stream = new FileStream(
                fullPath, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                BufferSize, FileOptions.Asynchronous | FileOptions.SequentialScan);

            int read;
            while ((read = await stream.ReadAsync(buffer.AsMemory(0, BufferSize), cancellationToken).ConfigureAwait(false)) > 0)
            {
                foreach (var step in steps)
                    step?.Append(buffer, 0, read);

                done += read;

                if (progress is not null &&
                    (stopwatch.ElapsedMilliseconds - lastReportAt >= ProgressIntervalMs || done >= totalBytes))
                {
                    lastReportAt = stopwatch.ElapsedMilliseconds;
                    progress.Report(new HashProgress(done, totalBytes, ComputeSpeed(done, stopwatch)));
                }
            }
        }
        catch
        {
            foreach (var step in steps)
                step?.Dispose();
            throw;
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }

        var values = new List<HashValue>(specs.Count);
        for (var i = 0; i < specs.Count; i++)
        {
            var step = steps[i];
            try
            {
                values.Add(step is null
                    ? new HashValue(specs[i].Label, null, reasons[i])
                    : new HashValue(specs[i].Label, step.FinishHex(), null));
            }
            catch (Exception ex)
            {
                values.Add(new HashValue(specs[i].Label, null, ex.Message));
            }
            finally
            {
                step?.Dispose();
            }
        }

        progress?.Report(new HashProgress(done, totalBytes, ComputeSpeed(done, stopwatch)));

        // 计算期间文件被改动过的话，结果可能对应一个“混合状态”，需要提示用户
        var after = new FileInfo(fullPath);
        var changed = after.Exists &&
                      (after.Length != before.Length || after.LastWriteTimeUtc != before.LastWriteTimeUtc);

        return new HashComputation(
            new HashResult(before.Name, fullPath, totalBytes, before.LastWriteTimeUtc, values),
            changed);
    }

    private static double ComputeSpeed(long done, Stopwatch stopwatch) =>
        done / Math.Max(0.001, stopwatch.Elapsed.TotalSeconds);
}