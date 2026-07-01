using System.Security.Cryptography;

namespace ContextMessenger.Patching;

public static class ContentHash
{
    public static string ForFile(string path)
    {
        using var stream = File.OpenRead(path);
        return ForStream(stream);
    }

    public static string ForBytes(byte[] bytes) =>
        "sha256:" + Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static string ForStream(Stream stream)
    {
        var hash = SHA256.HashData(stream);
        return "sha256:" + Convert.ToHexString(hash).ToLowerInvariant();
    }
}
