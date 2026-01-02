using System;
using System.ComponentModel;
using System.Linq;
using System.Windows.Input;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using MapsWPF.Services;

namespace MapsWPF.ViewModels
{
    public class TemplateEditorViewModel : INotifyPropertyChanged
    {
        private readonly TemplateService _templateService;
        private readonly NotificationService? _notificationService;

        private string _templateText = string.Empty;
        private string _selectedTemplate = string.Empty;
        private string _customUnit = string.Empty;
        private string _launchArea = string.Empty;
        private bool _templatesInitialized = false;

        public TemplateEditorViewModel(TemplateService templateService, NotificationService? notificationService, Func<System.Collections.Generic.List<string>, string>? generateFunc = null, Action<string>? onGenerated = null)
        {
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            _notificationService = notificationService;
            _generateFunc = generateFunc;
            _onGenerated = onGenerated;

            SaveCommand = new RelayCommand(_ => ExecuteSave());
            ResetCommand = new RelayCommand(_ => ExecuteReset());
            LoadDefaultCommand = new RelayCommand(_ => ExecuteLoadDefault());
            SaveLaunchAreaCommand = new RelayCommand(_ => ExecuteSaveLaunchArea());
            SetUnitNameCommand = new RelayCommand(_ => ExecuteSetUnitName());
            GenerateCommand = new RelayCommand(_ => ExecuteGenerate());

            // Initialize Rotate from persisted service value
            RotateAfterGenerate = _templateService.RotateAfterGenerate;

            // Placeholders for UI
            Placeholders = new[] {
                "{height}", "{Frequencies}", "{Purpose}", "{Direction}", "{LaunchArea}", "{UnitName}", "{MGRS_Short}", "{CurrentCoordMGRS}", "{CurrentCoordUTM}", "{azimyth}", "{range}", "{Time}", "{Position}", "{Pilot}", "{DroneBy}", "{ShootingTarget}", "{TargetType}", "{TargetStatus}", "{Expenses}", "{AdditionalInfo}"
            };

            // Initialize from service (RefreshLists will set initial SelectedTemplate to first template loaded)
            RefreshLists();
            CustomUnit = _templateService.CustomUnit;
            LaunchArea = _templateService.LaunchArea;
            // Load persisted last values (do not override user-selected defaults unless present)
            LastHeight = _templateService.LastHeight ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(_templateService.LastSelectedPosition) && PositionNames.Contains(_templateService.LastSelectedPosition)) SelectedPosition = _templateService.LastSelectedPosition;
            if (!string.IsNullOrWhiteSpace(_templateService.LastSelectedPilot) && Pilots.Contains(_templateService.LastSelectedPilot)) SelectedPilot = _templateService.LastSelectedPilot;
            if (!string.IsNullOrWhiteSpace(_templateService.LastSelectedDrone) && Drones.Contains(_templateService.LastSelectedDrone)) SelectedDrone = _templateService.LastSelectedDrone;
            if (!string.IsNullOrWhiteSpace(_templateService.LastSelectedTarget) && Targets.Contains(_templateService.LastSelectedTarget)) SelectedTarget = _templateService.LastSelectedTarget;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand LoadDefaultCommand { get; }
        public ICommand SaveLaunchAreaCommand { get; }
        public ICommand SetUnitNameCommand { get; }
        public ICommand GenerateCommand { get; }

        private readonly Func<System.Collections.Generic.List<string>, string>? _generateFunc;
        private readonly Action<string>? _onGenerated;

        public string[] Placeholders { get; private set; }

        public ObservableCollection<string> TemplateNames { get; private set; } = new();
        public ObservableCollection<string> Targets { get; private set; } = new();
        public ObservableCollection<string> UnitsHistory { get; private set; } = new();
        public ObservableCollection<string> LaunchAreasHistory { get; private set; } = new();
        public ObservableCollection<string> PositionNames { get; private set; } = new();
        public ObservableCollection<string> Pilots { get; private set; } = new();
        public ObservableCollection<string> Drones { get; private set; } = new();

