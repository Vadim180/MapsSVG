using System;
using System.ComponentModel;
using System.Windows.Input;
using MapsWPF.Controllers;
using MapsWPF.Services;

namespace MapsWPF.ViewModels
{
    public class ReportViewModel : INotifyPropertyChanged
    {
        private readonly ReportController _controller;
        private readonly ClipboardService? _clipboard;
        private readonly NotificationService? _notification;
        private string _reportText = string.Empty;

        public ReportViewModel(ReportController controller, ClipboardService? clipboard, NotificationService? notification)
        {
            _controller = controller ?? throw new ArgumentNullException(nameof(controller));
            _clipboard = clipboard;
            _notification = notification;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public string ReportText
        {
            get => _reportText;
            set
            {
                if (_reportText != value)
                {
                    _reportText = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ReportText)));
                }
            }
        }
    }
}