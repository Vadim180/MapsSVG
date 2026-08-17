using System;
using System.ComponentModel;
using System.Windows.Input;

using MapsWPF.Services;

namespace MapsWPF.ViewModels
{
    public class ReportViewModel : INotifyPropertyChanged
    {
        private readonly ReportService _controller;
        private readonly ClipboardService? _clipboard;
        private readonly NotificationService? _notification;
        private string _reportText = string.Empty;
        private string _selectedReportTemplate = "Report";

        public string SelectedReportTemplate
        {
            get => _selectedReportTemplate;
            set
            {
                if (_selectedReportTemplate != value)
                {
                    _selectedReportTemplate = value ?? "Report";
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedReportTemplate)));
                }
            }
        }

        public ReportViewModel(ReportService controller, ClipboardService? clipboard, NotificationService? notification)
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