using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Diagnostics;
using MapsWPF.Data.Defaults;

namespace MapsWPF.Services
{
    public class TemplateService
    {

        public Dictionary<string, List<string>> Position_Point { get; set; } = new Dictionary<string, List<string>>();
        public Dictionary<string, List<string>> DroneByPosition { get; set; } = new Dictionary<string, List<string>>();

        public List<string> StartWorkShablon { get; set; } = new List<string>();
        public List<string> EndWorkShablon { get; set; } = new List<string>();
        public List<string> ReportWorkShablon { get; set; } = new List<string>();
        public List<string> Targets { get; set; } = new List<string>();

        // templates map (template name -> lines)
        public Dictionary<string, List<string>> Templates { get; set; } = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);

        public List<string> CustomReportWorkShablon { get; set; }

        // Last selections to persist UI state across runs
        public string LastHeight { get; set; }
        public string LastSelectedPosition { get; set; }
        public string LastSelectedPilot { get; set; }
        public string LastSelectedDrone { get; set; }
        public string LastSelectedTarget { get; set; }
        public bool RotateAfterGenerate { get; set; } = false;

        // Additional persisted report inputs
        public string Frequencies { get; set; } = string.Empty;
        public string Purpose { get; set; } = string.Empty;

        // Defaults for report placeholders
        public string DefaultTargetStatus { get; set; } = "Ціль знищено.";
        public string DefaultExpenses { get; set; } = "Борт втрачено";

        public TemplateService()
        {
            if (CustomReportWorkShablon == null)
            {
                CustomReportWorkShablon = new List<string>();
            }
        }

        public void InitializeData()
        {
            StartWorkShablon = ReportTemplateDefaults.CreateStartWorkTemplate();
            EndWorkShablon = ReportTemplateDefaults.CreateEndWorkTemplate();
            ReportWorkShablon = ReportTemplateDefaults.CreateReportWorkTemplate();

            Templates = ReportTemplateDefaults.CreateTemplateMap();

            if (CustomReportWorkShablon == null)
            {
                CustomReportWorkShablon = new List<string>();
            }
        }

