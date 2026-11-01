namespace Vela.Api.Common;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string textBody);
}
