using System.Collections.Concurrent;

namespace Vela.Api.Common;

public sealed record SentEmail(string To, string Subject, string TextBody);

/// <summary>In-memory outbox. Registered only in the Test environment.</summary>
public sealed class FakeEmailSender : IEmailSender
{
    private readonly ConcurrentQueue<SentEmail> _sent = new();

    public IReadOnlyList<SentEmail> Sent => [.. _sent];

    public Task SendAsync(string to, string subject, string textBody)
    {
        _sent.Enqueue(new SentEmail(to, subject, textBody));
        return Task.CompletedTask;
    }
}
