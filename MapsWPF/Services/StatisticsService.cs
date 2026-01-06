using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using MapsWPF.Models;
using Newtonsoft.Json;

namespace MapsWPF.Services
{
    public class StatisticsService
    {
        private readonly string _filePath;
        private readonly SettingsService _settingsService;

        public ObservableCollection<StatisticRecord> Records { get; private set; } = new ObservableCollection<StatisticRecord>();

        public StatisticsService(SettingsService settingsService)
        {
            _settingsService = settingsService;
            var folder = _settingsService.GetSettingsFolder();
            _filePath = Path.Combine(folder, "statistics.json");
            LoadRecords();
        }

        private void LoadRecords()
        {
            try
            {
                if (File.Exists(_filePath))
                {
                    var json = File.ReadAllText(_filePath);
                    var list = JsonConvert.DeserializeObject<System.Collections.Generic.List<StatisticRecord>>(json);
                    if (list != null)
                    {
                        foreach (var item in list) Records.Add(item);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error loading statistics: {ex.Message}");
            }
        }

        private void SaveRecords()
        {
            try
            {
                var json = JsonConvert.SerializeObject(Records, Formatting.Indented);
                File.WriteAllText(_filePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error saving statistics: {ex.Message}");
            }
        }

        public (bool success, string message) TryAddRecord(StatisticRecord record)
        {
            bool isEnd = record.TemplateName.IndexOf("End", StringComparison.OrdinalIgnoreCase) >= 0 || 
                         record.TemplateName.IndexOf("Кінець", StringComparison.OrdinalIgnoreCase) >= 0 ||
                         record.TemplateName.IndexOf("Закінчення", StringComparison.OrdinalIgnoreCase) >= 0;
            
            if (isEnd)
            {
                // Find last start record
                var lastStart = Records.OrderByDescending(r => r.Date)
                                       .FirstOrDefault(r => r.TemplateName.IndexOf("Start", StringComparison.OrdinalIgnoreCase) >= 0 || 
                                                            r.TemplateName.IndexOf("Початок", StringComparison.OrdinalIgnoreCase) >= 0);

                if (lastStart != null)
                {
                    var diff = record.Date - lastStart.Date;
                    if (diff.TotalMinutes < 3)
                    {
                        return (false, $"Між 'Початок' ({lastStart.Date:HH:mm}) і 'Кінець' ще не пройшло 3 хвилини (пройшло {diff.TotalMinutes:F1} хв).");
                    }
                }
            }

            Records.Insert(0, record); // Add to top
            SaveRecords();
            return (true, "Запис додано в статистику");
        }
    }
}
