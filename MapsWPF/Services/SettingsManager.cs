using System;
using System.IO;
using Newtonsoft.Json;
using MapsWPF.Models;

namespace MapsWPF.Services
{
    public class SettingsManager
    {
        private readonly string _coordinatesPath;
        private readonly string _attackPointPath;

        public MapStartSettings StartSettings { get; private set; }
        public AttackSettings AttackSettings { get; private set; }

        public event Action OnAttackSettingsChanged;

        public SettingsManager()
        {
            var baseDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "settings");
            if (!Directory.Exists(baseDir))
            {
                Directory.CreateDirectory(baseDir);
            }

            _coordinatesPath = Path.Combine(baseDir, "coordinates.json");
            _attackPointPath = Path.Combine(baseDir, "attack_point.json");

            LoadSettings();
        }

        private void LoadSettings()
        {
            // Load Coordinates
            if (File.Exists(_coordinatesPath))
            {
                try
                {
                    var json = File.ReadAllText(_coordinatesPath);
                    StartSettings = JsonConvert.DeserializeObject<MapStartSettings>(json) ?? new MapStartSettings();
                }
                catch
                {
                    StartSettings = new MapStartSettings();
                }
            }
            else
            {
                StartSettings = new MapStartSettings();
                SaveStartSettings(); // Create default file
            }

            // Load Attack Settings
            if (File.Exists(_attackPointPath))
            {
                try
                {
                    var json = File.ReadAllText(_attackPointPath);
                    AttackSettings = JsonConvert.DeserializeObject<AttackSettings>(json) ?? new AttackSettings();
                }
                catch
                {
                    AttackSettings = new AttackSettings();
                }
            }
            else
            {
                AttackSettings = new AttackSettings();
                SaveAttackSettings(); // Create default file
            }

            // Hook up auto-save
            AttackSettings.PropertyChanged += (s, e) =>
            {
                SaveAttackSettings();
                OnAttackSettingsChanged?.Invoke();
            };
        }

        public void SaveStartSettings()
        {
            try
            {
                var json = JsonConvert.SerializeObject(StartSettings, Formatting.Indented);
                File.WriteAllText(_coordinatesPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving coordinates: {ex.Message}");
            }
        }

        public void SaveAttackSettings()
        {
            try
            {
                var json = JsonConvert.SerializeObject(AttackSettings, Formatting.Indented);
                File.WriteAllText(_attackPointPath, json);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error saving attack settings: {ex.Message}");
            }
        }
    }
}
