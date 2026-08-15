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
        private readonly StatisticsService? _statisticsService;

        private string _templateText = string.Empty;
        private string _selectedTemplate = string.Empty;
        private bool _templatesInitialized = false;

        public TemplateEditorViewModel(TemplateService templateService, NotificationService? notificationService, StatisticsService? statisticsService, Func<string, System.Collections.Generic.List<string>, string>? generateFunc = null, Action<string>? onGenerated = null)
        {
            _templateService = templateService ?? throw new ArgumentNullException(nameof(templateService));
            _notificationService = notificationService;
            _statisticsService = statisticsService;
            _generateFunc = generateFunc;
            _onGenerated = onGenerated;

            SaveCommand = new RelayCommand(_ => ExecuteSave());
            ResetCommand = new RelayCommand(_ => ExecuteReset());
            LoadDefaultCommand = new RelayCommand(_ => ExecuteLoadDefault());
            GenerateCommand = new RelayCommand(_ => ExecuteGenerate());


            // Initialize Rotate from persisted service value
            RotateAfterGenerate = _templateService.RotateAfterGenerate;

            // Placeholders for UI
            Placeholders = new[] {
                "{height}", "{Frequencies}", "{Purpose}", "{Direction}", "{UnitName}", "{MGRS_Short}", "{CurrentCoordMGRS}", "{CurrentCoordUTM}", "{azimyth}", "{range}", "{Time}", "{Position}", "{Pilot}", "{DroneBy}", "{ShootingTarget}", "{TargetType}", "{TargetStatus}", "{Expenses}", "{AdditionalInfo}"
            };

            // Initialize from service (RefreshLists will set initial SelectedTemplate to first template loaded)
            RefreshLists();

            // Load persisted last values (do not override user-selected defaults unless present)
            LastHeight = _templateService.LastHeight ?? string.Empty;
            Frequencies = _templateService.Frequencies ?? string.Empty;
            Purpose = _templateService.Purpose ?? string.Empty;

            // report defaults
            DefaultTargetStatus = _templateService.DefaultTargetStatus ?? "Ціль знищено.";
            DefaultExpenses = _templateService.DefaultExpenses ?? "Борт втрачено";

            if (!string.IsNullOrWhiteSpace(_templateService.LastSelectedPosition) && PositionNames.Contains(_templateService.LastSelectedPosition)) SelectedPosition = _templateService.LastSelectedPosition;
            if (!string.IsNullOrWhiteSpace(_templateService.LastSelectedPilot) && Pilots.Contains(_templateService.LastSelectedPilot)) SelectedPilot = _templateService.LastSelectedPilot;
            if (!string.IsNullOrWhiteSpace(_templateService.LastSelectedDrone) && Drones.Contains(_templateService.LastSelectedDrone)) SelectedDrone = _templateService.LastSelectedDrone;
            if (!string.IsNullOrWhiteSpace(_templateService.LastSelectedTarget) && Targets.Contains(_templateService.LastSelectedTarget)) SelectedTarget = _templateService.LastSelectedTarget;
        }

        public event PropertyChangedEventHandler? PropertyChanged;

        public ICommand SaveCommand { get; }
        public ICommand ResetCommand { get; }
        public ICommand LoadDefaultCommand { get; }
        public ICommand GenerateCommand { get; }


        private readonly Func<string, System.Collections.Generic.List<string>, string>? _generateFunc;
        private readonly Action<string>? _onGenerated;

        public string[] Placeholders { get; private set; }

        public ObservableCollection<string> TemplateNames { get; private set; } = new();
        public ObservableCollection<string> Targets { get; private set; } = new();
        public ObservableCollection<string> PositionNames { get; private set; } = new();
        public ObservableCollection<string> Pilots { get; private set; } = new();
        public ObservableCollection<string> Drones { get; private set; } = new();

        public ObservableCollection<MapsWPF.Models.StatisticRecord>? Records => _statisticsService?.Records;

        private string _distance = string.Empty;
        public string Distance
        {
            get => _distance;
            set
            {
                if (_distance != value)
                {
                    _distance = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Distance)));
                }
            }
        }

        private bool _isTargetDestroyed;
        public bool IsTargetDestroyed
        {
            get => _isTargetDestroyed;
            set
            {
                if (_isTargetDestroyed != value)
                {
                    _isTargetDestroyed = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTargetDestroyed)));
                }
            }
        }

        private bool _isTargetReturned = true; // Default to true (checked) -> Board Returned
        public bool IsTargetReturned
        {
            get => _isTargetReturned;
            set
            {
                if (_isTargetReturned != value)
                {
                    _isTargetReturned = value;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsTargetReturned)));
                }
            }
        }

        private string _frequencies = string.Empty;
        public string Frequencies
        {
            get => _frequencies;
            set
            {
                if (_frequencies != value)
                {
                    _frequencies = value ?? string.Empty;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Frequencies)));
                    try { _templateService.Frequencies = _frequencies; _templateService.SaveLastChoices(); } catch { }
                }
            }
        }

        private string _purpose = string.Empty;
        public string Purpose
        {
            get => _purpose;
            set
            {
                if (_purpose != value)
                {
                    _purpose = value ?? string.Empty;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Purpose)));
                    try { _templateService.Purpose = _purpose; _templateService.SaveLastChoices(); } catch { }
                }
            }
        }

        // Report defaults (user-configurable)
        private string _defaultTargetStatus = "Ціль знищено.";
        public string DefaultTargetStatus
        {
            get => _defaultTargetStatus;
            set
            {
                if (_defaultTargetStatus != value)
                {
                    _defaultTargetStatus = value ?? string.Empty;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DefaultTargetStatus)));
                    try { _templateService.DefaultTargetStatus = _defaultTargetStatus; _templateService.SaveLastChoices(); } catch { }
                }
            }
        }

        private string _defaultExpenses = "Борт втрачено";
        public string DefaultExpenses
        {
            get => _defaultExpenses;
            set
            {
                if (_defaultExpenses != value)
                {
                    _defaultExpenses = value ?? string.Empty;
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DefaultExpenses)));
                    try { _templateService.DefaultExpenses = _defaultExpenses; _templateService.SaveLastChoices(); } catch { }
                }
            }
        }

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
                    var newVal = value ?? "Report";

                    _selectedTemplate = newVal;
                    TemplateText = string.Join(Environment.NewLine, _templateService.GetTemplateByName(_selectedTemplate) ?? new System.Collections.Generic.List<string>());
                    PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedTemplate)));
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

                if (lines.Count == 0)
                {
                    _notificationService?.Notify(
                        "Неможливо зберегти порожній шаблон.",
                        NotificationType.Warning
                    );

                    return;
                }

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

        private async void ExecuteGenerate()
        {
            try
            {
                // Persist current frequencies and purpose so replacements use up-to-date values
                try { _templateService.Frequencies = Frequencies ?? string.Empty; _templateService.Purpose = Purpose ?? string.Empty; _templateService.SaveLastChoices(); } catch { }

                // Preserve current selections so RefreshLists won't appear to clear them
                var prevPosition = SelectedPosition;
                var prevPilot = SelectedPilot;
                var prevDrone = SelectedDrone;
                var prevTarget = SelectedTarget;
                var prevTemplate = SelectedTemplate;

                // If user entered a target text manually, DO NOT persist it automatically to targets.json.
                // New targets remain transient for generation; persist targets only by editing targets.json manually.
                var currentTarget = SelectedTarget?.Trim();
                if (!string.IsNullOrEmpty(currentTarget))
                {
                    // Keep SelectedTarget as-is for generation; do not modify _templateService.Targets or write to disk.
                }

                var lines = TemplateText?.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries)
                    .Select(l => l.Trim()).Where(l => !string.IsNullOrEmpty(l)).ToList() ?? new System.Collections.Generic.List<string>();

                if (_generateFunc == null)
                {
                    _notificationService?.Notify("Генерація недоступна (контролер не встановлений)", NotificationType.Warning);
                    return;
                }

                // Use the UI dispatcher to execute generation code that depends on UI-owned objects. We use InvokeAsync so the work is queued on UI thread without deadlocking.
                try
                {
                    string result = await System.Windows.Application.Current.Dispatcher.InvokeAsync(() =>
                    {
                        try
                        {
                            return _generateFunc(SelectedTemplate, lines);
                        }
                        catch (Exception ex)
                        {
                            return "__GENERATE_ERROR__:" + ex.ToString();
                        }
                    });

                    if (string.IsNullOrWhiteSpace(result))
                    {
                        return;
                    }

                    if (result.StartsWith("__GENERATE_ERROR__:", StringComparison.Ordinal))
                    {
                        _notificationService?.Notify("Помилка генерації шаблону.", NotificationType.Error);
                        return;
                    }

                    if (_statisticsService != null)
                    {
                        var record = new MapsWPF.Models.StatisticRecord
                        {
                            Date = DateTime.Now,
                            Position = SelectedPosition,
                            Drone = SelectedDrone,
                            Pilot = SelectedPilot,
                            Distance = Distance,
                            TemplateName = SelectedTemplate,
                            IsTargetDestroyed = IsTargetDestroyed,
                            IsBoardReturned = IsTargetReturned
                        };

                        var (success, msg) = _statisticsService.TryAddRecord(record);
                        if (!success)
                        {
                            _notificationService?.Notify(msg, NotificationType.Warning);
                        }
                    }

                    // We're back on UI thread (because of await), so we can safely update UI
                    try
                    {
                        if (_onGenerated != null)
                        {
                            _onGenerated(result);
                        }
                        else
                        {
                            _notificationService?.Notify("Згенеровано звіт", NotificationType.Info);
                        }

                        // After potential saves, refresh only necessary lists (we already updated Units/LaunchAreas above) and restore prior selections (in dependency order)
                        if (!string.IsNullOrWhiteSpace(prevPosition) && PositionNames.Contains(prevPosition))
                        {
                            SelectedPosition = prevPosition;
                            RefreshPositionDetails();
                            if (!string.IsNullOrWhiteSpace(prevPilot) && Pilots.Contains(prevPilot)) SelectedPilot = prevPilot;
                            if (!string.IsNullOrWhiteSpace(prevDrone) && Drones.Contains(prevDrone)) SelectedDrone = prevDrone;
                        }
                        if (!string.IsNullOrWhiteSpace(prevTarget) && Targets.Contains(prevTarget)) SelectedTarget = prevTarget;

                        if (!string.IsNullOrWhiteSpace(prevTemplate))
                        {
                            var match = TemplateNames.FirstOrDefault(t => string.Equals(t?.Trim(), prevTemplate.Trim(), StringComparison.OrdinalIgnoreCase));
                            if (match != null)
                            {
                                SelectedTemplate = match;
                            }
                            else if (TemplateNames.Count > 0 && string.IsNullOrWhiteSpace(SelectedTemplate))
                            {
                                SelectedTemplate = TemplateNames.First();
                            }
                        }
                        else if (TemplateNames.Count > 0 && string.IsNullOrWhiteSpace(SelectedTemplate))
                        {
                            SelectedTemplate = TemplateNames.First();
                        }

                    }
                    catch (Exception ex)
                    {
                        _notificationService?.Notify($"Помилка генерації: {ex.Message}", NotificationType.Error);
                    }
                }
                catch (Exception ex)
                {
                    try { System.IO.File.AppendAllText(System.IO.Path.Combine(System.IO.Path.GetTempPath(), "maps_template_selection_debug.txt"), $"{DateTime.Now:O} ExecuteGenerate dispatch error: {ex}\n"); } catch { }
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
                var prevTemplate = SelectedTemplate;
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
                    // targets, positions
                     Targets.Clear();
                    if (_templateService.Targets != null) foreach (var t in _templateService.Targets) Targets.Add(t);

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

                var msg = $"RefreshLists: TemplateNames={TemplateNames.Count}, Targets={Targets.Count}, Positions={PositionNames.Count}";
                Console.WriteLine(msg);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in RefreshLists: {ex.Message}");
            }

            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TemplateNames)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Targets)));
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