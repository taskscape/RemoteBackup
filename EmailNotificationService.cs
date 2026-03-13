using MailKit.Net.Smtp;
using Microsoft.Extensions.Options;
using MimeKit;

namespace BackupService;

public class EmailNotificationService(
    ILogger<EmailNotificationService> logger,
    IOptions<BackupOptions> options)
{
    private readonly BackupOptions _options = options.Value;

    public async Task SendFailureNotificationAsync(BackupJobOptions job, string reason, Exception? ex = null)
    {
        if (!job.NotifyOnFailure) return;

        var recipients = _options.NotifyEmails.Concat(job.NotifyEmails).Distinct().ToList();
        if (recipients.Count == 0)
        {
            logger.LogWarning("No recipients configured for failure notification of job '{name}'.", job.Name);
            return;
        }

        if (string.IsNullOrEmpty(_options.Smtp.Host))
        {
            logger.LogWarning("SMTP Host is not configured. Cannot send failure notification for job '{name}'.", job.Name);
            return;
        }

        try
        {
            var message = new MimeMessage();
            message.From.Add(new MailboxAddress(_options.Smtp.FromName, _options.Smtp.FromEmail));
            foreach (var email in recipients)
            {
                message.To.Add(new MailboxAddress("", email));
            }

            message.Subject = $"[FAILURE] Backup Job: {job.Name}";

            var bodyBuilder = new BodyBuilder
            {
                TextBody = $"Backup job '{job.Name}' failed.\n\n" +
                           $"Reason: {reason}\n" +
                           (ex != null ? $"Exception: {ex.Message}\n" : "") +
                           $"Time: {DateTime.Now}\n\n" +
                           $"Please check the service logs for more details."
            };

            message.Body = bodyBuilder.ToMessageBody();

            using var client = new SmtpClient();
            // In some environments, we might need to skip certificate validation
            client.ServerCertificateValidationCallback = (s, c, h, e) => true;

            await client.ConnectAsync(_options.Smtp.Host, _options.Smtp.Port, _options.Smtp.EnableSsl);
            
            if (!string.IsNullOrEmpty(_options.Smtp.Username))
            {
                await client.AuthenticateAsync(_options.Smtp.Username, _options.Smtp.Password);
            }

            await client.SendAsync(message);
            await client.DisconnectAsync(true);

            logger.LogInformation("Failure notification email sent for job '{name}' to {count} recipients.", job.Name, recipients.Count);
        }
        catch (Exception mailEx)
        {
            logger.LogError(mailEx, "Failed to send failure notification email for job '{name}'.", job.Name);
        }
    }
}