        private string _selectedPilot;
        public string SelectedPilot
        {
            get => _selectedPilot;
            set
            {
                if (_selectedPilot != value)
                {
                    _selectedPilot = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedPilot)));
                    try { _templateService.LastSelectedPilot = _selectedPilot; _templateService.SaveLastChoices(); } catch { }
                }
            }
        }

        private string _selectedDrone;
        public string SelectedDrone
        {
            get => _selectedDrone;
            set
            {
                if (_selectedDrone != value)
                {
                    _selectedDrone = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedDrone)));
                    try { _templateService.LastSelectedDrone = _selectedDrone; _templateService.SaveLastChoices(); } catch { }
                }
            }
        }

        private string _selectedTarget;
        public string SelectedTarget
        {
            get => _selectedTarget;
            set
            {
                if (_selectedTarget != value)
                {
                    _selectedTarget = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTarget)));
                    try { _templateService.LastSelectedTarget = _selectedTarget; _templateService.SaveLastChoices(); } catch { }
                }
            }
        }

        public string TemplateText
        {
            get => _templateText;
            set
            {
                if (_templateText != value)
                {
                    _templateText = value ?? string.Empty;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TemplateText)));
                }
            }
        }

        private string _lastHeight = string.Empty;
        public string LastHeight
        {
            get => _lastHeight;
            set
            {
                if (_lastHeight != value)
                {
                    _lastHeight = value ?? string.Empty;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LastHeight)));
                    try { _templateService.LastHeight = _lastHeight; _templateService.SaveLastChoices(); } catch { }
                }
            }
        }

        public string SelectedTemplate
        {
            get => _selectedTemplate;
            set
            {
                if (_selectedTemplate != value)
                {
                    _selectedTemplate = value ?? "Report";
                    TemplateText = string.Join(Environment.NewLine, _templateService.GetTemplateByName(_selectedTemplate) ?? new System.Collections.Generic.List<string>());
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTemplate)));
                }
            }
        }

        public string CustomUnit
        {
            get => _customUnit;
            set
            {
                if (_customUnit != value)
                {
                    _customUnit = value ?? string.Empty;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(CustomUnit)));
                }
            }
        }

        public string LaunchArea
        {
            get => _launchArea;
            set
            {
                if (_launchArea != value)
                {
                    _launchArea = value ?? string.Empty;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LaunchArea)));
                }
            }
        }

        private bool _rotateAfterGenerate = false;
        public bool RotateAfterGenerate
        {
            get => _rotateAfterGenerate;
            set
            {
                if (_rotateAfterGenerate != value)
                {
                    _rotateAfterGenerate = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(RotateAfterGenerate)));
                    try { _templateService.RotateAfterGenerate = _rotateAfterGenerate; _templateService.SaveLastChoices(); } catch { }
                }
            }
        }

        private void ExecuteSave()
        {
            try
            {
                var lines = TemplateText?.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList() ?? new System.Collections.Generic.List<string>();

                // Save current template by selected name
                var name = string.IsNullOrWhiteSpace(SelectedTemplate) ? "Report" : SelectedTemplate;
                _templateService.SaveTemplateByName(name, lines);
                // refresh names and content from saved value
                RefreshLists();
                TemplateText = string.Join(Environment.NewLine, _templateService.GetTemplateByName(name) ?? new System.Collections.Generic.List<string>());
                _notificationService?.Notify($"Шаблон '{name}' збережено ({lines.Count} рядків)", NotificationType.Info);
            }
            catch (Exception ex)
            {
                _notificationService?.Notify($"Помилка збереження: {ex.Message}", NotificationType.Error);
            }
        }

        private void ExecuteReset()
        {
            try
            {
                var name = string.IsNullOrWhiteSpace(SelectedTemplate) ? "Report" : SelectedTemplate;
                TemplateText = string.Join(Environment.NewLine, _templateService.GetTemplateByName(name) ?? new System.Collections.Generic.List<string>());
                _notificationService?.Notify("Шаблон перезавантажено з файлу", NotificationType.Info);
            }
            catch (Exception ex)
            {
                _notificationService?.Notify($"Помилка: {ex.Message}", NotificationType.Error);
            }
        }

        private void ExecuteLoadDefault()
        {
            try
            {
                var name = string.IsNullOrWhiteSpace(SelectedTemplate) ? "Report" : SelectedTemplate;
                TemplateText = string.Join(Environment.NewLine, _templateService.ReportWorkShablon ?? new System.Collections.Generic.List<string>());
                _notificationService?.Notify("Дефолтний шаблон завантажено", NotificationType.Info);
            }
            catch (Exception ex)
            {
                _notificationService?.Notify($"Помилка: {ex.Message}", NotificationType.Error);
            }
        }

        private void ExecuteSaveLaunchArea()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(LaunchArea))
                {
                    _templateService.SaveLaunchArea(LaunchArea);
                    RefreshLists();
                    _notificationService?.Notify("LaunchArea збережено", NotificationType.Info);
                }
                else
                {
                    _notificationService?.Notify("Введіть LaunchArea", NotificationType.Warning);
                }
            }
            catch (Exception ex)
            {
                _notificationService?.Notify($"Помилка: {ex.Message}", NotificationType.Error);
            }
        }

        private void ExecuteSetUnitName()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(CustomUnit))
                {
                    _templateService.SaveCustomUnit(CustomUnit);
                    RefreshLists();
                    _notificationService?.Notify("Ім'я підрозділу встановлено", NotificationType.Info);
                }
                else
                {
                    _notificationService?.Notify("Введіть ім'я підрозділу", NotificationType.Warning);
                }
            }
            catch (Exception ex)
            {
                _notificationService?.Notify($"Помилка: {ex.Message}", NotificationType.Error);
            }
        }

        private void ExecuteGenerate()
        {
            try
            {
                // Preserve current selections so RefreshLists won't appear to clear them
                var prevPosition = SelectedPosition;
                var prevPilot = SelectedPilot;
                var prevDrone = SelectedDrone;
                var prevTarget = SelectedTarget;
                var prevTemplate = SelectedTemplate;
                var prevUnit = CustomUnit;

                // If user entered a target text manually, DO NOT persist it automatically to targets.json.
                // New targets remain transient for generation; persist targets only by editing targets.json manually.
                var currentTarget = SelectedTarget?.Trim();
                if (!string.IsNullOrEmpty(currentTarget))
                {
                    // Keep SelectedTarget as-is for generation; do not modify _templateService.Targets or write to disk.
                }

                // If LaunchArea was edited manually, persist it so it appears in history and JSON
                var la = LaunchArea?.Trim();
                if (!string.IsNullOrEmpty(la) && _templateService != null)
                {
                    if (_templateService.LaunchAreasHistory == null) _templateService.LaunchAreasHistory = new System.Collections.Generic.List<string>();
                    if (!_templateService.LaunchAreasHistory.Contains(la))
                    {
                        _templateService.SaveLaunchArea(la);
                        var laMsg = $"ExecuteGenerate: Saved LaunchArea '{la}' to { _templateService.SettingsFolderPath }";
                        Console.WriteLine(laMsg);
                        try { System.IO.Directory.CreateDirectory(_templateService.SettingsFolderPath); System.IO.File.AppendAllText(System.IO.Path.Combine(_templateService.SettingsFolderPath, "diagnostics.log"), DateTime.Now.ToString("o") + " " + laMsg + Environment.NewLine); } catch { }
                        RefreshLists();
                    }
                }

                // If Unit name was edited manually, persist it as well
                var unit = CustomUnit?.Trim();
                if (!string.IsNullOrEmpty(unit) && _templateService != null)
                {
                    if (_templateService.UnitsHistory == null) _templateService.UnitsHistory = new System.Collections.Generic.List<string>();
                    if (!_templateService.UnitsHistory.Contains(unit))
                    {
                        _templateService.SaveCustomUnit(unit);
                        var unitMsg = $"ExecuteGenerate: Saved CustomUnit '{unit}' to { _templateService.SettingsFolderPath }";
                        Console.WriteLine(unitMsg);
                        try { System.IO.Directory.CreateDirectory(_templateService.SettingsFolderPath); System.IO.File.AppendAllText(System.IO.Path.Combine(_templateService.SettingsFolderPath, "diagnostics.log"), DateTime.Now.ToString("o") + " " + unitMsg + Environment.NewLine); } catch { }
                        RefreshLists();
                    }
                }

                var lines = TemplateText?.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList() ?? new System.Collections.Generic.List<string>();

                if (_generateFunc == null)
                {
                    _notificationService?.Notify("Генерація недоступна (контролер не встановлений)", NotificationType.Warning);
                    return;
                }

                var generated = _generateFunc(lines);
                if (_onGenerated != null)
                {
                    _onGenerated(generated);
                }
                else
                {
                    _notificationService?.Notify("Згенеровано звіт", NotificationType.Info);
                }

                // After potential saves, refresh lists and restore prior selections (in dependency order)
                RefreshLists();
                if (!string.IsNullOrWhiteSpace(prevPosition) && PositionNames.Contains(prevPosition))
                {
                    SelectedPosition = prevPosition;
                    RefreshPositionDetails();
                    if (!string.IsNullOrWhiteSpace(prevPilot) && Pilots.Contains(prevPilot)) SelectedPilot = prevPilot;
                    if (!string.IsNullOrWhiteSpace(prevDrone) && Drones.Contains(prevDrone)) SelectedDrone = prevDrone;
                }
                if (!string.IsNullOrWhiteSpace(prevTarget) && Targets.Contains(prevTarget)) SelectedTarget = prevTarget;

                // Restore template and custom unit if possible so they don't appear to 'disappear' after generation
                if (!string.IsNullOrWhiteSpace(prevTemplate) && TemplateNames.Contains(prevTemplate)) SelectedTemplate = prevTemplate;
                else if (TemplateNames.Count > 0 && string.IsNullOrWhiteSpace(SelectedTemplate)) SelectedTemplate = TemplateNames.First();

                // If Rotate is enabled, select the next template in the list (wrap-around)
                if (RotateAfterGenerate && TemplateNames.Count > 0)
                {
                    var cur = SelectedTemplate;
                    int idx = TemplateNames.IndexOf(cur);
                    if (idx < 0) idx = 0;
                    int next = (idx + 1) % TemplateNames.Count;
                    SelectedTemplate = TemplateNames[next];
                }

                if (!string.IsNullOrWhiteSpace(prevUnit))
                {
                    CustomUnit = prevUnit;
                    if (!UnitsHistory.Contains(prevUnit))
                    {
                        UnitsHistory.Insert(0, prevUnit);
                        try { if (_templateService != null && _templateService.UnitsHistory != null && !_templateService.UnitsHistory.Contains(prevUnit)) _templateService.UnitsHistory.Insert(0, prevUnit); } catch { }
                    }
                }
            }
            catch (Exception ex)
            {
                _notificationService?.Notify($"Помилка генерації: {ex.Message}", NotificationType.Error);
            }
        }


        private void RefreshLists()
        {
            try
            {
                Action update = () =>
                {
                    // templates
                    TemplateNames.Clear();
                    var names = (_templateService?.Templates != null && _templateService.Templates.Count > 0) ? _templateService.Templates.Keys.ToList() : new List<string> { "Report", "StartWork", "EndWork" };
                    foreach (var n in names) TemplateNames.Add(n);

                    // On first refresh (initial load), make the first template the selected one (as loaded from JSON)
                    if (!_templatesInitialized && TemplateNames.Count > 0)
                    {
                        SelectedTemplate = TemplateNames.First();
                        _templatesInitialized = true;
                    }
                    // targets, units, launch areas, positions
                    Targets.Clear();
                    if (_templateService.Targets != null) foreach (var t in _templateService.Targets) Targets.Add(t);

                    UnitsHistory.Clear();
                    if (_templateService.UnitsHistory != null) foreach (var u in _templateService.UnitsHistory) UnitsHistory.Add(u);

                    LaunchAreasHistory.Clear();
                    if (_templateService.LaunchAreasHistory != null) foreach (var la in _templateService.LaunchAreasHistory) LaunchAreasHistory.Add(la);

                    PositionNames.Clear();
                    if (_templateService.Position_Point != null) foreach (var p in _templateService.Position_Point.Keys.OrderBy(k => k)) PositionNames.Add(p);

                    // refresh pilots/drones for selected position if any
                    RefreshPositionDetails();
                };

                if (System.Windows.Application.Current?.Dispatcher != null && !System.Windows.Application.Current.Dispatcher.CheckAccess())
                {
                    System.Windows.Application.Current.Dispatcher.Invoke(update);
                }
                else
                {
                    update();
                }

                var msg = $"RefreshLists: TemplateNames={TemplateNames.Count}, Targets={Targets.Count}, Units={UnitsHistory.Count}, LaunchAreas={LaunchAreasHistory.Count}, Positions={PositionNames.Count}, Pilots={Pilots.Count}, Drones={Drones.Count}, SettingsFolder={_templateService.SettingsFolderPath}";
                Console.WriteLine(msg);
                try { System.IO.Directory.CreateDirectory(_templateService.SettingsFolderPath); System.IO.File.AppendAllText(System.IO.Path.Combine(_templateService.SettingsFolderPath, "diagnostics.log"), DateTime.Now.ToString("o") + " " + msg + Environment.NewLine); } catch { }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RefreshLists: {ex.Message}");
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TemplateNames)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Targets)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(UnitsHistory)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(LaunchAreasHistory)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(PositionNames)));
        }

        private string _selectedPosition;
        public string SelectedPosition
        {
            get => _selectedPosition;
            set
            {
                if (_selectedPosition != value)
                {
                    _selectedPosition = value;
                    RefreshPositionDetails();
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedPosition)));
                    try { _templateService.LastSelectedPosition = _selectedPosition; _templateService.SaveLastChoices(); } catch { }
                }
            }
        }

        private void RefreshPositionDetails()
        {
            Pilots.Clear();
            Drones.Clear();
            if (!string.IsNullOrEmpty(SelectedPosition) && _templateService.Position_Point != null && _templateService.Position_Point.TryGetValue(SelectedPosition, out var pilots))
            {
                foreach (var p in pilots.Distinct()) Pilots.Add(p);
            }
            if (!string.IsNullOrEmpty(SelectedPosition) && _templateService.DroneByPosition != null && _templateService.DroneByPosition.TryGetValue(SelectedPosition, out var drones))
            {
                foreach (var d in drones.Distinct()) Drones.Add(d);
            }

            // do not preselect pilot/drone; user chooses explicitly
        }

    }
}