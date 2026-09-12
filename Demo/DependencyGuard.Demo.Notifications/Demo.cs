namespace Notifications
{
    public interface INotificationSender { Task SendAsync(NotificationRequest request); }
    public sealed record NotificationRequest(string Recipient, string Subject, string Body);
}

namespace Email
{
    using Notifications; // Allowed: Email → Notifications.
    using Sms;           // DG0001: no rule allows Email → Sms.

    public sealed record EmailOptions(string SmtpHost, int SmtpPort);

    public sealed class EmailSender(EmailOptions options) : INotificationSender
    {
        public Task SendAsync(NotificationRequest request)
        {
            return Task.CompletedTask;
        }
    }

    public sealed class ProviderDispatcher
    {
        private readonly SmsSender _fallback = new(new SmsOptions(""));

        public Task DispatchAsync(NotificationRequest request)
        {
            return _fallback.SendAsync(request);
        }
    }
}

namespace Sms
{
    using Notifications; // Allowed: Sms → Notifications.

    public sealed record SmsOptions(string ApiKey);

    public sealed class SmsSender(SmsOptions options) : INotificationSender
    {
        public Task SendAsync(NotificationRequest request)
        {
            return Task.CompletedTask;
        }
    }
}
