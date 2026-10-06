using System.IO;
using System.Security.Cryptography;

namespace DuoLaBox.Services;

public sealed record StagedFile(string Path, string Sha256)
{
    public static StagedFile Create(string path) =>
        new(path, ComputeHash(path));

    public byte[] ReadVerifiedBytes()
    {
        byte[] expectedHash;
        try
        {
            expectedHash = Convert.FromHexString(Sha256);
        }
        catch (FormatException exception)
        {
            throw new InvalidOperationException("临时任务哈希格式无效，已拒绝执行。", exception);
        }

        var bytes = File.ReadAllBytes(Path);
        if (!CryptographicOperations.FixedTimeEquals(expectedHash, SHA256.HashData(bytes)))
        {
            throw new InvalidOperationException("临时任务完整性校验失败，已拒绝执行。");
        }

        return bytes;
    }

    public bool HasExpectedHash()
    {
        try
        {
            return CryptographicOperations.FixedTimeEquals(
                Convert.FromHexString(Sha256),
                Convert.FromHexString(ComputeHash(Path)));
        }
        catch
        {
            return false;
        }
    }

    private static string ComputeHash(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream));
    }
}
