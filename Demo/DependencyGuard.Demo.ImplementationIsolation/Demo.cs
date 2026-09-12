namespace External
{
    internal interface INotificationSender { Task SendAsync(string recipient, string message); }
    internal sealed record NotificationRequest(string Recipient, string Message);
}

namespace Internal.Email
{
    using External;     // Allowed: Internal.* → External.
    using Internal.Sms; // DG0001: no rule allows Internal.Email → Internal.Sms.

    internal sealed class EmailSender : INotificationSender
    {
        public Task SendAsync(string recipient, string message)
        {
            Console.WriteLine($"[Email] To: {recipient} | {message}");
            return Task.CompletedTask;
        }
    }

    internal sealed class EmailFallback
    {
        private readonly SmsSender _sms = new();

        public Task FallbackAsync(string recipient, string message)
        {
            return _sms.SendAsync(recipient, message);
        }
    }
}

namespace Internal.Sms
{
    using External; // Allowed: Internal.* → External.

    internal sealed class SmsSender : INotificationSender
    {
        public Task SendAsync(string recipient, string message)
        {
            Console.WriteLine($"[SMS] To: {recipient} | {message}");
            return Task.CompletedTask;
        }
    }
}

namespace Dispatching
{
    using External; // Allowed: Dispatching → External.

    internal sealed class NotificationDispatcher(INotificationSender sender)
    {
        public Task DispatchAsync(NotificationRequest request)
        {
            return sender.SendAsync(request.Recipient, request.Message);
        }
    }
}
