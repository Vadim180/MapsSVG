using System;

namespace MapsWPF.Services
{
    public class NotificationService : INotificationService
    {
        public event Action<string, NotificationType>? NotificationRaised;

        public void Notify(string message, NotificationType type = NotificationType.Info)
        {
            try
            {
                NotificationRaised?.Invoke(message, type);
            }
            catch { }
        }
    }
}