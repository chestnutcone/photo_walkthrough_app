using System.Text;
using Vela.Api.Common;

namespace Vela.Api.Tests.Social;

public class CursorTests
{
    private static readonly DateTimeOffset At = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Two_argument_form_round_trips()
    {
        var id = Guid.NewGuid();
        var (at, gotId) = Cursor.Decode(Cursor.Encode(At.AddTicks(7), id));
        Assert.Equal(At.AddTicks(7), at);
        Assert.Equal(id, gotId);
    }

    [Fact]
    public void Segmented_form_round_trips_and_is_url_safe()
    {
        var id = Guid.NewGuid();
        var text = Cursor.Encode(1, At, id);
        var c = Cursor.DecodeSegmented(text);
        Assert.Equal((1, At, id), (c.Segment, c.At, c.Id));
        Assert.DoesNotContain('+', text);
        Assert.DoesNotContain('/', text);
        Assert.DoesNotContain('=', text);
    }

    [Theory]
    [InlineData("")]
    [InlineData("!!!")]
    [InlineData("YWJj")]
    public void Malformed_cursor_is_rejected(string bad) =>
        Assert.Throws<InvalidCursorException>(() => Cursor.Decode(bad));

    [Fact]
    public void Tampered_cursor_is_rejected()
    {
        var good = Cursor.Encode(At, Guid.NewGuid());
        var raw = Convert.FromBase64String(good.Replace('-', '+').Replace('_', '/') + new string('=', (4 - good.Length % 4) % 4));
        var text = Encoding.UTF8.GetString(raw).Replace("|", "|x");
        var tampered = Convert.ToBase64String(Encoding.UTF8.GetBytes(text)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        Assert.Throws<InvalidCursorException>(() => Cursor.Decode(tampered));
    }

    [Fact]
    public void Segment_outside_zero_or_one_is_rejected()
    {
        var text = Cursor.Encode(5, At, Guid.NewGuid());
        Assert.Throws<InvalidCursorException>(() => Cursor.DecodeSegmented(text));
    }
}
