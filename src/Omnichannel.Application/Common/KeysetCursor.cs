using System.Globalization;
using System.Text;

namespace Omnichannel.Application.Common;

/// <summary>
/// Opaque cursor for keyset pagination on a (timestamp, id) tie-break — used for the
/// conversation list (LastMessageAt) and message history (CreatedAt) per PRD §47's guidance to
/// paginate rather than load full histories, without offset-pagination's page-drift problem on
/// a frequently-changing sort key.
/// </summary>
public static class KeysetCursor
{
    public static string Encode(DateTimeOffset timestamp, Guid id)
    {
        var raw = $"{timestamp.UtcTicks}_{id}";
        return Convert.ToBase64String(Encoding.UTF8.GetBytes(raw))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }

    public static (DateTimeOffset Timestamp, Guid Id)? Decode(string? cursor)
    {
        if (string.IsNullOrWhiteSpace(cursor))
        {
            return null;
        }

        try
        {
            var padded = cursor.Replace('-', '+').Replace('_', '/');
            padded += new string('=', (4 - (padded.Length % 4)) % 4);
            var raw = Encoding.UTF8.GetString(Convert.FromBase64String(padded));
            var parts = raw.Split('_', 2);
            var ticks = long.Parse(parts[0], CultureInfo.InvariantCulture);
            var id = Guid.Parse(parts[1]);
            return (new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }
        catch (Exception ex) when (ex is FormatException or IndexOutOfRangeException or OverflowException)
        {
            return null;
        }
    }
}
