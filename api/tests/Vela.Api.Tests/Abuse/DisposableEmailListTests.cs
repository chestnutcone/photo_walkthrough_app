using Vela.Api.Abuse;

namespace Vela.Api.Tests.Abuse;

public class DisposableEmailListTests
{
    [Theory]
    [InlineData("a@mailinator.com")]
    [InlineData("a@MAILINATOR.COM")]
    [InlineData("a@inbox.mailinator.com")]
    [InlineData("a@guerrillamail.com")]
    [InlineData("a@10minutemail.com")]
    public void Embedded_domains_and_their_subdomains_are_disposable(string email) =>
        Assert.True(new DisposableEmailList([]).IsDisposable(email));

    [Theory]
    [InlineData("a@gmail.com")]
    [InlineData("a@example.test")]
    [InlineData("a@notmailinator.com")]
    [InlineData("a@mailinator.com.example.test")]
    [InlineData("no-at-sign")]
    [InlineData("")]
    public void Ordinary_domains_are_not(string email) =>
        Assert.False(new DisposableEmailList([]).IsDisposable(email));

    [Fact]
    public void Configured_additions_are_honoured_and_normalised()
    {
        var list = new DisposableEmailList(["  Burner.Example ", "", "@trash.example"]);
        Assert.True(list.IsDisposable("x@burner.example"));
        Assert.True(list.IsDisposable("x@sub.trash.example"));
        Assert.False(list.IsDisposable("x@burner.example.org"));
    }
}
