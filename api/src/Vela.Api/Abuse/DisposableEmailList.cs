namespace Vela.Api.Abuse;

/// <summary>Throwaway-mailbox domains refused at email sign-in. A small embedded list plus Abuse:DisposableEmailDomains.</summary>
public sealed class DisposableEmailList
{
    private static readonly string[] Embedded =
    [
        "mailinator.com", "guerrillamail.com", "guerrillamail.net", "guerrillamail.org", "guerrillamail.biz", "guerrillamail.de",
        "sharklasers.com", "grr.la", "10minutemail.com", "10minutemail.net", "20minutemail.com", "tempmail.com", "temp-mail.org",
        "temp-mail.io", "tempmail.net", "tempmailo.com", "tempinbox.com", "throwawaymail.com", "trashmail.com", "trashmail.net",
        "trashmail.de", "yopmail.com", "yopmail.fr", "yopmail.net", "getnada.com", "nada.email", "dispostable.com", "maildrop.cc",
        "mailnesia.com", "mailcatch.com", "mintemail.com", "mohmal.com", "emailondeck.com", "fakeinbox.com", "fakemail.net",
        "spamgourmet.com", "spambox.us", "mytemp.email", "burnermail.io", "discard.email", "discardmail.com", "getairmail.com",
        "harakirimail.com", "inboxbear.com", "mailforspam.com", "mailtemp.info", "moakt.com", "mail.tm", "mailsac.com",
        "owlymail.com", "tmail.ws", "tmpmail.org", "tmpmail.net", "dropmail.me", "emltmp.com", "fexpost.com", "linshiyouxiang.net",
        "spam4.me", "tempr.email", "temporary-mail.net", "trbvm.com", "wegwerfmail.de", "zetmail.com", "33mail.com", "anonbox.net",
    ];

    private readonly HashSet<string> _domains;

    public DisposableEmailList(IEnumerable<string> additions) =>
        _domains = new HashSet<string>(Embedded.Concat(additions.Select(Normalise).Where(d => d.Length > 0)), StringComparer.Ordinal);

    /// <summary>True when the address's domain, or any parent domain of it, is listed.</summary>
    public bool IsDisposable(string email)
    {
        var at = email.LastIndexOf('@');
        if (at < 0) return false;
        var domain = Normalise(email[(at + 1)..]);
        while (domain.Length > 0)
        {
            if (_domains.Contains(domain)) return true;
            var dot = domain.IndexOf('.');
            if (dot < 0) return false;
            domain = domain[(dot + 1)..];
        }
        return false;
    }

    private static string Normalise(string d) => d.Trim().TrimStart('@').ToLowerInvariant();
}
