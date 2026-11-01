using System.Security.Cryptography;

namespace Linework.Core.Library;

public static class FileHash
{
    /// <summary>Reads in pieces, so a very large file isn't held in memory.</summary>
    public static string Sha256(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 1 << 20);
        return Convert.ToHexStringLower(SHA256.HashData(stream));
    }
}
