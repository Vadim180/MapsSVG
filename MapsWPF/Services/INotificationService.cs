using System;

namespace MapsWPF.Services
{
    public enum NotificationType { Info, Warning, Error }

    public interface INotificationService
    {
        event Action<string, NotificationType>? NotificationRaised;
        void Notify(string message, NotificationType type = NotificationType.Info);
    }
}