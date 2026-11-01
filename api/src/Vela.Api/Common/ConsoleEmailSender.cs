using System.Text.RegularExpressions;

namespace Vela.Api.Common;

/// <summary>Development only: prints the one-time code so a developer can sign in. Never prints the address.</summary>
public sealed partial class ConsoleEmailSender(TextWriter output) : IEmailSender
{
    public ConsoleEmailSender() : this(Console.Out) { }

    public Task SendAsync(string to, string subject, string textBody)
    {
        var code = CodePattern().Match(textBody).Value;
        output.WriteLine($"[dev email] sign-in code: {code}");
        return Task.CompletedTask;
    }

    [GeneratedRegex(@"\b\d{6}\b")]
    private static partial Regex CodePattern();
}
