using System;
using System.Diagnostics;
using System.IO;
using MapsWPF.Models;
using Newtonsoft.Json;

namespace MapsWPF.Services
{
    public class SettingsService
    {
        private readonly string _settingsFolder;
        private readonly string _coordinatesPath;
        private readonly string _attackPointPath;

        public MapStartSettings StartSettings { get; private set; }
        public AttackSettings AttackSettings { get; private set; }

        public event Action? OnAttackSettingsChanged;

        public SettingsService()
        {
            var localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData
            );

            _settingsFolder = Path.Combine(
                localAppData,
                "MapsWPF",
                "settings"
            );

            Directory.CreateDirectory(_settingsFolder);

            _coordinatesPath = Path.Combine(
                _settingsFolder,
                "coordinates.json"
            );

            _attackPointPath = Path.Combine(
                _settingsFolder,
                "attack_point.json"
            );

            LoadSettings();
        }

        public string GetSettingsFolder()
        {
            return _settingsFolder;
        }

        private void LoadSettings()
        {
            StartSettings = LoadOrDefault<MapStartSettings>(
                _coordinatesPath
            );

            AttackSettings = LoadOrDefault<AttackSettings>(
                _attackPointPath
            );

            AttackSettings.PropertyChanged += AttackSettings_PropertyChanged;
        }

        private static T LoadOrDefault<T>(string path)
            where T : new()
        {
            if (!File.Exists(path))
            {
                return new T();
            }

            try
            {
                var json = File.ReadAllText(path);

                return JsonConvert.DeserializeObject<T>(json) ?? new T();
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"Не вдалося завантажити '{path}': {ex}"
                );

                return new T();
            }
        }

        private void AttackSettings_PropertyChanged(
            object? sender,
            System.ComponentModel.PropertyChangedEventArgs e)
        {
            SaveAttackSettings();
            OnAttackSettingsChanged?.Invoke();
        }

        public void SaveStartSettings()
        {
            Save(
                _coordinatesPath,
                StartSettings
            );
        }

        public void SaveAttackSettings()
        {
            Save(
                _attackPointPath,
                AttackSettings
            );
        }

        private static void Save<T>(
            string path,
            T data)
        {
            try
            {
                var json = JsonConvert.SerializeObject(
                    data,
                    Formatting.Indented
                );

                File.WriteAllText(
                    path,
                    json
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"Не вдалося зберегти '{path}': {ex}"
                );
            }
        }
    }
}