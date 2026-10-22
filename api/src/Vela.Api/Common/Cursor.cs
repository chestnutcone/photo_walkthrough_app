using System.Buffers.Text;
using System.Globalization;
using System.Text;

namespace Vela.Api.Common;

/// <summary>A cursor that is not valid base64url, has the wrong shape, or carries impossible values.</summary>
public sealed class InvalidCursorException() : Exception("The cursor is not valid.");

/// <summary>Opaque keyset cursor: URL-safe base64 of <c>segment|utcTicks|id</c>. Anything else is a 400 invalid_cursor.</summary>
public static class Cursor
{
    public readonly record struct Position(int Segment, DateTimeOffset At, Guid Id);

    public static string Encode(DateTimeOffset at, Guid id) => Encode(0, at, id);

    public static string Encode(int segment, DateTimeOffset at, Guid id)
    {
        var text = string.Create(CultureInfo.InvariantCulture, $"{segment}|{at.UtcTicks}|{id:N}");
        return Base64Url.EncodeToString(Encoding.UTF8.GetBytes(text));
    }

    public static (DateTimeOffset At, Guid Id) Decode(string cursor)
    {
        var p = DecodeSegmented(cursor);
        return (p.At, p.Id);
    }

    public static Position DecodeSegmented(string cursor)
    {
        try
        {
            var parts = Encoding.UTF8.GetString(Base64Url.DecodeFromChars(cursor)).Split('|');
            if (parts.Length == 3
                && int.TryParse(parts[0], NumberStyles.None, CultureInfo.InvariantCulture, out var segment) && segment is 0 or 1
                && long.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
                && ticks <= DateTime.MaxValue.Ticks
                && Guid.TryParseExact(parts[2], "N", out var id))
                return new Position(segment, new DateTimeOffset(ticks, TimeSpan.Zero), id);
        }
        catch (Exception e) when (e is FormatException or ArgumentException or InvalidOperationException) { }
        throw new InvalidCursorException();
    }
}