        // Folder next to executable where settings JSON files are stored
        public string SettingsFolderPath
        {
            get
            {
                var localAppData = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData
                );

                return Path.Combine(
                    localAppData,
                    "MapsWPF",
                    "settings"
                );
            }
        }

        public void LoadAllData()
        {
            Console.WriteLine(
                $"TemplateService.LoadAllData: SettingsFolderPath={SettingsFolderPath}"
            );

            Directory.CreateDirectory(SettingsFolderPath);

            // 1. Не чіпаємо наявну логіку шаблонів.
            InitializeData();

            // 2. Позиції, пілоти, дрони і дефолтні цілі беремо з коду програми.
            ApplyDefaultReportSelectionLists();

            // 3. Якщо користувач зберіг свої шаблони — вони замінюють дефолтні шаблони.
            LoadUserTemplatesIfExists();

            // 4. Користувацькі цілі додаються до дефолтних цілей.
            LoadUserTargetsIfExists();

            // 5. Останні вибори/поля форми звіту — локальні.
            LoadLastChoicesIfExists();
        }

        private void LoadLastChoicesIfExists()
        {
            try
            {
                var lastPath = Path.Combine(
                    SettingsFolderPath,
                    "lastchoices.json"
                );

                if (!File.Exists(lastPath))
                {
                    return;
                }

                var json = File.ReadAllText(lastPath);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                dynamic data = JsonConvert.DeserializeObject(json);

                if (data == null)
                {
                    return;
                }

                LastHeight = data.LastHeight?.ToString() ?? LastHeight;
                LastSelectedPosition = data.LastSelectedPosition?.ToString() ?? LastSelectedPosition;
                LastSelectedPilot = data.LastSelectedPilot?.ToString() ?? LastSelectedPilot;
                LastSelectedDrone = data.LastSelectedDrone?.ToString() ?? LastSelectedDrone;
                LastSelectedTarget = data.LastSelectedTarget?.ToString() ?? LastSelectedTarget;

                if (data.RotateAfterGenerate != null)
                {
                    RotateAfterGenerate = (bool)data.RotateAfterGenerate;
                }

                Frequencies = data.Frequencies?.ToString() ?? Frequencies;
                Purpose = data.Purpose?.ToString() ?? Purpose;
                DefaultTargetStatus = data.DefaultTargetStatus?.ToString() ?? DefaultTargetStatus;
                DefaultExpenses = data.DefaultExpenses?.ToString() ?? DefaultExpenses;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TemplateService] LoadLastChoicesIfExists: {ex}");
            }
        }

        private void ApplyDefaultReportSelectionLists()
        {
            Position_Point = ReportSelectionDefaults.CreatePositionPilots();
            DroneByPosition = ReportSelectionDefaults.CreateDronesByPosition();
            Targets = ReportSelectionDefaults.CreateTargets();
        }

        private void LoadUserTemplatesIfExists()
        {
            try
            {
                var templatesPath = Path.Combine(
                    SettingsFolderPath,
                    "templates.json"
                );

                if (!File.Exists(templatesPath))
                {
                    return;
                }

                var json = File.ReadAllText(templatesPath);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                var loadedTemplates =
                    JsonConvert.DeserializeObject<Dictionary<string, List<string>>>(
                        json
                    );

                if (loadedTemplates == null ||
                    loadedTemplates.Count == 0)
                {
                    return;
                }

                var hasRealTemplate = false;

                foreach (var item in loadedTemplates)
                {
                    if (string.IsNullOrWhiteSpace(item.Key))
                    {
                        continue;
                    }

                    if (item.Value == null ||
                        item.Value.Count == 0)
                    {
                        continue;
                    }

                    Templates[item.Key.Trim()] = item.Value
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(NormalizeTemplateLine)
                        .ToList();

                    hasRealTemplate = true;
                }

                if (!hasRealTemplate)
                {
                    return;
                }

                if (Templates.TryGetValue("Report", out var report))
                {
                    ReportWorkShablon = report;
                }

                if (Templates.TryGetValue("StartWork", out var start))
                {
                    StartWorkShablon = start;
                }

                if (Templates.TryGetValue("EndWork", out var end))
                {
                    EndWorkShablon = end;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TemplateService] LoadUserTemplatesIfExists: {ex}");
            }
        }

        private static string NormalizeTemplateLine(string line)
        {
            return (line ?? string.Empty)
                .Trim()
                .Replace("{LocalCiti}", "{Direction}");
        }

        private void LoadUserTargetsIfExists()
        {
            try
            {
                var targetsPath = Path.Combine(
                    SettingsFolderPath,
                    "targets.json"
                );

                if (!File.Exists(targetsPath))
                {
                    return;
                }

                var json = File.ReadAllText(targetsPath);

                if (string.IsNullOrWhiteSpace(json))
                {
                    return;
                }

                List<string>? userTargets = null;

                var token = JToken.Parse(json);

                if (token.Type == JTokenType.Array)
                {
                    userTargets = token
                        .Values<string>()
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .ToList();
                }
                else if (token.Type == JTokenType.Object &&
                         token["Targets"] is JArray array)
                {
                    userTargets = array
                        .Values<string>()
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Select(x => x.Trim())
                        .ToList();
                }

                if (userTargets == null ||
                    userTargets.Count == 0)
                {
                    return;
                }

                foreach (var target in userTargets)
                {
                    if (!Targets.Contains(target, StringComparer.OrdinalIgnoreCase))
                    {
                        Targets.Add(target);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TemplateService] LoadUserTargetsIfExists: {ex}");
            }
        }

        public void SaveUserTarget(string target)
        {
            if (string.IsNullOrWhiteSpace(target))
            {
                return;
            }

            target = target.Trim();

            if (!Targets.Contains(target, StringComparer.OrdinalIgnoreCase))
            {
                Targets.Add(target);
            }

            SaveUserTargets();
        }

        private void SaveUserTargets()
        {
            try
            {
                Directory.CreateDirectory(SettingsFolderPath);

                var targetsPath = Path.Combine(
                    SettingsFolderPath,
                    "targets.json"
                );

                var defaultTargets = ReportSelectionDefaults.CreateTargets();

                var userTargets = (Targets ?? new List<string>())
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Select(x => x.Trim())
                    .Where(x => !defaultTargets.Contains(x, StringComparer.OrdinalIgnoreCase))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(x => x)
                    .ToList();

                if (userTargets.Count == 0)
                {
                    if (File.Exists(targetsPath))
                    {
                        File.Delete(targetsPath);
                    }

                    return;
                }

                var json = JsonConvert.SerializeObject(
                    new
                    {
                        Targets = userTargets
                    },
                    Formatting.Indented
                );

                File.WriteAllText(targetsPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TemplateService] SaveUserTargets: {ex}");
            }
        }

        public void SaveTargetsToSettingsFolder()
        {
            SaveUserTargets();
            SaveLastChoices();
        }

        // Public helpers for templates and lists used by UI and controllers
        public List<string> ReportWorkShablonActual => (CustomReportWorkShablon != null && CustomReportWorkShablon.Count > 0) ? CustomReportWorkShablon : ReportWorkShablon;



        public void SaveTemplateByName(string name, List<string> template)
        {
            if (string.IsNullOrWhiteSpace(name) || template == null)
            {
                return;
            }

            var normalizedTemplate = template
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .Select(NormalizeTemplateLine)
                .ToList();

            if (normalizedTemplate.Count == 0)
            {
                return;
            }

            if (Templates == null)
            {
                Templates = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
            }

            var templateName = name.Trim();

            Templates[templateName] = normalizedTemplate;

            if (string.Equals(templateName, "Report", StringComparison.OrdinalIgnoreCase))
            {
                ReportWorkShablon = normalizedTemplate;
            }

            if (string.Equals(templateName, "StartWork", StringComparison.OrdinalIgnoreCase))
            {
                StartWorkShablon = normalizedTemplate;
            }

            if (string.Equals(templateName, "EndWork", StringComparison.OrdinalIgnoreCase))
            {
                EndWorkShablon = normalizedTemplate;
            }

            SaveTemplatesToSettingsFolder();
        }

        public List<string> GetTemplateByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return new List<string>();
            if (Templates != null && Templates.TryGetValue(name.Trim(), out var t) && t != null) return t;
            // fallback to legacy names
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "startwork": case "start": return StartWorkShablon ?? new List<string>();
                case "endwork": case "end": return EndWorkShablon ?? new List<string>();
                default: return ReportWorkShablon ?? new List<string>();
            }
        }

        public void SaveTemplatesToSettingsFolder()
        {
            try
            {
                var folder = SettingsFolderPath;
                Directory.CreateDirectory(folder);

                var templatesPath = Path.Combine(folder, "templates.json");

                var map = Templates ??
                          new Dictionary<string, List<string>>(
                              StringComparer.OrdinalIgnoreCase
                          );

                File.WriteAllText(
                    templatesPath,
                    JsonConvert.SerializeObject(map, Formatting.Indented)
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TemplateService] SaveTemplatesToSettingsFolder: {ex}");
            }

            SaveLastChoices();
        }

        public void SaveLastChoices()
        {
            try
            {
                var folder = SettingsFolderPath;
                Directory.CreateDirectory(folder);
                var lastPath = Path.Combine(folder, "lastchoices.json");
                var obj = new
                {
                    LastHeight = LastHeight,
                    LastSelectedPosition = LastSelectedPosition,
                    LastSelectedPilot = LastSelectedPilot,
                    LastSelectedDrone = LastSelectedDrone,
                    LastSelectedTarget = LastSelectedTarget,
                    RotateAfterGenerate = RotateAfterGenerate,
                    Frequencies = Frequencies,
                    Purpose = Purpose,
                    //FlyDirectionList = FlyDirectionList,
                    DefaultTargetStatus = DefaultTargetStatus,
                    DefaultExpenses = DefaultExpenses
                };
                File.WriteAllText(lastPath, JsonConvert.SerializeObject(obj, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[ContextName] {ex}");
            }
        }

    }

}

