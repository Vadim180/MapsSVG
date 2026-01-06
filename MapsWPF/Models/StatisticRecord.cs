using System;

namespace MapsWPF.Models
{
    public class StatisticRecord
    {
        public DateTime Date { get; set; }
        public string Position { get; set; } = string.Empty;
        public string Drone { get; set; } = string.Empty;
        public string Pilot { get; set; } = string.Empty;
        public string Distance { get; set; } = string.Empty;
        public string TemplateName { get; set; } = string.Empty;
        public bool IsTargetDestroyed { get; set; }
        public bool IsBoardReturned { get; set; }
    }
}
