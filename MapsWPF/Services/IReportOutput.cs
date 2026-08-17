namespace MapsWPF.Services
{
    public interface IReportOutput
    {
        void SetReportText(string text);
        void CopyToClipboardWithNotification(string text);
        void SetAzimuthDisplay(string text);
    }
}