using System;

namespace MapsWPF.Services
{
    public enum NotificationType { Info, Warning, Error }

    public class NotificationService
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