using Azure;
using Azure.Communication.Email;

namespace Vela.Api.Common;

public sealed class AcsEmailOptions
{
    public const string ConnectionStringSetting = "Email:Acs:ConnectionString";
    public const string SenderAddressSetting = "Email:SenderAddress";

    public string ConnectionString { get; init; } = "";
    public string SenderAddress { get; init; } = "";

    /// <summary>Fails fast at startup when a required setting is missing.</summary>
    public static AcsEmailOptions Load(IConfiguration config)
    {
        var conn = config[ConnectionStringSetting];
        var sender = config[SenderAddressSetting];
        if (string.IsNullOrWhiteSpace(conn))
            throw new InvalidOperationException($"{ConnectionStringSetting} is not configured. Set it via environment (Email__Acs__ConnectionString) or a secret store.");
        if (string.IsNullOrWhiteSpace(sender))
            throw new InvalidOperationException($"{SenderAddressSetting} is not configured. Set it to an address on the verified ACS sending domain.");
        return new AcsEmailOptions { ConnectionString = conn, SenderAddress = sender };
    }
}

/// <summary>Sends plain-text mail through Azure Communication Services using the configured sending domain.</summary>
public sealed class AcsEmailSender(AcsEmailOptions options) : IEmailSender
{
    private readonly EmailClient _client = new(options.ConnectionString);

    public async Task SendAsync(string to, string subject, string textBody)
    {
        var message = new EmailMessage(
            options.SenderAddress,
            new EmailRecipients([new EmailAddress(to)]),
            new EmailContent(subject) { PlainText = textBody });
        await _client.SendAsync(WaitUntil.Started, message);
    }
}
