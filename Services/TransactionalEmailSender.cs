using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;
using TaskApi.Configuration;

namespace TaskApi.Services;

public interface ITransactionalEmailSender
{
    Task SendAsync(TransactionalEmail email, CancellationToken cancellationToken);
}

public sealed class SmtpTransactionalEmailSender(IOptions<EmailOptions> options) : ITransactionalEmailSender
{
    public async Task SendAsync(TransactionalEmail email, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(settings.FromName, settings.FromAddress));
        message.To.Add(MailboxAddress.Parse(email.Address));
        message.Subject = email.Subject;
        message.Body = new TextPart("plain") { Text = email.Text };
        using var client = new SmtpClient { Timeout = 10_000 };
        var security = Enum.Parse<SecureSocketOptions>(settings.Security);
        await client.ConnectAsync(settings.Host, settings.Port, security, cancellationToken);
        if (!string.IsNullOrWhiteSpace(settings.Username))
            await client.AuthenticateAsync(settings.Username, settings.Password, cancellationToken);
        await client.SendAsync(message, cancellationToken);
        await client.DisconnectAsync(true, cancellationToken);
    }
}
