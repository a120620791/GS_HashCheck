using Blake3;

namespace HashCheck.Hashing;

/// <summary>
/// BLAKE3（NuGet 包 Blake3，xoofx/Blake3.NET 的全托管 SIMD 实现）。
///
/// 注意：BLAKE3 是 XOF（可扩展输出），输出长度不固定，不同长度得到完全不同的值。
/// 这里固定使用该库的默认 32 字节输出（64 位十六进制），与官方 b3sum 等工具的默认结果一致。
/// </summary>
internal sealed class Blake3Step : IHashStep
{
    private readonly Hasher _hasher = Hasher.New();

    public void Append(byte[] buffer, int offset, int count) => _hasher.Update(buffer.AsSpan(offset, count));

    public string FinishHex() => _hasher.Finalize().ToString().ToLowerInvariant();

    public void Dispose()
    {
        // Hasher 为纯托管对象，无需释放
    }
}