namespace MapsWPF.Services
{
    public interface IClipboardService
    {
        bool TrySetText(string text);
    }
}