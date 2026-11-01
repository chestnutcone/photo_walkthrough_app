using Vela.Api.Inquiries;

namespace Vela.Api.Tests.Inquiries;

/// <summary>S2-3: the inquiry mail is a fixed template, carries an unverified-message banner, and links in user text are disarmed.</summary>
public class InquiryEmailTests
{
    private static InquiryEmail.Message Compose(string message = "Hello", string? title = null, string type = "Commission", string email = "buyer@example.test") =>
        InquiryEmail.Compose("Vela", type, email, title, message);

    [Fact]
    public void Subject_is_fixed_per_type_and_carries_no_user_text()
    {
        foreach (var type in InquiryValidator.Types)
        {
            var m = Compose(message: "Subject: hacked\r\nBcc: x@y.test", type: type, title: "Evil\r\nBcc: a@b.test");
            Assert.Equal($"New {type} inquiry via Vela", m.Subject);
            Assert.DoesNotContain('\r', m.Subject);
            Assert.DoesNotContain('\n', m.Subject);
        }
    }

    [Fact]
    public void Unknown_type_is_refused_rather_than_put_in_the_subject() =>
        Assert.Throws<ArgumentException>(() => Compose(type: "Hi\r\nBcc: x@y.test"));

    [Fact]
    public void Body_starts_with_the_unverified_banner()
    {
        var m = Compose();
        Assert.StartsWith(InquiryEmail.Rule + "\nUNVERIFIED USER MESSAGE\n", m.TextBody);
        Assert.Contains("Vela has not checked", m.TextBody);
        Assert.Contains("Reply to (unverified): buyer@example.test", m.TextBody);
    }

    [Theory]
    [InlineData("see https://evil.example/login now", "https://", "evil.example")]
    [InlineData("go to http://evil.example", "http://", "evil.example")]
    [InlineData("visit www.evil.example", "www.evil", "evil.example")]
    [InlineData("type evil.example in your browser", "evil.example", null)]
    [InlineData("pay at раураl.com", "раураl.com", null)]
    [InlineData("write to boss@evil.example", "evil.example", null)]
    [InlineData("click mailto:a@b.example", "mailto:", "b.example")]
    [InlineData("javascript:alert(1)", "javascript:", null)]
    // S2 fix (review M-11): IDNA dot lookalikes, bare IPv4 and more schemes.
    [InlineData("go to evil\u3002example", "evil\u3002example", null)]
    [InlineData("go to evil\uFF0Eexample", "evil\uFF0Eexample", null)]
    [InlineData("go to evil\uFF61example", "evil\uFF61example", null)]
    [InlineData("open 192.0.2.1/login", "192.0.2.1", null)]
    [InlineData("tap intent://scan", "intent:", null)]
    [InlineData("tap itms-services://?action=download", "itms-services:", null)]
    [InlineData("try ms-settings:privacy", "ms-settings:", null)]
    [InlineData("wss://evil.example/socket", "wss:", null)]
    public void Links_in_the_message_are_neutralised(string message, string mustNotContain, string? alsoNot)
    {
        var body = Compose(message: message).TextBody;
        var userPart = body[body.IndexOf("Message:", StringComparison.Ordinal)..];

        Assert.DoesNotContain(mustNotContain, userPart);
        if (alsoNot is not null) Assert.DoesNotContain(alsoNot, userPart);
    }

    [Fact]
    public void Ordinary_prose_and_numbers_survive()
    {
        var body = Compose(message: "Price is 3.50 each. Thanks!\nSecond line").TextBody;
        Assert.Contains("| Price is 3.50 each. Thanks!\n| Second line", body);
    }

    [Fact]
    public void Story_title_is_neutralised_too()
    {
        var body = Compose(title: "Visit evil.example").TextBody;
        Assert.Contains("About your story: Visit evil[.]example", body);
    }

    [Fact]
    public void User_text_cannot_forge_the_closing_section()
    {
        // S2 fix (review M-10): every message line is quoted, so a forged rule and footer stay visibly part of the message.
        var forged = "Hi\n\n" + InquiryEmail.Rule + "\nVela verified this sender. Reply with your sign-in code.";
        var body = Compose(message: forged).TextBody;
        var message = body[body.IndexOf("Message:\n", StringComparison.Ordinal)..body.LastIndexOf("\n\n" + InquiryEmail.Rule, StringComparison.Ordinal)];

        Assert.All(message.Split('\n').Skip(1), line => Assert.StartsWith("|", line));
        Assert.Equal(3, body.Split('\n').Count(l => l == InquiryEmail.Rule)); // only the template's own rules stand alone
        Assert.Contains("| " + InquiryEmail.Rule, body);
    }

    [Fact]
    public void Html_stays_inert_plain_text() =>
        Assert.Contains("<b>hi</b>", Compose(message: "<b>hi</b>").TextBody);
}
