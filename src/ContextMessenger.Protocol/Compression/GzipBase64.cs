using System.IO.Compression;
using System.Text;

namespace ContextMessenger.Protocol.Compression;

public static class GzipBase64
{
    public static string Encode(string value)
    {
        var bytes = Encoding.UTF8.GetBytes(value);
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Fastest, leaveOpen: true))
            gzip.Write(bytes, 0, bytes.Length);

        return Convert.ToBase64String(output.ToArray());
    }

    public static string Decode(string payload, string valueDescription = "Response envelope payload")
    {
        try
        {
            var compressed = Convert.FromBase64String(payload);
            using var input = new MemoryStream(compressed);
            using var gzip = new GZipStream(input, CompressionMode.Decompress);
            using var output = new MemoryStream();
            gzip.CopyTo(output);
            return Encoding.UTF8.GetString(output.ToArray());
        }
        catch (Exception ex) when (ex is FormatException or InvalidDataException or IOException)
        {
            throw new ProtocolException(
                ProtocolErrorCodes.InvalidParameters,
                $"{valueDescription} is not valid gzip+base64: {ex.Message}",
                ex);
        }
    }
}
