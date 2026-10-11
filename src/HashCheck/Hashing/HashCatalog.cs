using System.Security.Cryptography;

namespace HashCheck.Hashing;

/// <summary>可增量喂入数据的哈希计算步骤。</summary>
public interface IHashStep : IDisposable
{
    void Append(byte[] buffer, int offset, int count);

    /// <summary>结束计算并返回小写十六进制摘要。</summary>
    string FinishHex();
}

/// <summary>界面上展示的一种算法定义。</summary>
/// <param name="Label">显示名称，同时作为导出文本里的键（如 "SHA-256"）。</param>
/// <param name="Factory">创建计算实例。</param>
/// <param name="CryptographicallySecure">是否仍被认为密码学安全（用于界面提示）。</param>
public sealed record HashAlgorithmSpec(string Label, Func<IHashStep> Factory, bool CryptographicallySecure);

/// <summary>基于 IncrementalHash 的实现：MD5 / SHA-1 / SHA-256 / SHA-512，以及可用时的 SHA-3。</summary>
internal sealed class IncrementalHashStep : IHashStep
{
    private readonly IncrementalHash _hash;

    public IncrementalHashStep(HashAlgorithmName name) => _hash = IncrementalHash.CreateHash(name);

    public void Append(byte[] buffer, int offset, int count) => _hash.AppendData(buffer, offset, count);

    public string FinishHex() => Convert.ToHexString(_hash.GetHashAndReset()).ToLowerInvariant();

    public void Dispose() => _hash.Dispose();
}

/// <summary>基于 HashAlgorithm.TransformBlock 的实现，作为 SHA-3 的兜底路径。</summary>
internal sealed class TransformHashStep : IHashStep
{
    private static readonly byte[] Empty = [];

    private readonly HashAlgorithm _hash;

    public TransformHashStep(HashAlgorithm hash) => _hash = hash;

    public void Append(byte[] buffer, int offset, int count) => _hash.TransformBlock(buffer, offset, count, null, 0);

    public string FinishHex()
    {
        _hash.TransformFinalBlock(Empty, 0, 0);
        return Convert.ToHexString(_hash.Hash ?? []).ToLowerInvariant();
    }

    public void Dispose() => _hash.Dispose();
}

/// <summary>算法清单。顺序即界面与导出文本中的顺序。</summary>
public static class HashCatalog
{
    public const string Sha3Label = "SHA-3";
    public const string Blake3Label = "BLAKE3";

    public static readonly IReadOnlyList<HashAlgorithmSpec> All =
    [
        new("MD5",       () => new IncrementalHashStep(HashAlgorithmName.MD5),    false),
        new("SHA-1",     () => new IncrementalHashStep(HashAlgorithmName.SHA1),   false),
        new("SHA-256",   () => new IncrementalHashStep(HashAlgorithmName.SHA256), true),
        new("SHA-512",   () => new IncrementalHashStep(HashAlgorithmName.SHA512), true),
        new(Sha3Label,   CreateSha3_256,                                          true),
        new(Blake3Label, () => new Blake3Step(),                                  true),
    ];

    /// <summary>
    /// SHA-3 采用 SHA3-256（FIPS 202；不是 Keccak-256，两者结果完全不同）。
    /// .NET 8+ 内置该算法，但依赖操作系统/底层加密库支持，所以做两级尝试：
    /// 优先 IncrementalHash（可复用“单次读取算全部算法”的管道），失败再退回 SHA3_256.Create()。
    /// </summary>
    private static IHashStep CreateSha3_256()
    {
        try
        {
            // IncrementalHashStep 内部会调用 IncrementalHash.CreateHash(name)，
            // 若当前系统不支持 SHA3-256，这里会抛异常，由 catch 转入下面的兜底路径
            return new IncrementalHashStep(new HashAlgorithmName("SHA3-256"));
        }
        catch (Exception ex) when (ex is CryptographicException or PlatformNotSupportedException or ArgumentException or NotSupportedException)
        {
            // 忽略，继续尝试下一条路径
        }

        if (!SHA3_256.IsSupported)
            throw new PlatformNotSupportedException("当前操作系统/加密库不支持 SHA-3");

        return new TransformHashStep(SHA3_256.Create());
    }
}