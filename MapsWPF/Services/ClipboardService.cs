using System;
using System.Windows;
using System.Windows.Threading;

namespace MapsWPF.Services
{
    public class ClipboardService
    {
        public bool TrySetText(string text)
        {
            try
            {
                // Ensure running on UI thread
                if (Application.Current != null)
                {
                    Application.Current.Dispatcher.Invoke(() => Clipboard.SetText(text));
                    return true;
                }

                // fallback
                Clipboard.SetText(text);
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}