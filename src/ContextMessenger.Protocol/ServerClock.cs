using System.Globalization;

namespace ContextMessenger.Protocol;

internal static class ServerClock
{
    public static string NowIso8601Utc() =>
        DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
}
