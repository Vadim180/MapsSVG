using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsPresentation;

using MapsWPF.Services;
using MapsWPF.Utils;
using MapsWPF.GMapIntegration;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Controls.Primitives;
using System.Threading.Tasks;
using MapsWPF.Services.Settlements;

namespace MapsWPF
{
    public partial class MainWindow : Window, Services.ISelectionProvider, Services.IReportOutput
    {
        [Conditional("DEBUG")]
        private void DebugSplitterState(
    string source,
    double? horizontalChange = null,
    double? currentRightWidth = null,
    double? requestedRightWidth = null,
    double? clampedRightWidth = null,
    double? futureMapWidth = null)
        {
            var logPath = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "MapsWPF-bounds-diagnostics.log"
            );

            var sb = new StringBuilder();

            sb.AppendLine();
            sb.AppendLine("============================================================");
            sb.AppendLine($"{DateTime.Now:HH:mm:ss.fff} | {source}");
            sb.AppendLine("============================================================");

            if (horizontalChange.HasValue)
            {
                sb.AppendLine($"HorizontalChange={horizontalChange.Value.ToString("0.##########", CultureInfo.InvariantCulture)}");
            }

            if (currentRightWidth.HasValue)
            {
                sb.AppendLine($"CurrentRightWidth={currentRightWidth.Value.ToString("0.##########", CultureInfo.InvariantCulture)}");
            }

            if (requestedRightWidth.HasValue)
            {
                sb.AppendLine($"RequestedRightWidth={requestedRightWidth.Value.ToString("0.##########", CultureInfo.InvariantCulture)}");
            }

            if (clampedRightWidth.HasValue)
            {
                sb.AppendLine($"ClampedRightWidth={clampedRightWidth.Value.ToString("0.##########", CultureInfo.InvariantCulture)}");
            }

            if (futureMapWidth.HasValue)
            {
                sb.AppendLine($"FutureMapWidth={futureMapWidth.Value.ToString("0.##########", CultureInfo.InvariantCulture)}");
            }

            sb.AppendLine();
            sb.AppendLine("ACTUAL LAYOUT:");
            sb.AppendLine($"MainLayoutGrid.ActualWidth={MainLayoutGrid.ActualWidth.ToString("0.##########", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"MapColumn.ActualWidth={MapColumn.ActualWidth.ToString("0.##########", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"MainMap.ActualWidth={MainMap.ActualWidth.ToString("0.##########", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"SplitterColumn.ActualWidth={SplitterColumn.ActualWidth.ToString("0.##########", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"RightColumn.ActualWidth={RightColumn.ActualWidth.ToString("0.##########", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"RightSettingsGrid.ActualWidth={RightSettingsGrid.ActualWidth.ToString("0.##########", CultureInfo.InvariantCulture)}");

            sb.AppendLine();
            sb.AppendLine("COLUMN WIDTH SETTINGS:");
            sb.AppendLine($"RightColumn.Width.Value={RightColumn.Width.Value.ToString("0.##########", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"RightColumn.Width.GridUnitType={RightColumn.Width.GridUnitType}");

            var formulaMapWidth = MainLayoutGrid.ActualWidth -
                                  SplitterColumn.ActualWidth -
                                  RightColumn.ActualWidth;

            sb.AppendLine();
            sb.AppendLine("WIDTH FORMULA:");
            sb.AppendLine($"FormulaMapWidth=MainLayoutGrid.ActualWidth - SplitterColumn.ActualWidth - RightColumn.ActualWidth");
            sb.AppendLine($"FormulaMapWidth={formulaMapWidth.ToString("0.##########", CultureInfo.InvariantCulture)}");
            sb.AppendLine($"FormulaMinusMainMap={Math.Abs(formulaMapWidth - MainMap.ActualWidth).ToString("0.##########", CultureInfo.InvariantCulture)}");

            if (futureMapWidth.HasValue)
            {
                sb.AppendLine($"FutureMinusMainMapActual={(futureMapWidth.Value - MainMap.ActualWidth).ToString("0.##########", CultureInfo.InvariantCulture)}");
                sb.AppendLine($"FutureMinusMapColumnActual={(futureMapWidth.Value - MapColumn.ActualWidth).ToString("0.##########", CultureInfo.InvariantCulture)}");
            }

            System.IO.File.AppendAllText(logPath, sb.ToString());
            Debug.WriteLine(sb.ToString());
        }

        private bool _isInitializing = true;
        private bool _isMapInitialized = false;

        private bool _flightDirectionChanged = false;
        private string _lastSavedFlightDirection = string.Empty;

        private List<string> _lastSavedSelectedCities = new List<string>();

        // coordinate converter
        private CoordinateConverter _coordinateConverter = new();

        // Reports: template service and controller
        private Services.TemplateService? _templateService;
        private ReportService? _reportService;

        // Map service (concrete)
        private Services.MapService? _mapService;

        // Clipboard and notification services (concrete)
        private Services.ClipboardService? _clipboardService;
        private Services.NotificationService? _notificationService;
        private DispatcherTimer? _notificationTimer;

        // Last known UTM zone/band from TryGetUTM conversion
        private int _lastUtmZone = 0;
        private char _lastUtmBand = ' ';

        // Cache statistics debouncing
        private DispatcherTimer _cacheStatsUpdateTimer;

        private static readonly HttpClient _httpClient = new HttpClient();

        private readonly Services.StatisticsService _statisticsService;

        private readonly GMapControlConfigurator _gMapControlConfigurator = new();
        private readonly GMapTargetMarkerManager _targetMarkerManager = new();
        private readonly GMapWorkAreaEditor _workAreaEditor = new();

        private readonly SettlementGeometryService _settlementGeometryService = new();

        private SettlementOverpassLoader? _settlementOverpassLoader;

        private readonly List<GMapRoute> _settlementBoundaryRoutes = new();

        private System.Threading.CancellationTokenSource? _settlementLoadCts;

        private bool _wasTargetMarkerVisibleBeforeWorkAreaEdit;
        private bool _wasAttackOverlayVisibleBeforeWorkAreaEdit;
        private RectLatLng? _boundsBeforeWorkAreaEdit;

        private const double MinRightPanelWidth = 280;
        private const double MaxRightPanelWidth = 520;

        private const int TechnicalMinZoom = 1;
        private const int TechnicalMaxZoom = 24;

        private bool _isUpdatingZoomLimitsUi;

        private bool _isUpdatingMapLimitsUi;

        private bool _useShortReportButtonTitles;

        private const double MinMapWidth = 500;

        public MainWindow(Services.SettingsService settingsService, Services.StatisticsService statisticsService)
        {
            // SettingsService is injected by host; fallback to new instance if null
            _settingsService = settingsService ?? new Services.SettingsService();
            _statisticsService = statisticsService;
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MapsWPF_Geocoding.log");
            var logWriter = new System.IO.StreamWriter(logPath, false) { AutoFlush = true };
            Console.SetOut(logWriter);
            Console.WriteLine($"=== MapsWPF Log Started at {DateTime.Now} ===");

            // UserAgent for OpenStreetMap

            GMapProvider.UserAgent = "Application/1.0 (Windows; U; Windows NT 10.0; uk-UA) GMap.NET/2.0";

            InitializeComponent();

            _gMapControlConfigurator.Configure(MainMap);

            _targetMarkerManager.AttachTo(MainMap);
            _targetMarkerManager.MarkerDeleted += TargetMarkerManager_MarkerDeleted;

            _workAreaEditor.AttachTo(MainMap);

            // Initialize cache statistics update timer
            _cacheStatsUpdateTimer = new DispatcherTimer();
            _cacheStatsUpdateTimer.Interval = TimeSpan.FromMilliseconds(500);
            _cacheStatsUpdateTimer.Tick += CacheStatsUpdateTimer_Tick;

            // Load startup settings (injected via constructor)

            Dispatcher.BeginInvoke(DispatcherPriority.Loaded,new Action(UpdateReportGenerationButtonTitles));

            // Apply saved AccessMode (persisted as string) and respect offline mode
            var savedModeStr = _settingsService.StartSettings.AccessMode ?? "ServerAndCache";
            if (!Enum.TryParse<AccessMode>(savedModeStr, true, out var savedMode))
            {
                savedMode = MainMap.Manager.Mode;
            }
            // If there's no internet available, force CacheOnly
            if (!NetworkInterface.GetIsNetworkAvailable() &&
                savedMode != AccessMode.CacheOnly)
            {
                MainMap.Manager.Mode = AccessMode.CacheOnly;
                MessageBox.Show("No internet connection available, going to CacheOnly mode.",
                    "MapsWPF",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }
            else
            {
                MainMap.Manager.Mode = savedMode;
            }

            // Reset counters so stats reflect fresh state after applying mode
            ResetCacheCounters();

            // Restore Window State
            if (_settingsService.StartSettings.WindowWidth.HasValue && _settingsService.StartSettings.WindowHeight.HasValue)
            {
                this.Width = _settingsService.StartSettings.WindowWidth.Value;
                this.Height = _settingsService.StartSettings.WindowHeight.Value;
            }
            if (_settingsService.StartSettings.WindowTop.HasValue && _settingsService.StartSettings.WindowLeft.HasValue)
            {
                this.Top = _settingsService.StartSettings.WindowTop.Value;
                this.Left = _settingsService.StartSettings.WindowLeft.Value;
            }
            if (_settingsService.StartSettings.WindowState == 2) // Maximized
            {
                this.WindowState = System.Windows.WindowState.Maximized;
            }

            // Map Provider

            var savedProviderName = _settingsService.StartSettings.MapProviderName;
            var savedProvider = GMapProviders.List.FirstOrDefault(p => p.Name == savedProviderName) ?? GMapProviders.GoogleHybridMap;
            MainMap.MapProvider = savedProvider;

            MainMap.Position = new PointLatLng(_settingsService.StartSettings.Lat, _settingsService.StartSettings.Lng);
            MainMap.Zoom = _settingsService.StartSettings.Zoom;

            // Load UI settings
            CheckBoxDebug.IsChecked = _settingsService.StartSettings.ShowGrid;
            CheckBoxShowCoordinates.IsChecked = _settingsService.StartSettings.ShowCoordinates;
            MainMap.ShowTileGridLines = _settingsService.StartSettings.ShowGrid;
            MainMap.ShowCoordinates = _settingsService.StartSettings.ShowCoordinates;

            // Load panel/tab state: keep coordinates expander behavior and map old 'expanded' flags to the selected tab
            ExpanderCoordinates.IsExpanded = _settingsService.StartSettings.IsCoordinatesExpanded;

            ExpanderFlightDirection.IsExpanded = _settingsService.StartSettings.IsFlightDirectionExpanded;

            // Choose which tab should be selected on startup if any of the old 'expanded' flags were set.
            int selectedTab = 0; // default to first tab (Target)
            if (_settingsService.StartSettings.IsTargetExpanded) selectedTab = 0;
            else if (_settingsService.StartSettings.IsGmapExpanded) selectedTab = 1;
            else if (_settingsService.StartSettings.IsCacheExpanded) selectedTab = 2;
            else if (_settingsService.StartSettings.IsGoExpanded) selectedTab = 3;
            else if (_settingsService.StartSettings.IsRayExpanded) selectedTab = 4;
            else if (_settingsService.StartSettings.IsMapLimitsExpanded) selectedTab = 5;

            // Restore IsExpanded flags for all expanders so their visible state persists across runs
            try
            {
                ExpanderGmap.IsExpanded = _settingsService.StartSettings.IsGmapExpanded;
                ExpanderCache.IsExpanded = _settingsService.StartSettings.IsCacheExpanded;
                ExpanderGo.IsExpanded = _settingsService.StartSettings.IsGoExpanded;
                ExpanderRay.IsExpanded = _settingsService.StartSettings.IsRayExpanded;
                ExpanderMapLimits.IsExpanded = _settingsService.StartSettings.IsMapLimitsExpanded;
            }
            catch { }

            // Prefer explicit saved SelectedRightTabIndex when present (backward-compatible)
            if (_settingsService.StartSettings.SelectedRightTabIndex.HasValue)
            {
                selectedTab = _settingsService.StartSettings.SelectedRightTabIndex.Value;
            }

            try { RightTabControl.SelectedIndex = selectedTab; } catch { }

            // Load Map Limits UI
            CheckBoxLimitMap.IsChecked = _settingsService.StartSettings.IsMapLimitsEnabled;
            if (_settingsService.StartSettings.LimitTopLeftLat.HasValue && _settingsService.StartSettings.LimitTopLeftLng.HasValue)
                TextBoxLimitTopLeft.Text = $"{_settingsService.StartSettings.LimitTopLeftLat.Value.ToString(CultureInfo.InvariantCulture)}, {_settingsService.StartSettings.LimitTopLeftLng.Value.ToString(CultureInfo.InvariantCulture)}";


            if (_settingsService.StartSettings.LimitBottomRightLat.HasValue && _settingsService.StartSettings.LimitBottomRightLng.HasValue)
                TextBoxLimitBottomRight.Text = $"{_settingsService.StartSettings.LimitBottomRightLat.Value.ToString(CultureInfo.InvariantCulture)}, {_settingsService.StartSettings.LimitBottomRightLng.Value.ToString(CultureInfo.InvariantCulture)}";

            // Show UTM representation in inputs when possible (override lat/lng display)
            // Prefer UTM display for Map Limits fields so user sees expected format on startup.
            try
            {
                if (_settingsService.StartSettings.LimitTopLeftLat.HasValue && _settingsService.StartSettings.LimitTopLeftLng.HasValue)
                {
                    if (_coordinateConverter.TryLatLngToUTM(_settingsService.StartSettings.LimitTopLeftLat.Value, _settingsService.StartSettings.LimitTopLeftLng.Value, out System.Drawing.PointF utmTL, out int zoneTL, out char bandTL))
                    {
                        TextBoxLimitTopLeft.Text = _coordinateConverter.FormatUTM(utmTL, zoneTL, bandTL);
                    }
                }

                if (_settingsService.StartSettings.LimitBottomRightLat.HasValue && _settingsService.StartSettings.LimitBottomRightLng.HasValue)
                {
                    if (_coordinateConverter.TryLatLngToUTM(_settingsService.StartSettings.LimitBottomRightLat.Value, _settingsService.StartSettings.LimitBottomRightLng.Value, out System.Drawing.PointF utmBR, out int zoneBR, out char bandBR))
                    {
                        TextBoxLimitBottomRight.Text = _coordinateConverter.FormatUTM(utmBR, zoneBR, bandBR);
                    }
                }
            }
            catch { }
            // Map Zoom Limits
            CheckBoxZoomLimits.IsChecked = _settingsService.StartSettings.IsZoomLimitsEnabled;
            TextBoxMinZoom.Text = _settingsService.StartSettings.MinZoom.ToString();
            TextBoxMaxZoom.Text = _settingsService.StartSettings.MaxZoom.ToString();

            // Apply loaded Zoom limits
            ApplyZoomLimits();

            // Initialize map bounds from settings
            UpdateBoundsOfMap();

            // If Map Limits are enabled at startup, make sure current position is inside the bounds
            if (_settingsService.StartSettings.IsMapLimitsEnabled)
            {
                EnsurePositionInsideBoundsOnEnable();
            }

            if (_settingsService.StartSettings.IsMapLimitsEnabled &&
    MainMap.BoundsOfMap.HasValue)
            {
                MainMap.ApplyBoundsOfMapToViewport();
            }

            // Force update labels

            MainMap_OnCurrentPositionChanged(MainMap.Position);

            //-- map events
            MainMap.OnPositionChanged += MainMap_OnCurrentPositionChanged;
            MainMap.OnMapZoomChanged += MainMap_OnMapZoomChanged;
            MainMap.OnTileLoadComplete += MainMap_OnTileLoadComplete;
            MainMap.OnTileLoadStart += MainMap_OnTileLoadStart;
            MainMap.OnMapTypeChanged += MainMap_OnMapTypeChanged;

            // Task 4: Enforce limits on Drag

            MainMap.MouseMove += MainMap_MouseMove;
            MainMap.MouseRightButtonDown += MainMap_MouseRightButtonDown;
            MainMap.MouseEnter += MainMap_MouseEnter;
            MainMap.Loaded += MainMap_Loaded;

            // Підписки з MainMap_Loaded (перенесені в конструктор)
            MainMap.OnPositionChanged += (p) =>
            {
                if (!MainMap.IsDragging)
                {
                    UpdateDistanceDisplay();
                }
            };

            // Зміни в налаштуваннях атаки
            _settingsService.OnAttackSettingsChanged += () => Dispatcher.Invoke(RestoreAttackSettings);
            this.Closing += MainWindow_Closing;

            // initialize WASD timer
            _wasdTimer = new DispatcherTimer();
            _wasdTimer.Interval = TimeSpan.FromMilliseconds(_wasdTickIntervalMs);
            _wasdTimer.Tick += WasdTimer_Tick;

            // Provide CoordinateFormatter
            MainMap.CoordinateFormatter = (pt) =>
            {
                try
                {
                    if (_coordinateConverter != null && _coordinateConverter.TryLatLngToUTM(pt.Lat, pt.Lng, out System.Drawing.PointF utm, out int utmZone, out char bandLetter))
                    {
                        var utmStr = _coordinateConverter.FormatUTM(utm, utmZone, bandLetter);
                        var mgrsStr = _coordinateConverter.FormatShortMGRSFromUTM(utm, utmZone, bandLetter);
                        return (utmStr, mgrsStr);
                    }
                }
                catch { }
                return (null, null);
            };

            // Map Providers setup
            var providers = GMapProviders.List.ToList();
            Func<GMapProvider, bool> isGoogle = p => p.Name.IndexOf("google", StringComparison.OrdinalIgnoreCase) >= 0;
            Func<GMapProvider, bool> isBing = p => p.Name.IndexOf("bing", StringComparison.OrdinalIgnoreCase) >= 0;
            Func<GMapProvider, bool> isOSM = p => p.Name.IndexOf("openstreet", StringComparison.OrdinalIgnoreCase) >= 0 || p.Name.IndexOf("open street", StringComparison.OrdinalIgnoreCase) >= 0 || p.Name.IndexOf("osm", StringComparison.OrdinalIgnoreCase) >= 0;
            Func<GMapProvider, bool> isChina = p => p.Name.IndexOf("china", StringComparison.OrdinalIgnoreCase) >= 0;
            Func<GMapProvider, bool> isHybrid = p => p.Name.IndexOf("hybrid", StringComparison.OrdinalIgnoreCase) >= 0;

            var google = providers.Where(isGoogle).OrderBy(p => isHybrid(p) ? 0 : (isChina(p) ? 2 : 1)).ThenBy(p => p.Name).ToList();
            var bing = providers.Where(isBing).OrderBy(p => isHybrid(p) ? 0 : (isChina(p) ? 2 : 1)).ThenBy(p => p.Name).ToList();
            var osm = providers.Where(isOSM).OrderBy(p => p.Name).ToList();
            var others = providers.Where(p => !isGoogle(p) && !isBing(p) && !isOSM(p)).OrderBy(p => p.Name).ToList();

            var ordered = new List<GMapProvider>();
            ordered.AddRange(google);
            ordered.AddRange(bing);
            ordered.AddRange(osm);
            ordered.AddRange(others);

            ComboBoxMapType.ItemsSource = ordered;
            ComboBoxMapType.DisplayMemberPath = "Name";
            ComboBoxMapType.SelectedItem = MainMap.MapProvider;

            ComboBoxMode.ItemsSource = Enum.GetValues(typeof(AccessMode));
            ComboBoxMode.SelectedItem = MainMap.Manager.Mode;

            // Initialize report template service
            try
            {
                _mapService = new Services.MapService(_coordinateConverter);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"MapService initialization failed: {ex.Message}");
            }

            CheckBoxCacheRoute.IsChecked = MainMap.Manager.UseRouteCache;
            CheckBoxGeoCache.IsChecked = MainMap.Manager.UseGeocoderCache;

            TextBoxLat.Text = MainMap.Position.Lat.ToString(CultureInfo.InvariantCulture);
            TextBoxLng.Text = MainMap.Position.Lng.ToString(CultureInfo.InvariantCulture);

            CheckBoxCurrentMarker.IsChecked = true;

            //CheckBoxDragMap.IsChecked = MainMap.CanDragMap;
            CheckBoxDragMap.IsChecked = MainMap.CanDragMap;

            // Apply saved right panel width (pixels) if present
            try
            {
                if (_settingsService?.StartSettings != null)
                {
                    var width = _settingsService.StartSettings.RightPanelWidth;

                    if (width > 50)
                    {
                        var clampedWidth = Math.Max(
                            MinRightPanelWidth,
                            Math.Min(MaxRightPanelWidth, width)
                        );

                        RightColumn.Width = new GridLength(
                            clampedWidth,
                            GridUnitType.Pixel
                        );
                    }
                }
            }
            catch { }

            _isInitializing = false;

            if (TextBoxFlightDirection != null)
            {
                _lastSavedFlightDirection = _settingsService.StartSettings.FlightDirectionCities ?? string.Empty;
                TextBoxFlightDirection.Text = _lastSavedFlightDirection;

                TextBoxFlightDirection.TextChanged += (s, e) =>
                {
                    if (_isInitializing) return;
                    bool hasChanged = !string.Equals(TextBoxFlightDirection.Text, _lastSavedFlightDirection, StringComparison.Ordinal);
                    ButtonSaveFlightDirection.IsEnabled = hasChanged;
                    ButtonCancelFlightDirection.IsEnabled = hasChanged;
                    _flightDirectionChanged = hasChanged;
                    UpdateFlightCitiesList();
                };

                UpdateFlightCitiesList();
                ListBoxFlightCities.UpdateLayout();
                RestoreSelectedFlightCities();
                _lastSavedSelectedCities = _settingsService.StartSettings.SelectedFlightCities?.ToList() ?? new List<string>();

                ListBoxFlightCities.SelectionChanged += (s, e) =>
                {
                    if (_isInitializing) return;
                    SaveSelectedFlightCities();
                };
            }

        }

        private void ReportGenerationButtonsGrid_SizeChanged(
    object sender,
    SizeChangedEventArgs e)
        {
            UpdateReportGenerationButtonTitles();
        }

        private void UpdateReportGenerationButtonTitles()
        {
            if (ButtonGenerateStartWork == null ||
                ButtonGenerateEndWork == null ||
                ButtonGenerateCombatWork == null)
            {
                return;
            }

            if (ButtonGenerateStartWork.ActualWidth <= 0 ||
                ButtonGenerateEndWork.ActualWidth <= 0 ||
                ButtonGenerateCombatWork.ActualWidth <= 0)
            {
                return;
            }

            var fullTitlesFit =
                DoesButtonTextFit(ButtonGenerateStartWork, "Початок роботи") &&
                DoesButtonTextFit(ButtonGenerateEndWork, "Кінець роботи") &&
                DoesButtonTextFit(ButtonGenerateCombatWork, "Бойова робота");

            var shouldUseShortTitles = !fullTitlesFit;

            if (_useShortReportButtonTitles == shouldUseShortTitles)
            {
                return;
            }

            _useShortReportButtonTitles = shouldUseShortTitles;

            if (_useShortReportButtonTitles)
            {
                ButtonGenerateStartWork.Content = "Початок";
                ButtonGenerateEndWork.Content = "Кінець";
                ButtonGenerateCombatWork.Content = "Бойова";
            }
            else
            {
                ButtonGenerateStartWork.Content = "Початок роботи";
                ButtonGenerateEndWork.Content = "Кінець роботи";
                ButtonGenerateCombatWork.Content = "Бойова робота";
            }

            ButtonGenerateStartWork.ToolTip = "Початок роботи";
            ButtonGenerateEndWork.ToolTip = "Кінець роботи";
            ButtonGenerateCombatWork.ToolTip = "Бойова робота";
        }

        private bool DoesButtonTextFit(Button button, string text)
        {
            if (button == null ||
                string.IsNullOrWhiteSpace(text) ||
                button.ActualWidth <= 0)
            {
                return false;
            }

            var textBlock = new TextBlock
            {
                Text = text,
                FontFamily = button.FontFamily,
                FontSize = button.FontSize,
                FontStyle = button.FontStyle,
                FontWeight = button.FontWeight,
                FontStretch = button.FontStretch
            };

            textBlock.Measure(
                new Size(
                    double.PositiveInfinity,
                    double.PositiveInfinity
                )
            );

            var requiredWidth =
                textBlock.DesiredSize.Width +
                button.Padding.Left +
                button.Padding.Right +
                14;

            return requiredWidth <= button.ActualWidth;
        }

        public IEnumerable<string> GetSelectedFlyDirections()
        {
            return ListBoxFlightCities?.SelectedItems?.Cast<string>() ?? Enumerable.Empty<string>();
        }

        private static RectLatLng CreateNormalizedBounds(
    double firstLat,
    double firstLng,
    double secondLat,
    double secondLng)
        {
            var top = Math.Max(firstLat, secondLat);
            var bottom = Math.Min(firstLat, secondLat);
            var left = Math.Min(firstLng, secondLng);
            var right = Math.Max(firstLng, secondLng);

            return RectLatLng.FromLTRB(
                left,
                top,
                right,
                bottom
            );
        }

        private void PlaceholderTag_Click(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            try
            {
                if (sender is FrameworkElement fe && fe.DataContext is string tag)
                {
                    InsertTextIntoTemplateEditor(tag);
                }
            }
            catch { }
        }

        private void InsertTextIntoTemplateEditor(string text)
        {
            try
            {
                TextBoxTemplateEditor.Focus();
                int selStart = TextBoxTemplateEditor.SelectionStart;
                int selLen = TextBoxTemplateEditor.SelectionLength;
                var current = TextBoxTemplateEditor.Text ?? string.Empty;
                TextBoxTemplateEditor.Text = current.Substring(0, selStart) + text + current.Substring(selStart + selLen);
                TextBoxTemplateEditor.SelectionStart = selStart + text.Length;
                TextBoxTemplateEditor.SelectionLength = 0;
                var be = TextBoxTemplateEditor.GetBindingExpression(System.Windows.Controls.TextBox.TextProperty);
                be?.UpdateSource();
            }
            catch { }
        }

        private void MainWindow_Closing(object sender, CancelEventArgs e)
        {
            // Save current map state
            _settingsService.StartSettings.Lat = MainMap.Position.Lat;
            _settingsService.StartSettings.Lng = MainMap.Position.Lng;
            _settingsService.StartSettings.Zoom = MainMap.Zoom;


            _settingsService.StartSettings.GoGeo = TextBoxGeo.Text;

            _settingsService.StartSettings.ShowGrid = CheckBoxDebug.IsChecked == true;
            _settingsService.StartSettings.ShowCoordinates = CheckBoxShowCoordinates.IsChecked == true;

            // Save panel/tab state: coordinates expander + which tab is selected
            _settingsService.StartSettings.IsCoordinatesExpanded = ExpanderCoordinates.IsExpanded;
            int sel = 0;
            try { sel = RightTabControl.SelectedIndex; } catch { }

            // Persist explicit selected index (preferred) for future loads
            _settingsService.StartSettings.SelectedRightTabIndex = sel;

            // Save actual expander states (respect user toggles) — don't overwrite with tab selection here
            if (ExpanderCoordinates != null) _settingsService.StartSettings.IsCoordinatesExpanded = ExpanderCoordinates.IsExpanded;
            if (ExpanderGmap != null) _settingsService.StartSettings.IsGmapExpanded = ExpanderGmap.IsExpanded;
            if (ExpanderCache != null) _settingsService.StartSettings.IsCacheExpanded = ExpanderCache.IsExpanded;
            if (ExpanderGo != null) _settingsService.StartSettings.IsGoExpanded = ExpanderGo.IsExpanded;
            if (ExpanderRay != null) _settingsService.StartSettings.IsRayExpanded = ExpanderRay.IsExpanded;
            if (ExpanderMapLimits != null) _settingsService.StartSettings.IsMapLimitsExpanded = ExpanderMapLimits.IsExpanded;

            // Save Window State
            if (this.WindowState == System.Windows.WindowState.Normal)
            {
                _settingsService.StartSettings.WindowTop = this.Top;
                _settingsService.StartSettings.WindowLeft = this.Left;
                _settingsService.StartSettings.WindowWidth = this.Width;
                _settingsService.StartSettings.WindowHeight = this.Height;
            }
            _settingsService.StartSettings.WindowState = (int)this.WindowState;

            // Zoom Limits
            _settingsService.StartSettings.IsZoomLimitsEnabled = CheckBoxZoomLimits.IsChecked == true;

            // Map Limits settings are updated in their respective event handlers/setters, 
            // but saving calls SaveStartSettings for all.
            _settingsService.StartSettings.AccessMode = MainMap.Manager.Mode.ToString();

            // Save right panel width
            try
            {
                _settingsService.StartSettings.RightPanelWidth = RightColumn.ActualWidth;
            }
            catch { }

            _settingsService.SaveStartSettings();
        }

        // Persist expander state immediately when user toggles one
        private void Expander_Toggled(object sender, RoutedEventArgs e)
        {
            // Defensive: ignore if settings service or controls are not yet initialized
            try
            {
                if (_isInitializing) return;
                if (_settingsService?.StartSettings == null) return;

                // Update only those expanders that are currently created
                if (ExpanderCoordinates != null) _settingsService.StartSettings.IsCoordinatesExpanded = ExpanderCoordinates.IsExpanded;
                if (ExpanderGmap != null) _settingsService.StartSettings.IsGmapExpanded = ExpanderGmap.IsExpanded;
                if (ExpanderCache != null) _settingsService.StartSettings.IsCacheExpanded = ExpanderCache.IsExpanded;
                if (ExpanderGo != null) _settingsService.StartSettings.IsGoExpanded = ExpanderGo.IsExpanded;
                if (ExpanderRay != null) _settingsService.StartSettings.IsRayExpanded = ExpanderRay.IsExpanded;
                if (ExpanderMapLimits != null) _settingsService.StartSettings.IsMapLimitsExpanded = ExpanderMapLimits.IsExpanded;
                if (ExpanderFlightDirection != null) _settingsService.StartSettings.IsFlightDirectionExpanded = ExpanderFlightDirection.IsExpanded;

                _settingsService.SaveStartSettings();
            }
            catch (Exception ex)
            {
                // Log for diagnostics, avoid silent swallowing
                System.Diagnostics.Debug.WriteLine($"Expander_Toggled error: {ex.Message}");
            }
        }

        private void RightTabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (_settingsService?.StartSettings == null) return;
                int sel = 0;
                try { sel = RightTabControl.SelectedIndex; } catch { }

                _settingsService.StartSettings.SelectedRightTabIndex = sel;

                // Mirror selected tab to the old 'expanded' flags so legacy behavior remains consistent
                _settingsService.StartSettings.IsTargetExpanded = sel == 0;
                _settingsService.StartSettings.IsGmapExpanded = sel == 1;
                _settingsService.StartSettings.IsCacheExpanded = sel == 2;
                _settingsService.StartSettings.IsGoExpanded = sel == 3;
                _settingsService.StartSettings.IsRayExpanded = sel == 4;
                _settingsService.StartSettings.IsMapLimitsExpanded = sel == 5;

                _settingsService.SaveStartSettings();
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"RightTabControl_SelectionChanged error: {ex.Message}");
            }
        }

        private void RestoreAttackSettings()
        {
            _settingsService.AttackSettings.PropertyChanged -= AttackSettings_PropertyChanged;

            TextBoxAttackAngle.Text = _settingsService.AttackSettings.Angle.ToString("F2");
            TextBoxRayLength.Text = _settingsService.AttackSettings.RayLength.ToString("F0");
            TextBoxSectorWidth.Text = _settingsService.AttackSettings.SectorWidth.ToString("F2");
            TextBoxRotateStep.Text = _settingsService.AttackSettings.RotateStep.ToString("F3", CultureInfo.InvariantCulture);
            TextBoxRotateShiftStep.Text = _settingsService.AttackSettings.RotateShiftStep.ToString("F3", CultureInfo.InvariantCulture);

            // Restore servo-related UI and map overlay
            try
            {
                var servoValue = (_settingsService.AttackSettings.Angle + _settingsService.AttackSettings.ServoAngleDelta + 360) % 360;
                TextBoxServoAngle.Text = servoValue.ToString("F1", CultureInfo.InvariantCulture);
                MainMap.ServoAngleDisplay = (float)servoValue;

                // Update Servo label in Coordinates expander
                LabelServoValue.Content = _settingsService.AttackSettings.IsSet || !double.IsNaN(servoValue)
                    ? servoValue.ToString("F1", CultureInfo.InvariantCulture) + "°"
                    : "-";
            }
            catch { }
            UpdateMapAttackZone();

            // Initialize angle label
            // Initialize angle, azimuth and distance labels
            try
            {
                if (_settingsService.AttackSettings.IsSet)
                {
                    LabelAngleValue.Content =
                        _settingsService.AttackSettings.Angle.ToString(
                            "F1",
                            CultureInfo.InvariantCulture
                        ) + "°";

                    UpdateDistanceDisplay();
                }
                else
                {
                    LabelAngleValue.Content = "-";

                }
            }
            catch
            {
            }

            _settingsService.AttackSettings.PropertyChanged += AttackSettings_PropertyChanged;
        }

        private void AttackSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Update angle label when attack settings change
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    if (_settingsService?.AttackSettings != null && _settingsService.AttackSettings.IsSet)
                    {
                        LabelAngleValue.Content = _settingsService.AttackSettings.Angle.ToString("F1", CultureInfo.InvariantCulture) + "°";

                        // Update Servo label as well (Angle + delta)
                        var servo = (_settingsService.AttackSettings.Angle + _settingsService.AttackSettings.ServoAngleDelta + 360) % 360;
                        LabelServoValue.Content = servo.ToString("F1", CultureInfo.InvariantCulture) + "°";

                        // Keep the input box in sync
                        if (TextBoxServoAngle != null && !TextBoxServoAngle.IsFocused)
                        {
                            TextBoxServoAngle.Text = servo.ToString("F1", CultureInfo.InvariantCulture);
                        }
                    }
                    else
                    {
                        LabelAngleValue.Content = "-";
                        LabelServoValue.Content = "-";
                    }

                    // Also refresh distance in case Lat/Lng changed
                    UpdateDistanceDisplay();
                }));
            }
            catch { }
        }

        void MainMap_MouseEnter(object sender, MouseEventArgs e)
        {
            MainMap.Focus();
        }

        public RenderTargetBitmap ToImageSource(FrameworkElement obj)
        {
            ArgumentNullException.ThrowIfNull(obj);

            var width = obj.ActualWidth;
            var height = obj.ActualHeight;

            if (width <= 0 || height <= 0)
            {
                throw new InvalidOperationException(
                    "Неможливо створити зображення: елемент ще не має фактичного розміру."
                );
            }

            var oldTransform = obj.LayoutTransform;
            var oldMargin = obj.Margin;

            try
            {
                obj.LayoutTransform = null;
                obj.Margin = new Thickness(
                    0,
                    0,
                    oldMargin.Right - oldMargin.Left,
                    oldMargin.Bottom - oldMargin.Top
                );

                var size = new Size(width, height);

                obj.Measure(size);
                obj.Arrange(new Rect(size));

                var bitmap = new RenderTargetBitmap(
                    (int)Math.Ceiling(width),
                    (int)Math.Ceiling(height),
                    96,
                    96,
                    PixelFormats.Pbgra32
                );

                bitmap.Render(obj);

                if (bitmap.CanFreeze)
                {
                    bitmap.Freeze();
                }

                return bitmap;
            }
            finally
            {
                obj.LayoutTransform = oldTransform;
                obj.Margin = oldMargin;
            }
        }

        void MainMap_Loaded(object sender, RoutedEventArgs e)
        {
            // Запобігаємо повторному виконанню
            if (_isMapInitialized)
                return;
            _isMapInitialized = true;

            // Ініціалізація стану (те, що було в Loaded, без підписок)
            MainMap.Zoom = _settingsService.StartSettings.Zoom;
            MainMap.Position = new PointLatLng(_settingsService.StartSettings.Lat, _settingsService.StartSettings.Lng);
            TextBoxLat.Text = MainMap.Position.Lat.ToString(CultureInfo.InvariantCulture);
            TextBoxLng.Text = MainMap.Position.Lng.ToString(CultureInfo.InvariantCulture);
            TextBoxGeo.Text = _settingsService.StartSettings.GoGeo;

            ValidateAndApplyZoomLimits();
            MainMap_OnCurrentPositionChanged(MainMap.Position);
            RestoreTargetPoint();

            // Цей виклик вже є в конструкторі, але він потрібен для відновлення атаки після завантаження
            RestoreAttackSettings();

        }

        private SettlementOverpassLoader GetSettlementOverpassLoader()
        {
            if (_settlementOverpassLoader == null)
            {
                _settlementOverpassLoader =
                    new SettlementOverpassLoader(TryConvertLatLngToUtmForSettlementLoader);
            }

            return _settlementOverpassLoader;
        }

        private async Task LoadKupianskBoundaryTestAsync()
        {
            const long kupianskRelationId = 3592314;
            const string kupianskName = "Куп’янськ";

            try
            {
                UpdateSettlementLoadStatusFromCurrentCache(
                    "Тестове завантаження меж Куп’янська...");

                var polygons = await GetSettlementOverpassLoader()
                    .LoadRelationPolygonsForTestAsync(
                        kupianskRelationId);

                if (polygons.Count == 0)
                {
                    UpdateSettlementLoadStatusFromCurrentCache(
                        "Межі Куп’янська не отримано.");

                    _notificationService?.Notify(
                        "Не вдалося завантажити межі Куп’янська",
                        NotificationType.Warning);

                    Debug.WriteLine(
                        "[SETTLEMENT TEST] Kupiansk: no polygons received");

                    return;
                }

                var cache = _settlementGeometryService.CurrentCache;

                cache.Settlements ??= new List<SettlementGeometryItem>();

                var kupiansk = cache.Settlements.FirstOrDefault(
                    x =>
                        string.Equals(
                            x.Name,
                            kupianskName,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            x.OsmType,
                            "node",
                            StringComparison.OrdinalIgnoreCase));

                kupiansk ??= cache.Settlements.FirstOrDefault(
                    x => string.Equals(
                        x.Name,
                        kupianskName,
                        StringComparison.OrdinalIgnoreCase));

                if (kupiansk == null)
                {
                    kupiansk = new SettlementGeometryItem
                    {
                        Name = kupianskName,
                        Place = "town",
                        OsmType = "relation",
                        OsmId = kupianskRelationId
                    };

                    cache.Settlements.Add(kupiansk);
                }

                kupiansk.Polygons ??= new List<string>();

                var existingPolygons = new HashSet<string>(
                    kupiansk.Polygons,
                    StringComparer.Ordinal);

                foreach (var polygon in polygons)
                {
                    if (existingPolygons.Add(polygon))
                    {
                        kupiansk.Polygons.Add(polygon);
                    }
                }

                kupiansk.GeometrySourceOsmType = "relation";
                kupiansk.GeometrySourceOsmId = kupianskRelationId;

                cache.Source = "overpass-relation-test";
                cache.UtmZone = 37;
                cache.UtmBand = "U";

                _settlementGeometryService.Save(cache);
                _settlementGeometryService.Load();

                UpdateSettlementLoadStatusFromCurrentCache(
                    "Межі Куп’янська завантажено.");

                _notificationService?.Notify(
                    "Межі Куп’янська завантажено",
                    NotificationType.Info);

                Debug.WriteLine(
                    $"[SETTLEMENT TEST] Kupiansk saved: " +
                    $"receivedPolygons={polygons.Count}, " +
                    $"totalPolygons={kupiansk.Polygons.Count}");
            }
            catch (TimeoutException ex)
            {
                Debug.WriteLine(
                    $"[SETTLEMENT TEST] Kupiansk timeout: {ex}");

                UpdateSettlementLoadStatusFromCurrentCache(
                    "Overpass не відповів під час завантаження Куп’янська.");

                _notificationService?.Notify(
                    "Overpass не відповів вчасно",
                    NotificationType.Warning);
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine(
                    $"[SETTLEMENT TEST] Kupiansk HTTP error: {ex}");

                UpdateSettlementLoadStatusFromCurrentCache(
                    "Помилка сервера під час завантаження Куп’янська.");

                _notificationService?.Notify(
                    "Не вдалося отримати межі Куп’янська",
                    NotificationType.Warning);
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[SETTLEMENT TEST] Kupiansk error: {ex}");

                UpdateSettlementLoadStatusFromCurrentCache(
                    "Помилка завантаження меж Куп’янська.");

                _notificationService?.Notify(
                    "Помилка завантаження меж Куп’янська",
                    NotificationType.Warning);
            }
        }

        private bool TryConvertLatLngToUtmForSettlementLoader(
    double lat,
    double lng,
    out System.Drawing.PointF utm,
    out int zone,
    out string band)
        {
            utm = System.Drawing.PointF.Empty;
            zone = 0;
            band = string.Empty;

            try
            {
                if (_mapService != null &&
                    _mapService.TryLatLngToUTM(
                        lat,
                        lng,
                        out var mapServiceUtm,
                        out var mapServiceZone,
                        out var mapServiceBand))
                {
                    utm = mapServiceUtm;
                    zone = mapServiceZone;
                    band = mapServiceBand.ToString();
                    return true;
                }

                if (_coordinateConverter.TryLatLngToUTM(
                        lat,
                        lng,
                        out var converterUtm,
                        out var converterZone,
                        out var converterBand))
                {
                    utm = converterUtm;
                    zone = converterZone;
                    band = converterBand.ToString();
                    return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SETTLEMENT UTM] Convert error: {ex}");
            }

            return false;
        }

        private static SettlementCacheBounds CreateSettlementCacheBoundsFromWorkArea(
    RectLatLng workAreaBounds,
    double paddingKm)
        {
            var lat1 = workAreaBounds.Lat;
            var lat2 = workAreaBounds.Lat - workAreaBounds.HeightLat;

            var lng1 = workAreaBounds.Lng;
            var lng2 = workAreaBounds.Lng + workAreaBounds.WidthLng;

            var top = Math.Max(lat1, lat2);
            var bottom = Math.Min(lat1, lat2);
            var left = Math.Min(lng1, lng2);
            var right = Math.Max(lng1, lng2);

            var centerLat = (top + bottom) / 2.0;

            var latPadding = paddingKm / 111.32;

            var cosLat = Math.Cos(centerLat * Math.PI / 180.0);

            if (Math.Abs(cosLat) < 0.1)
            {
                cosLat = 0.1;
            }

            var lngPadding = paddingKm / (111.32 * Math.Abs(cosLat));

            return new SettlementCacheBounds
            {
                Top = top + latPadding,
                Bottom = bottom - latPadding,
                Left = left - lngPadding,
                Right = right + lngPadding,
                PaddingKm = paddingKm
            };
        }

        private void UpdateSettlementLoadStatus(
    SettlementGeometryCache? cache,
    string stateText)
        {
            var foundCount = cache?.Settlements?.Count ?? 0;

            var loadedCount = cache?.Settlements?
                .Count(x => x.Polygons != null &&
                            x.Polygons.Any(p => !string.IsNullOrWhiteSpace(p)))
                ?? 0;

            Dispatcher.Invoke(() =>
            {
                TextSettlementFoundStatus.Text = $"Знайдено НП: {foundCount}";
                TextSettlementLoadedStatus.Text = $"Завантажено НП: {loadedCount}";
                TextSettlementLoadStateStatus.Text = stateText;
            });
        }

        private void UpdateSettlementLoadStatusFromCurrentCache(string stateText)
        {
            UpdateSettlementLoadStatus(
                _settlementGeometryService.CurrentCache,
                stateText);
        }

        private async Task RefreshSettlementGeometryCacheForWorkAreaAsync(
    RectLatLng workAreaBounds)
        {
            const double paddingKm = 10.0;

            _settlementLoadCts?.Cancel();
            _settlementLoadCts = new System.Threading.CancellationTokenSource();

            var token = _settlementLoadCts.Token;

            try
            {
                var bounds = CreateSettlementCacheBoundsFromWorkArea(
                    workAreaBounds,
                    paddingKm);

                UpdateSettlementLoadStatusFromCurrentCache(
    "Оновлення списку НП...");

                _notificationService?.Notify(
                    "Завантаження НП робочої області...",
                    NotificationType.Info);

                Debug.WriteLine(
                    $"[SETTLEMENT LOAD] Bounds: " +
                    $"Top={bounds.Top}, Bottom={bounds.Bottom}, " +
                    $"Left={bounds.Left}, Right={bounds.Right}, PaddingKm={bounds.PaddingKm}");

                var cache = await GetSettlementOverpassLoader()
     .LoadAsync(
         bounds,
         token,
       partialCache =>
{
    var count = partialCache.Settlements?.Count ?? 0;

    if (count <= 0)
    {
        Debug.WriteLine("[SETTLEMENT LOAD] Progress cache is empty, not saved");

        UpdateSettlementLoadStatusFromCurrentCache(
            "Очікування відповіді Overpass...");
        return;
    }

    _settlementGeometryService.SavePreservingExistingGeometry(partialCache);
    _settlementGeometryService.Load();

    var polygonCount = partialCache.Settlements
        .Count(x => x.Polygons != null &&
                    x.Polygons.Any(p => !string.IsNullOrWhiteSpace(p)));

    UpdateSettlementLoadStatus(
        partialCache,
        "Довантаження НП триває...");

    Debug.WriteLine(
        $"[SETTLEMENT LOAD] Progress cache saved: " +
        $"settlements={count}, withPolygons={polygonCount}");
         });

                if (token.IsCancellationRequested)
                {
                    return;
                }

                if (cache.Settlements == null || cache.Settlements.Count == 0)
                {
                    _notificationService?.Notify(
                        "НП у робочій області не знайдено",
                        NotificationType.Info);

                    UpdateSettlementLoadStatusFromCurrentCache(
                        "Не вдалося оновити НП. Використовується попередній кеш.");

                    Debug.WriteLine("[SETTLEMENT LOAD] Loaded 0 settlements, cache not overwritten");
                    return;
                }

                _settlementGeometryService.SavePreservingExistingGeometry(cache);
                _settlementGeometryService.Load();

                UpdateSettlementLoadStatus(
    cache,
    "Оновлення завершено");

                _notificationService?.Notify(
                    $"НП робочої області завантажено: {cache.Settlements.Count}",
                    NotificationType.Info);

                Debug.WriteLine(
                    $"[SETTLEMENT LOAD] Loaded settlements: {cache.Settlements.Count}");
            }
            catch (TimeoutException ex)
            {
                Debug.WriteLine($"[SETTLEMENT LOAD] Timeout: {ex}");

                UpdateSettlementLoadStatusFromCurrentCache(
    "Overpass не відповів. Використовується попередній кеш.");

                _notificationService?.Notify(
                    "Не вдалося завантажити НП: сервер не відповів вчасно",
                    NotificationType.Info);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SETTLEMENT LOAD] Error: {ex}");

                _notificationService?.Notify(
                    "Не вдалося завантажити НП робочої області",
                    NotificationType.Info);
            }
        }

        private void RestoreTargetPoint()
        {
            if (_settingsService.StartSettings.TargetLat.HasValue && _settingsService.StartSettings.TargetLng.HasValue)
            {
                var pos = new PointLatLng(
                    _settingsService.StartSettings.TargetLat.Value,
                    _settingsService.StartSettings.TargetLng.Value
                );

                _targetMarkerManager.SetMarker(pos);

                _ = UpdateTargetLocationInfoAsync(pos);
                UpdateDistanceDisplay();
            }
        }

        private void SetTargetPoint(PointLatLng position)
        {
            _targetMarkerManager.SetMarker(position);

            _settingsService.StartSettings.TargetLat = position.Lat;
            _settingsService.StartSettings.TargetLng = position.Lng;
            _settingsService.SaveStartSettings();

            _ = UpdateTargetLocationInfoAsync(position);
            UpdateDistanceDisplay();
        }

        private void TargetMarkerManager_MarkerDeleted(object? sender, EventArgs e)
        {
            MainMap.TargetDistance = -1;
            LabelDistanceValue.Content = "Distance: -";

            LabelTargetLocation.Content = "-";
            LabelTargetAddress.Content = "-";

            _settingsService.StartSettings.LastTargetLocationName = string.Empty;
            _settingsService.StartSettings.LastTargetAddress = string.Empty;
            _settingsService.StartSettings.CachedTargetLat = null;
            _settingsService.StartSettings.CachedTargetLng = null;
            _settingsService.StartSettings.TargetLat = null;
            _settingsService.StartSettings.TargetLng = null;
            _settingsService.SaveStartSettings();

            MainMap.InvalidateVisual();
        }

        private void TextBoxFlightDirection_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isInitializing) return;

            // Порівнюємо поточний текст з останнім збереженим
            bool hasChanged = !string.Equals(TextBoxFlightDirection.Text, _lastSavedFlightDirection, StringComparison.Ordinal);

            ButtonSaveFlightDirection.IsEnabled = hasChanged;
            ButtonCancelFlightDirection.IsEnabled = hasChanged;
            _flightDirectionChanged = hasChanged;

            // Оновлюємо список тегів (для попереднього перегляду)
            UpdateFlightCitiesList();
        }

        private void RestoreSelectedFlightCities()
        {
            if (ListBoxFlightCities?.Items == null) return;

            var savedSelection = _settingsService.StartSettings.SelectedFlightCities;
            if (savedSelection == null || savedSelection.Count == 0)
            {
                ListBoxFlightCities.SelectedItems.Clear();
                return;
            }

            ListBoxFlightCities.SelectedItems.Clear();
            foreach (string? item in ListBoxFlightCities.Items)
            {
                if (item != null && savedSelection.Contains(item, StringComparer.OrdinalIgnoreCase))
                    ListBoxFlightCities.SelectedItems.Add(item);
            }
        }

        private List<string> FindDuplicateCities(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
                return new List<string>();

            var cities = input.Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                              .Select(c => c.Trim())
                              .Where(c => !string.IsNullOrWhiteSpace(c))
                              .ToList();

            return cities.GroupBy(c => c, StringComparer.OrdinalIgnoreCase)
                         .Where(g => g.Count() > 1)
                         .Select(g => g.Key)
                         .ToList();
        }

        private void ButtonSaveFlightDirection_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string currentText = TextBoxFlightDirection.Text ?? string.Empty;

                // Перевірка на дублікати
                var duplicates = FindDuplicateCities(currentText);
                if (duplicates.Any())
                {
                    MessageBox.Show(
                        $"Знайдено дублікати населених пунктів:\n\n{string.Join(", ", duplicates)}\n\n" +
                        "Будь ласка, виправте список перед збереженням.",
                        "Помилка збереження",
                        MessageBoxButton.OK,
                        MessageBoxImage.Warning);
                    return; // не зберігаємо
                }

                _settingsService.StartSettings.FlightDirectionCities = currentText;

                RemoveInvalidSelectedFlightCities();

                _settingsService.SaveStartSettings();

                _lastSavedFlightDirection = currentText;
                _flightDirectionChanged = false;
                ButtonSaveFlightDirection.IsEnabled = false;
                ButtonCancelFlightDirection.IsEnabled = false;

                UpdateFlightCitiesList();
                RestoreSelectedFlightCities();

                _notificationService?.Notify("Список міст збережено.");
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Не вдалося зберегти: {ex.Message}", "Помилка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ButtonCancelFlightDirection_Click(object sender, RoutedEventArgs e)
        {
            TextBoxFlightDirection.Text = _lastSavedFlightDirection;
            // Виділення не чіпаємо – воно вже збережене окремо
        }

        void MainMap_OnMapTypeChanged(GMapProvider type)
        {
            if (_settingsService != null)
            {
                _settingsService.StartSettings.MapProviderName = type.Name;
                _settingsService.SaveStartSettings();
            }
        }

        private void UpdateDistanceDisplay()
        {
            var targetMarker = _targetMarkerManager.CurrentMarker;

            if (targetMarker != null &&
                _settingsService != null &&
                _settingsService.AttackSettings.IsSet)
            {
                var attackPoint = new PointLatLng(
                    _settingsService.AttackSettings.Lat,
                    _settingsService.AttackSettings.Lng
                );

                var distKm = MainMap.MapProvider.Projection.GetDistance(
                    attackPoint,
                    targetMarker.Position
                );

                var distM = distKm * 1000.0;

                var distKmValue = distM / 1000.0;
                LabelDistanceValue.Content = $"{distKmValue:F1} km";

                try
                {
                    LabelAngleValue.Content =
                        _settingsService.AttackSettings.Angle.ToString(
                            "F1",
                            CultureInfo.InvariantCulture
                        ) + "°";

                    var dx = targetMarker.Position.Lng - attackPoint.Lng;
                    var dy = targetMarker.Position.Lat - attackPoint.Lat;

                    var angleRad = Math.Atan2(dx, dy);
                    var angleDeg = (angleRad * (180.0 / Math.PI) + 360.0) % 360.0;

                    try
                    {
                        SetAzimuthDisplay(
                            Math.Round(angleDeg).ToString(CultureInfo.InvariantCulture) + "°"
                        );
                    }
                    catch
                    {
                    }
                }
                catch
                {
                }

                MainMap.TargetDistance = distM;
                MainMap.InvalidateVisual();
            }
        }

        // Асинхронно визначає найближений населений пункт і адресу цілі.
        //
        // Кожен новий виклик скасовує попередній. Додатковий номер версії
        // не дозволяє старому повільному запиту перезаписати дані нової цілі.
        private Task UpdateTargetLocationInfoAsync(PointLatLng pos)
        {
            try
            {
                var settings = _settingsService.StartSettings;

                ProgressBarTarget.Visibility = Visibility.Collapsed;

                if (PanelTargetInfo != null)
                {
                    PanelTargetInfo.Visibility = Visibility.Visible;
                }

                if (TryFindNearestSettlementFromGeometryCache(pos, out var settlementResult))
                {
                    var locality = settlementResult.Name;
                    var address = BuildSettlementAddressText(settlementResult);

                    LabelTargetLocation.Content = locality;
                    LabelTargetAddress.Content = address;

                    settings.CachedTargetLat = pos.Lat;
                    settings.CachedTargetLng = pos.Lng;
                    settings.LastTargetLocationName = locality;
                    settings.LastTargetAddress = address;

                    _settingsService.SaveStartSettings();

                    return Task.CompletedTask;
                }

                LabelTargetLocation.Content = "-";
                LabelTargetAddress.Content =
                    "Список НП робочої області не завантажено";

                settings.CachedTargetLat = pos.Lat;
                settings.CachedTargetLng = pos.Lng;
                settings.LastTargetLocationName = string.Empty;
                settings.LastTargetAddress = string.Empty;

                _settingsService.SaveStartSettings();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TARGET LOCALITY CACHE] {ex}");

                LabelTargetLocation.Content = "-";
                LabelTargetAddress.Content = string.Empty;

                ProgressBarTarget.Visibility = Visibility.Collapsed;

                if (PanelTargetInfo != null)
                {
                    PanelTargetInfo.Visibility = Visibility.Visible;
                }
            }

            return Task.CompletedTask;
        }

        // State for drag interception
        private System.Windows.Point _lastMousePos;
        private bool _isDragging = false;

        void MainMap_MouseMove(object sender, MouseEventArgs e)
        {
            if (MainMap.IsDragging)
            {
                return;
            }

            // Update MGRS Cursor
            var pCursor = e.GetPosition(MainMap);
            var cursorLatLng = MainMap.FromLocalToLatLng((int)pCursor.X, (int)pCursor.Y);

            // Update Map property for overlay
            MainMap.MousePositionLatLng = cursorLatLng;
            MainMap.InvalidateVisual();

            if (_coordinateConverter.TryLatLngToUTM(cursorLatLng.Lat, cursorLatLng.Lng, out var utmCursor, out var zoneCursor, out var bandCursor))
            {
                LabelMgrsCursorValue.Content = _coordinateConverter.FormatShortMGRSFromUTM(utmCursor, zoneCursor, bandCursor);
            }
            else
            {
                LabelMgrsCursorValue.Content = "-";
            }

            if (Keyboard.IsKeyDown(Key.LeftAlt))
            {
                // Rotation logic with geo-anchored attack point (only Left Alt)
                var p = e.GetPosition(MainMap);
                var mouseLatLng = MainMap.FromLocalToLatLng((int)p.X, (int)p.Y);
                var attackLatLng = new PointLatLng(_settingsService.AttackSettings.Lat, _settingsService.AttackSettings.Lng);

                // For bearing calculation we can use GMap provider or simple math
                // GMapProviders.EmptyProvider.Projection.GetBearing(attackLatLng, mouseLatLng)
                double bearing = MainMap.MapProvider.Projection.GetBearing(attackLatLng, mouseLatLng);

                // Adjust to match our angle system (0=North, 90=East)
                // GetBearing usually returns 0=North, 180=South. 
                // Our AttackAngle: 0 = Up(North), 90 = Right(East).

                if (bearing < 0) bearing += 360;

                _settingsService.AttackSettings.Angle = (float)bearing;

                UpdateMapAttackZone();
                InvalidateThrottled();
            }
        }

        private void button13_Click(object sender, RoutedEventArgs e)
        {
            MainMap.ZoomAndCenterMarkers(null);
        }

        void MainMap_OnTileLoadStart()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                // Show progress bar and hide inline stats while loading
                ProgressBar1.Visibility = Visibility.Visible;
                if (LoadingStatsGrid != null) LoadingStatsGrid.Visibility = Visibility.Collapsed;
                GroupBox3.Header = "Loading...";
            }));
        }

        void MainMap_OnTileLoadComplete(long elapsedMilliseconds)
        {
            MainMap.ElapsedMilliseconds = elapsedMilliseconds;

            // Previously we used an elapsed-based heuristic to guess tile source (RAM/DB/Net).
            // That was inaccurate (slow disk or GC could be misclassified as network).
            // Now rely on GMaps counters (updated periodically by GetCacheStats) to show accurate data.
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                // Hide progress and show stats
                ProgressBar1.Visibility = Visibility.Hidden;
                if (LoadingStatsGrid != null) LoadingStatsGrid.Visibility = Visibility.Visible;

                GroupBox3.Header = "Loading: " + MainMap.ElapsedMilliseconds + "ms";
                if (!_cacheStatsUpdateTimer.IsEnabled) _cacheStatsUpdateTimer.Start();
                else { _cacheStatsUpdateTimer.Stop(); _cacheStatsUpdateTimer.Start(); }
            }));
        }

        private void CacheStatsUpdateTimer_Tick(object sender, EventArgs e)
        {
            try
            {
                var stats = GetCacheStats();
                // Assumes new labels exist: LabelStatsRam, LabelStatsDb, LabelStatsNet, LabelStatsDbSize
                if (LabelStatsRam != null)
                {
                    LabelStatsRam.Content = $"RAM: {stats.FromRam}";

                    // Build a small inline block for DB: count and (size) — DB count in DarkOrange, size in gray.
                    var tb = new System.Windows.Controls.TextBlock();
                    tb.FontSize = 10;
                    tb.Inlines.Add(new System.Windows.Documents.Run($"DB: {stats.FromSQLite}") { Foreground = System.Windows.Media.Brushes.DarkOrange });
                    tb.Inlines.Add(new System.Windows.Documents.Run($" ({stats.FileSizeMb:F1} MB)") { Foreground = System.Windows.Media.Brushes.Gray });
                    tb.ToolTip = "Tiles loaded from SQLite (count and DB file size)";
                    LabelStatsDb.Content = tb;

                    LabelStatsNet.Content = $"Net: {stats.FromNetwork}";
                }
            }
            catch { }
        }

        private void ButtonShowKupianskBoundary_Click(
    object sender,
    RoutedEventArgs e)
        {
            const string kupianskName = "Куп’янськ";

            ClearSettlementBoundaryRoutes();

            if (_mapService == null)
            {
                _notificationService?.Notify(
                    "Сервіс карти не ініціалізовано",
                    NotificationType.Warning);

                return;
            }

            var cache = _settlementGeometryService.CurrentCache;

            var kupiansk = cache.Settlements?
                .FirstOrDefault(x =>
                    string.Equals(
                        x.Name,
                        kupianskName,
                        StringComparison.OrdinalIgnoreCase) &&
                    x.Polygons != null &&
                    x.Polygons.Any(p =>
                        !string.IsNullOrWhiteSpace(p)));

            if (kupiansk == null)
            {
                _notificationService?.Notify(
                    "У локальному кеші немає меж Куп’янська",
                    NotificationType.Warning);

                TextSettlementLoadStateStatus.Text =
                    "Межі Куп’янська не завантажені.";

                return;
            }

            var allMapPoints = new List<PointLatLng>();

            foreach (var polygonLine in kupiansk.Polygons)
            {
                var utmPoints =
                    SettlementGeometryService.ParsePolygonLine(polygonLine);

                if (utmPoints.Count < 3)
                {
                    continue;
                }

                var mapPoints = new List<PointLatLng>();

                foreach (var utmPoint in utmPoints)
                {
                    if (!_mapService.TryUTMToLatLng(
                            utmPoint,
                            out var lat,
                            out var lng))
                    {
                        continue;
                    }

                    mapPoints.Add(new PointLatLng(lat, lng));
                }

                if (mapPoints.Count < 3)
                {
                    continue;
                }

                var firstPoint = mapPoints[0];
                var lastPoint = mapPoints[mapPoints.Count - 1];

                if (firstPoint.Lat != lastPoint.Lat ||
                    firstPoint.Lng != lastPoint.Lng)
                {
                    mapPoints.Add(firstPoint);
                }

                var route = new GMapRoute(mapPoints)
                {
                    Shape = new System.Windows.Shapes.Path
                    {
                        Stroke = Brushes.Red,
                        StrokeThickness = 3,
                        StrokeLineJoin = PenLineJoin.Round
                    },
                    ZIndex = 1000
                };

                _settlementBoundaryRoutes.Add(route);
                MainMap.Markers.Add(route);

                allMapPoints.AddRange(mapPoints);
            }

            if (_settlementBoundaryRoutes.Count == 0)
            {
                _notificationService?.Notify(
                    "Не вдалося перетворити точки меж Куп’янська",
                    NotificationType.Warning);

                TextSettlementLoadStateStatus.Text =
                    "Полігон Куп’янська містить некоректні точки.";

                return;
            }

            ZoomToSettlementBoundary(allMapPoints);

            ButtonClearSettlementBoundaries.IsEnabled = true;

            TextSettlementLoadStateStatus.Text =
                $"Показано межі Куп’янська: " +
                $"{_settlementBoundaryRoutes.Count} контур.";

            Debug.WriteLine(
                $"[SETTLEMENT DISPLAY] Kupiansk: " +
                $"routes={_settlementBoundaryRoutes.Count}, " +
                $"points={allMapPoints.Count}");
        }

        private void ButtonClearSettlementBoundaries_Click(
    object sender,
    RoutedEventArgs e)
        {
            ClearSettlementBoundaryRoutes();

            TextSettlementLoadStateStatus.Text =
                "Межі населеного пункту прибрано.";
        }

        private void ClearSettlementBoundaryRoutes()
        {
            foreach (var route in _settlementBoundaryRoutes)
            {
                MainMap.Markers.Remove(route);
            }

            _settlementBoundaryRoutes.Clear();

            ButtonClearSettlementBoundaries.IsEnabled = false;

            Debug.WriteLine(
                "[SETTLEMENT DISPLAY] Boundary routes cleared");
        }

        private void ZoomToSettlementBoundary(
    List<PointLatLng> points)
        {
            if (points == null || points.Count == 0)
            {
                return;
            }

            var top = points.Max(p => p.Lat);
            var bottom = points.Min(p => p.Lat);
            var left = points.Min(p => p.Lng);
            var right = points.Max(p => p.Lng);

            var bounds = RectLatLng.FromLTRB(
                left,
                top,
                right,
                bottom);

            MainMap.SetZoomToFitRect(bounds);
        }

        private CacheStats GetCacheStats()
        {
            long memSize = 0;
            try
            {
                // Try to get actual MemoryCache size from the manager instance
                if (GMaps.Instance.MemoryCache != null)
                    memSize = (long)GMaps.Instance.MemoryCache.Size;
            }
            catch { }

            double dbMb = 0;
            string dbPath = "";
            try
            {
                // Check both MainMap.Manager (which is GMaps.Instance) and explicit cast
                var primary = GMaps.Instance.PrimaryCache as GMap.NET.CacheProviders.SQLitePureImageCache;
                if (primary == null)

                    primary = GMaps.Instance.SecondaryCache as GMap.NET.CacheProviders.SQLitePureImageCache;

                if (primary != null)
                {
                    dbPath = primary.CacheLocation;
                    // If path is a directory, find the largest .gmdb file (likely the main cache)
                    if (System.IO.Directory.Exists(dbPath))
                    {
                        try

                        {
                            var files = System.IO.Directory.GetFiles(dbPath, "*.gmdb", System.IO.SearchOption.AllDirectories);
                            if (files.Length > 0)
                            {
                                // Assume largest file is the main DB
                                dbPath = files.OrderByDescending(f => new System.IO.FileInfo(f).Length).First();
                            }
                        }
                        catch { }
                    }

                    if (System.IO.File.Exists(dbPath))
                        dbMb = new System.IO.FileInfo(dbPath).Length / 1024.0 / 1024.0;
                }
            }
            catch { }

            return new MapsWPF.CacheStats
            {
                DbPath = dbPath,
                FileSizeMb = dbMb,
                LastModified = System.IO.File.Exists(dbPath) ? System.IO.File.GetLastWriteTime(dbPath) : DateTime.MinValue,
                TileCount = 0, // GMap doesn't expose tile count in DB cheaply
                // Use accurate counters from core instead of heuristic timing
                FromRam = GMaps.Instance.TilesFromMemoryCache,
                FromSQLite = GMaps.Instance.TilesFromSQLiteCache,
                FromNetwork = GMaps.Instance.TilesFromNetwork,
                Mode = GMaps.Instance.Mode.ToString(),
                UseMemoryCache = GMaps.Instance.UseMemoryCache,
                CacheOnIdleRead = GMaps.Instance.CacheOnIdleRead,
                BoostCacheEngine = GMaps.Instance.BoostCacheEngine
            };
        }

        // Updates BoundsOfMap in the map control based on settings
        // Переносить збережену робочу область у GMapControl.
        // Метод лише встановлює BoundsOfMap. Корекцію zoom і viewport
        // виконує сам GMapControl через ApplyBoundsOfMapToViewport().
        private void UpdateBoundsOfMap()
        {
            var savedBounds = GetSavedWorkAreaBounds();

            if (_settingsService.StartSettings.IsMapLimitsEnabled &&
                savedBounds.HasValue)
            {
                MainMap.BoundsOfMap = savedBounds.Value;
            }
            else
            {
                MainMap.BoundsOfMap = null;
            }

            // Після збереження робоча область не повинна малюватися.
            MainMap.WorkAreaLimitDebugBounds = null;
            MainMap.IsWorkAreaLimitDebugVisible = false;

            MainMap.InvalidateVisual(true);
        }

        private void EnsurePositionInsideBoundsOnEnable()
        {
            if (!_settingsService.StartSettings.IsMapLimitsEnabled || MainMap.BoundsOfMap == null)
                return;

            var bounds = MainMap.BoundsOfMap.Value;
            var point = MainMap.Position;

            if (bounds.Contains(point)) return;

            // Basic clamping
            var clampedLat = Math.Max(bounds.Bottom, Math.Min(bounds.Top, point.Lat));
            var clampedLng = Math.Max(bounds.Left, Math.Min(bounds.Right, point.Lng));

            // Epsilon: small offset inside bounds — scale with bounds size but with a reasonable minimum
            double epsLat = Math.Max(1e-5, bounds.HeightLat * 0.001); // ~0.00001 degrees or fraction of height
            double epsLng = Math.Max(1e-5, bounds.WidthLng * 0.001);

            double targetLat = clampedLat;
            double targetLng = clampedLng;

            if (clampedLat == bounds.Top) targetLat = bounds.Top - epsLat;
            else if (clampedLat == bounds.Bottom) targetLat = bounds.Bottom + epsLat;

            if (clampedLng == bounds.Left) targetLng = bounds.Left + epsLng;
            else if (clampedLng == bounds.Right) targetLng = bounds.Right - epsLng;

            // Final safety: ensure target is inside bounds
            targetLat = Math.Max(bounds.Bottom, Math.Min(bounds.Top, targetLat));
            targetLng = Math.Max(bounds.Left, Math.Min(bounds.Right, targetLng));

            MainMap.Position = new PointLatLng(targetLat, targetLng);
        }

        void MainMap_OnCurrentPositionChanged(PointLatLng point)
        {

            LabelLatValue.Content = point.Lat.ToString("F8", CultureInfo.InvariantCulture);
            LabelLngValue.Content = point.Lng.ToString("F8", CultureInfo.InvariantCulture);

            if (_coordinateConverter.TryLatLngToUTM(point.Lat, point.Lng, out System.Drawing.PointF utm, out int utmZone, out char bandLetter))
            {
                _lastUtmZone = utmZone;
                _lastUtmBand = bandLetter;

                // Use a consistent formatter (invariant culture) with E/N labels
                LabelUTMValue.Content = _coordinateConverter.FormatUTM(utm, utmZone, bandLetter);
                LabelMGRSValue.Content = _coordinateConverter.FormatShortMGRSFromUTM(utm, utmZone, bandLetter);
            }
            else
            {
                LabelUTMValue.Content = "-";
                LabelMGRSValue.Content = "-";
            }

            LabelZoomValue.Content = MainMap.Zoom.ToString("F1", CultureInfo.InvariantCulture);
        }

        void MainMap_OnMapZoomChanged()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                LabelZoomValue.Content = MainMap.Zoom.ToString("F1", CultureInfo.InvariantCulture);
            }));
        }

        //void MainMap_MouseWheel(object sender, MouseWheelEventArgs e)
        //{
        //    Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
        //    {
        //        LabelZoomValue.Content = MainMap.Zoom.ToString("F1", CultureInfo.InvariantCulture);
        //    }));
        //}

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            MainMap.ReloadMap();
        }

        private void checkBoxCurrentMarker_Checked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            _targetMarkerManager.ShowMarker();
        }

        private void checkBoxCurrentMarker_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isInitializing)
            {
                return;
            }

            _targetMarkerManager.HideMarker();
        }

        private void checkBoxDragMap_Checked(object sender, RoutedEventArgs e)
        {
            MainMap.CanDragMap = true;
        }

        private void checkBoxDragMap_Unchecked(object sender, RoutedEventArgs e)
        {
            MainMap.CanDragMap = false;
        }
        private void button2_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                double lat = double.Parse(TextBoxLat.Text, CultureInfo.InvariantCulture);
                double lng = double.Parse(TextBoxLng.Text, CultureInfo.InvariantCulture);
                MainMap.Position = new PointLatLng(lat, lng);
            }
            catch (Exception ex)
            {
                MessageBox.Show("incorrect coordinate format: " + ex.Message);
            }
        }

        private void textBoxGeo_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SearchByKeywords();
            }
        }

        private void buttonSearch_Click(object sender, RoutedEventArgs e)
        {
            SearchByKeywords();
        }

        // Updated Search Method using Nominatim
        private async void SearchByKeywords()
        {
            string query = TextBoxGeo.Text;
            if (string.IsNullOrWhiteSpace(query)) return;

            try
            {
                // Task 2: City Search using Nominatim
                string url = $"https://nominatim.openstreetmap.org/search?q={Uri.EscapeDataString(query)}&format=json&limit=1";

                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.UserAgent.ParseAdd("MapsWPF/1.0");
                    var response = await _httpClient.SendAsync(request);
                    if (response.IsSuccessStatusCode)
                    {
                        using var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
                        if (doc.RootElement.ValueKind == JsonValueKind.Array && doc.RootElement.GetArrayLength() > 0)
                        {
                            var element = doc.RootElement[0];
                            double lat = double.Parse(element.GetProperty("lat").GetString(), CultureInfo.InvariantCulture);
                            double lon = double.Parse(element.GetProperty("lon").GetString(), CultureInfo.InvariantCulture);


                            MainMap.Position = new PointLatLng(lat, lon);

                            // Automatically place marker if there isn't one? user didn't request that. 
                            // But usually Go means Go there.

                            if (_targetMarkerManager.CurrentMarker != null)
                            {
                                SetTargetPoint(MainMap.Position);
                            }
                        }
                        else
                        {
                            // Fallback to existing GMap behavior if Nominatim finds nothing or fails? 
                            // Or just tell user.
                            // The user specifically asked to fix "Bad Request" by using API.
                            // Let's try default if Nominatim empty, just in case.
                            var status = MainMap.SetPositionByKeywords(query);
                            if (status != GeoCoderStatusCode.OK)
                            {
                                MessageBox.Show($"City '{query}' not found (Nominatim & GMap).", "Search", MessageBoxButton.OK, MessageBoxImage.Information);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Search error: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void czuZoomUp_Click(object sender, RoutedEventArgs e)
        {
            MainMap.Zoom = MainMap.Zoom + 1;
        }

        private void czuZoomDown_Click(object sender, RoutedEventArgs e)
        {
            MainMap.Zoom = (MainMap.Zoom + 0.99) - 1;
        }

        private void button3_Click(object sender, RoutedEventArgs e)
        {
            // Prefetch logic preserved
            var area = MainMap.SelectedArea;
            if (!area.IsEmpty)
            {
                for (int i = (int)MainMap.Zoom; i <= MainMap.MaxZoom; i++)
                {
                    if (MessageBox.Show("Ready ripp at Zoom = " + i + " ?", "GMap.NET", MessageBoxButton.YesNoCancel) == MessageBoxResult.Yes)
                    {
                        var obj = new TilePrefetcher();
                        obj.Owner = this;
                        obj.ShowCompleteMessage = true;
                        obj.Start(area, i, MainMap.MapProvider, 100);
                    }
                    else break;
                }
            }
            else
            {
                MessageBox.Show("Select map area holding ALT", "GMap.NET", MessageBoxButton.OK, MessageBoxImage.Exclamation);
            }
        }

        // Settings Manager & Attack Ray Logic

        private Services.SettingsService _settingsService;
        private DateTime _lastClickTime = DateTime.MinValue;
        private int _clickCount = 0;
        private const int DoubleClickMaxMs = 500;
        private DispatcherTimer _invalidateTimer;
        private bool _needsInvalidate = false;
        private DateTime _lastInvalidateTime = DateTime.MinValue;
        private const int InvalidateThrottleMs = 16;
        private DispatcherTimer _wasdTimer;
        private readonly HashSet<Key> _wasdHeld = new HashSet<Key>();
        private int _wasdTickIntervalMs = 16;

        private int _wasdPanPxPerTick = 20;

        private double _wasdAccumX = 0.0;
        private double _wasdAccumY = 0.0;

        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // When a text input (TextBox or editable ComboBox) has keyboard focus, let the key press go to that control
            var focused = Keyboard.FocusedElement;
            if (focused is System.Windows.Controls.TextBox) return;
            if (focused is System.Windows.Controls.ComboBox cb && cb.IsEditable && cb.IsKeyboardFocusWithin) return;

            if (e.Key == Key.Q) { MainMap.Bearing++; e.Handled = true; }
            else if (e.Key == Key.E) { MainMap.Bearing--; e.Handled = true; }
            else if (e.Key == Key.Left && MainMap.IsFocused && !Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.LeftAlt))
            {
                float step = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ? _settingsService.AttackSettings.RotateShiftStep : _settingsService.AttackSettings.RotateStep;
                var newAngle = _settingsService.AttackSettings.Angle - step;
                if (newAngle < 0) newAngle += 360f;
                _settingsService.AttackSettings.Angle = newAngle;
                TextBoxAttackAngle.Text = _settingsService.AttackSettings.Angle.ToString("F2");
                UpdateMapAttackZone();
                InvalidateThrottled();
                e.Handled = true;
            }
            else if (e.Key == Key.Right && MainMap.IsFocused && !Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.LeftAlt))
            {
                float step = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ? _settingsService.AttackSettings.RotateShiftStep : _settingsService.AttackSettings.RotateStep;
                var newAngle = _settingsService.AttackSettings.Angle + step;
                if (newAngle >= 360) newAngle -= 360f;
                _settingsService.AttackSettings.Angle = newAngle;
                TextBoxAttackAngle.Text = _settingsService.AttackSettings.Angle.ToString("F2");
                UpdateMapAttackZone();
                InvalidateThrottled();
                e.Handled = true;
            }
            else if (e.Key == Key.Up && MainMap.IsFocused && !Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.LeftAlt))
            {
                float step = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ? _settingsService.AttackSettings.RotateShiftStep : _settingsService.AttackSettings.RotateStep;
                var newWidth = _settingsService.AttackSettings.SectorWidth + step;
                if (newWidth > 180f) newWidth = 180f;
                _settingsService.AttackSettings.SectorWidth = newWidth;
                TextBoxSectorWidth.Text = _settingsService.AttackSettings.SectorWidth.ToString("F2");
                UpdateMapAttackZone();
                InvalidateThrottled();
                e.Handled = true;
            }
            else if (e.Key == Key.Down && MainMap.IsFocused && !Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.LeftAlt))
            {
                float step = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ? _settingsService.AttackSettings.RotateShiftStep : _settingsService.AttackSettings.RotateStep;
                var newWidth = _settingsService.AttackSettings.SectorWidth - step;
                if (newWidth < 5f) newWidth = 5f;
                _settingsService.AttackSettings.SectorWidth = newWidth;
                TextBoxSectorWidth.Text = _settingsService.AttackSettings.SectorWidth.ToString("F2");
                UpdateMapAttackZone();
                InvalidateThrottled();
                e.Handled = true;
            }
            else if ((e.Key == Key.W || e.Key == Key.A || e.Key == Key.S || e.Key == Key.D) && MainMap.IsFocused && !Keyboard.IsKeyDown(Key.LeftCtrl) && !Keyboard.IsKeyDown(Key.LeftAlt))
            {
                if (!_wasdHeld.Contains(e.Key)) _wasdHeld.Add(e.Key);
                if (!_wasdTimer.IsEnabled) { _wasdAccumX = 0.0; _wasdAccumY = 0.0; _wasdTimer.Start(); }
                e.Handled = true;
            }
        }

        private void MainMap_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (_workAreaEditor.IsEditing)
            {
                e.Handled = true;
                return;
            }

            var mousePosition = e.GetPosition(MainMap);
            var mapPosition = MainMap.FromLocalToLatLng(
                (int)mousePosition.X,
                (int)mousePosition.Y
            );

            bool isCtrlPressed =
                Keyboard.IsKeyDown(Key.LeftCtrl) ||
                Keyboard.IsKeyDown(Key.RightCtrl);

            bool isShiftPressed =
                Keyboard.IsKeyDown(Key.LeftShift) ||
                Keyboard.IsKeyDown(Key.RightShift);

            bool isAltPressed =
                Keyboard.IsKeyDown(Key.LeftAlt) ||
                Keyboard.IsKeyDown(Key.RightAlt);

            var now = DateTime.Now;

            if ((now - _lastClickTime).TotalMilliseconds <= DoubleClickMaxMs)
            {
                _clickCount++;
            }
            else
            {
                _clickCount = 1;
            }

            _lastClickTime = now;

            if (_clickCount >= 2)
            {
                _clickCount = 0;

                if (_settingsService.AttackSettings.IsSet)
                {
                    var attackPoint = new PointLatLng(
                        _settingsService.AttackSettings.Lat,
                        _settingsService.AttackSettings.Lng
                    );

                    var attackPointLocal = MainMap.FromLatLngToLocal(attackPoint);
                    var clickPointLocal = new GMap.NET.GPoint(
                        (long)mousePosition.X,
                        (long)mousePosition.Y
                    );

                    var distance = Math.Sqrt(
                        Math.Pow(attackPointLocal.X - clickPointLocal.X, 2) +
                        Math.Pow(attackPointLocal.Y - clickPointLocal.Y, 2)
                    );

                    if (distance < 30)
                    {
                        _settingsService.AttackSettings.IsSet = false;

                        UpdateMapAttackZone();
                        UpdateDistanceDisplay();

                        MainMap.InvalidateVisual();
                        e.Handled = true;
                        return;
                    }
                }

                if (isCtrlPressed && isShiftPressed)
                {
                    _settingsService.AttackSettings.Lat = mapPosition.Lat;
                    _settingsService.AttackSettings.Lng = mapPosition.Lng;
                    _settingsService.AttackSettings.IsSet = true;

                    UpdateMapAttackZone();
                    UpdateDistanceDisplay();

                    MainMap.InvalidateVisual();
                    e.Handled = true;
                    return;
                }

                return;
            }

            if (!isCtrlPressed && !isShiftPressed && !isAltPressed)
            {
                SetTargetPoint(mapPosition);
                e.Handled = true;
            }
        }

        private int? GetRequiredMaxZoomForActiveBounds()
        {
            if (!_settingsService.StartSettings.IsMapLimitsEnabled ||
                !MainMap.BoundsOfMap.HasValue ||
                MainMap.BoundsOfMap.Value.IsEmpty ||
                MainMap.ActualWidth <= 0 ||
                MainMap.ActualHeight <= 0)
            {
                return null;
            }

            return MainMap.GetMinimumCompatibleMaxZoom();
        }

        private void TextBoxAttackAngle_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (float.TryParse(TextBoxAttackAngle.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float angle))
            {
                _settingsService.AttackSettings.Angle = angle % 360;
                UpdateMapAttackZone();
                InvalidateThrottled();

                // Update servo display when base angle changes
                try
                {
                    var display = (float)((_settingsService.AttackSettings.Angle + _settingsService.AttackSettings.ServoAngleDelta + 360) % 360);
                    MainMap.ServoAngleDisplay = display;
                    MainMap.InvalidateVisual();
                }
                catch { }
            }
        }

        private void TextBoxRayLength_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (double.TryParse(TextBoxRayLength.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double length))
            {
                _settingsService.AttackSettings.RayLength = Math.Max(0, length);
                UpdateMapAttackZone();
                InvalidateThrottled();
            }
        }

        private void TextBoxSectorWidth_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (float.TryParse(TextBoxSectorWidth.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float width))
            {
                _settingsService.AttackSettings.SectorWidth = Math.Clamp(width, 5f, 180f);
                UpdateMapAttackZone();
                InvalidateThrottled();
            }
        }

        private List<string> ParseFlightCities(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return new List<string>();
            }

            return input
                .Split(new[] { '\r', '\n', ',' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(c => c.Trim())
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }

        private void ButtonSetServo_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (double.TryParse(TextBoxServoAngle.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double servoAngle))
                {
                    // Compute difference: servo - current attack angle
                    double currentAngle = _settingsService.AttackSettings.Angle;
                    double delta = servoAngle - currentAngle;

                    // Save delta (this triggers settings save)
                    _settingsService.AttackSettings.ServoAngleDelta = delta;

                    // Compute displayed servo angle: existing Angle plus this difference
                    float displayAngle = (float)((currentAngle + delta + 360) % 360);
                    MainMap.ServoAngleDisplay = displayAngle;

                    // Update input box to reflect stored servo (angle + delta)
                    TextBoxServoAngle.Text = displayAngle.ToString("F1", CultureInfo.InvariantCulture);

                    // Update Servo label in Coordinates expander
                    LabelServoValue.Content = displayAngle.ToString("F1", CultureInfo.InvariantCulture) + "°";

                    // Redraw overlay
                    MainMap.InvalidateVisual();
                }
            }
            catch { }
        }

        private void TextBoxRotateStep_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (float.TryParse(TextBoxRotateStep.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                _settingsService.AttackSettings.RotateStep = Math.Max(0f, val);
        }

        private void TextBoxRotateShiftStep_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (float.TryParse(TextBoxRotateShiftStep.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                _settingsService.AttackSettings.RotateShiftStep = Math.Max(0f, val);
        }

        private void UpdateMapAttackZone()
        {
            if (_settingsService.AttackSettings.IsSet)
            {
                MainMap.AttackPoint = new PointLatLng(_settingsService.AttackSettings.Lat, _settingsService.AttackSettings.Lng);
                MainMap.IsAttackPointSet = true;
            }
            else
            {
                MainMap.IsAttackPointSet = false;
            }


            MainMap.AttackAngle = _settingsService.AttackSettings.Angle;
            MainMap.AttackRayLengthMeters = _settingsService.AttackSettings.RayLength;
            MainMap.AttackSectorRadiusMeters = _settingsService.AttackSettings.RayLength; // Merged
            MainMap.AttackSectorWidth = _settingsService.AttackSettings.SectorWidth;
        }

        private void InvalidateThrottled()
        {
            var now = DateTime.Now;
            var timeSinceLastInvalidate = (now - _lastInvalidateTime).TotalMilliseconds;

            if (timeSinceLastInvalidate >= InvalidateThrottleMs)
            {
                MainMap.InvalidateVisual(true);
                _lastInvalidateTime = now;
                _needsInvalidate = false;
            }
            else
            {
                _needsInvalidate = true;
                if (_invalidateTimer == null)
                {
                    _invalidateTimer = new DispatcherTimer();
                    _invalidateTimer.Interval = TimeSpan.FromMilliseconds(InvalidateThrottleMs);
                    _invalidateTimer.Tick += (s, e) =>
                    {
                        if (_needsInvalidate)
                        {
                            MainMap.InvalidateVisual(true);
                            _lastInvalidateTime = DateTime.Now;
                            _needsInvalidate = false;
                        }
                        _invalidateTimer.Stop();
                    };
                }
                if (!_invalidateTimer.IsEnabled) _invalidateTimer.Start();
            }
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            if (MainMap.IsFocused)
            {
                if (e.Key == Key.Add) czuZoomUp_Click(null, null);
                else if (e.Key == Key.Subtract) czuZoomDown_Click(null, null);
            }

            if (e.Key == Key.W || e.Key == Key.A || e.Key == Key.S || e.Key == Key.D)
            {
                if (_wasdHeld.Contains(e.Key)) _wasdHeld.Remove(e.Key);
                if (_wasdHeld.Count == 0 && _wasdTimer != null && _wasdTimer.IsEnabled) _wasdTimer.Stop();
            }
        }

        private void WasdTimer_Tick(object? sender, EventArgs e)
        {
            if (!_wasdHeld.Any()) { _wasdTimer.Stop(); return; }

            int perTick = _wasdPanPxPerTick * (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ? 3 : 1);
            double vx = 0.0, vy = 0.0;
            foreach (var k in _wasdHeld)
            {
                switch (k) { case Key.W: vy -= 1.0; break; case Key.S: vy += 1.0; break; case Key.A: vx -= 1.0; break; case Key.D: vx += 1.0; break; }
            }

            double screenDx = 0.0, screenDy = 0.0;
            if (vx < 0) screenDx += perTick; if (vx > 0) screenDx -= perTick;
            if (vy < 0) screenDy += perTick; if (vy > 0) screenDy -= perTick;

            if (screenDx != 0 || screenDy != 0)
            {
                try
                {
                    _wasdAccumX += screenDx; _wasdAccumY += screenDy;
                    int iDx = (int)Math.Round(_wasdAccumX);
                    int iDy = (int)Math.Round(_wasdAccumY);

                    if (iDx != 0 || iDy != 0)
                    {
                        MainMap.Offset(iDx, iDy);

                        _wasdAccumX -= iDx; _wasdAccumY -= iDy;
                    }
                }
                catch (Exception ex) { Debug.WriteLine("WasdTimer pan failed: " + ex.Message); }
            }
        }

        private void Label_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Label lbl)
            {
                string text = lbl.Content?.ToString() ?? string.Empty;
                try
                {
                    if (_clipboardService != null && _clipboardService.TrySetText(text))
                    {
                        _notificationService?.Notify("Скопійовано");
                    }
                }
                catch { }
            }
        }

        // Map Limits Event Handlers
        private void CheckBoxLimitMap_Checked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingMapLimitsUi) return;

            _settingsService.StartSettings.IsMapLimitsEnabled = true;

            UpdateBoundsOfMap();
            EnsureZoomLimitsCompatibleWithBounds();
            EnsurePositionInsideBoundsOnEnable();

            if (MainMap.BoundsOfMap.HasValue)
            {
                MainMap.ApplyBoundsOfMapToViewport();
            }
        }

        private void CheckBoxLimitMap_Unchecked(object sender, RoutedEventArgs e)
        {
            if (_isUpdatingMapLimitsUi) return;

            _settingsService.StartSettings.IsMapLimitsEnabled = false;
            UpdateBoundsOfMap();

        }

        private void ButtonEditWorkArea_Click(object sender, RoutedEventArgs e)
        {
            EnterWorkAreaEditMode();
        }

        private void ButtonCancelWorkArea_Click(object sender, RoutedEventArgs e)
        {
            _workAreaEditor.CancelEdit();

            RestoreMapAfterWorkAreaEdit(restoreOldBounds: true);

            ButtonEditWorkArea.IsEnabled = true;
            ButtonSaveWorkArea.Visibility = Visibility.Collapsed;
            ButtonCancelWorkArea.Visibility = Visibility.Collapsed;
        }

        private async void ButtonSaveWorkArea_Click(object sender, RoutedEventArgs e)
        {
            if (!_workAreaEditor.DraftBounds.HasValue)
            {
                MessageBox.Show(
                    "Спочатку намалюй робочу область на карті.",
                    "Робоча область",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information
                );

                return;
            }

            var savedBounds = _workAreaEditor.DraftBounds.Value;

            SaveWorkAreaBounds(savedBounds);

            // Тимчасово вимкнено на час тесту relation Куп’янська.
            // _ = RefreshSettlementGeometryCacheForWorkAreaAsync(savedBounds);

            _workAreaEditor.CompleteEdit();

            RestoreMapAfterWorkAreaEdit(restoreOldBounds: false);

            MainMap.ApplyBoundsOfMapToViewport();

            ButtonEditWorkArea.IsEnabled = true;
            ButtonSaveWorkArea.Visibility = Visibility.Collapsed;
            ButtonCancelWorkArea.Visibility = Visibility.Collapsed;
        }

        private RectLatLng? GetSavedWorkAreaBounds()
        {
            if (!_settingsService.StartSettings.LimitTopLeftLat.HasValue ||
                !_settingsService.StartSettings.LimitTopLeftLng.HasValue ||
                !_settingsService.StartSettings.LimitBottomRightLat.HasValue ||
                !_settingsService.StartSettings.LimitBottomRightLng.HasValue)
            {
                return null;
            }

            return CreateNormalizedBounds(
                _settingsService.StartSettings.LimitTopLeftLat.Value,
                _settingsService.StartSettings.LimitTopLeftLng.Value,
                _settingsService.StartSettings.LimitBottomRightLat.Value,
                _settingsService.StartSettings.LimitBottomRightLng.Value
            );
        }

        private void EnterWorkAreaEditMode()
        {
            _wasTargetMarkerVisibleBeforeWorkAreaEdit =
                CheckBoxCurrentMarker.IsChecked == true &&
                _targetMarkerManager.CurrentMarker != null;

            _wasAttackOverlayVisibleBeforeWorkAreaEdit = MainMap.IsAttackPointSet;
            _boundsBeforeWorkAreaEdit = MainMap.BoundsOfMap ?? GetSavedWorkAreaBounds();

            if (_wasTargetMarkerVisibleBeforeWorkAreaEdit)
            {
                _targetMarkerManager.HideMarker();
            }

            MainMap.IsAttackPointSet = false;
            MainMap.BoundsOfMap = null;

            MainMap.InvalidateVisual();

            _workAreaEditor.StartEdit();

            if (_boundsBeforeWorkAreaEdit.HasValue)
            {
                _workAreaEditor.LoadDraftBounds(_boundsBeforeWorkAreaEdit.Value);
            }

            ButtonEditWorkArea.IsEnabled = false;
            ButtonSaveWorkArea.Visibility = Visibility.Visible;
            ButtonCancelWorkArea.Visibility = Visibility.Visible;
        }

        private void RestoreMapAfterWorkAreaEdit(bool restoreOldBounds)
        {
            if (restoreOldBounds)
            {
                MainMap.BoundsOfMap = _boundsBeforeWorkAreaEdit;
            }

            if (_wasAttackOverlayVisibleBeforeWorkAreaEdit)
            {
                UpdateMapAttackZone();
            }
            else
            {
                MainMap.IsAttackPointSet = false;
            }

            if (_wasTargetMarkerVisibleBeforeWorkAreaEdit &&
                CheckBoxCurrentMarker.IsChecked == true)
            {
                _targetMarkerManager.ShowMarker();
            }

            UpdateDistanceDisplay();
            MainMap.InvalidateVisual();

            _wasTargetMarkerVisibleBeforeWorkAreaEdit = false;
            _wasAttackOverlayVisibleBeforeWorkAreaEdit = false;
            _boundsBeforeWorkAreaEdit = null;
        }

        private void SaveWorkAreaBounds(RectLatLng bounds)
        {
            _isUpdatingMapLimitsUi = true;

            try
            {
                _settingsService.StartSettings.LimitTopLeftLat = bounds.Top;
                _settingsService.StartSettings.LimitTopLeftLng = bounds.Left;
                _settingsService.StartSettings.LimitBottomRightLat = bounds.Bottom;
                _settingsService.StartSettings.LimitBottomRightLng = bounds.Right;
                _settingsService.StartSettings.IsMapLimitsEnabled = true;

                CheckBoxLimitMap.IsChecked = true;

                TextBoxLimitTopLeft.Text = FormatLimitPoint(bounds.Top, bounds.Left);
                TextBoxLimitBottomRight.Text = FormatLimitPoint(bounds.Bottom, bounds.Right);
            }
            finally
            {
                _isUpdatingMapLimitsUi = false;
            }

            _settingsService.SaveStartSettings();

            UpdateBoundsOfMap();
            EnsureZoomLimitsCompatibleWithBounds();
            EnsurePositionInsideBoundsOnEnable();
        }

        private string FormatLimitPoint(double lat, double lng)
        {
            try
            {
                if (_coordinateConverter.TryLatLngToUTM(
                        lat,
                        lng,
                        out System.Drawing.PointF utm,
                        out int zone,
                        out char band))
                {
                    return _coordinateConverter.FormatUTM(
                        utm,
                        zone,
                        band
                    );
                }
            }
            catch
            {
            }

            return string.Create(
                CultureInfo.InvariantCulture,
                $"{lat}, {lng}"
            );
        }

        // Robust coordinate parser — supports Lat/Lng fallback and explicit UTM parsing.
        private bool TryParseLatLng(string text, out double lat, out double lng)
        {
            // Keep backwards compatibility: first try UTM if explicitly provided; otherwise parse lat/lng
            return TryParseLatLng(text, out lat, out lng, utmOnly: false);
        }

        // If utmOnly==true, we accept only UTM inputs (explicit zone/band or 'UTM' token).
        private bool TryParseLatLng(string text, out double lat, out double lng, bool utmOnly)
        {
            lat = 0; lng = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            try
            {
                var input = text.Trim();
                var upper = input.ToUpperInvariant();


                // Try MGRS first (it's stricter than our loose UTM regex)
                // This prevents ambiguous inputs like "36U UB 123 456" being parsed as UTM "36U 123 456" 
                // (interpreting 123/456 as full meters, leading to wrong location)
                if (_coordinateConverter.TryMGRSToLatLng(input, out double mLat, out double mLng))
                {
                    lat = mLat; lng = mLng;
                    return true;
                }

                // Detect UTM with zone token like '36U' or '36'
                var zoneMatch = Regex.Match(upper, @"\b(?<zone>\d{1,2})(?<band>[C-HJ-NP-X])\b");
                if (!zoneMatch.Success)
                    zoneMatch = Regex.Match(upper, @"\b(?<zone>\d{1,2})\b");

                // Find all numbers (supports dot or comma decimal)
                var matches = Regex.Matches(input, @"-?\d+(?:[.,]\d+)?");

                // If we have a UTM zone and at least two numbers, try to parse as UTM (Easting, Northing)
                if (zoneMatch.Success && matches.Count >= 2)
                {
                    bool hasBand = !string.IsNullOrEmpty(zoneMatch.Groups["band"].Value);
                    bool hasUtmWord = Regex.IsMatch(upper, "\\bUTM\\b");

                    string sE = null;
                    string sN = null;

                    // Prefer explicit 'E' and 'N' markers if present (e.g. "380707.88 E 5586994.00 N")
                    var eMatch = Regex.Match(input, @"(?<e>-?\d+(?:[.,]\d+)?)\s*[Ee]\b");
                    var nMatch = Regex.Match(input, @"(?<n>-?\d+(?:[.,]\d+)?)\s*[Nn]\b");
                    if (eMatch.Success && nMatch.Success)
                    {
                        sE = eMatch.Groups["e"].Value.Replace(',', '.');
                        sN = nMatch.Groups["n"].Value.Replace(',', '.');
                    }
                    else
                    {
                        // Fallback: account for the zone number appearing in the numeric matches list
                        // e.g. matches -> ["36","380707.88","5586994.00"]
                        int startIdx = 0;
                        if (matches.Count >= 3 && matches[0].Value == zoneMatch.Groups["zone"].Value)
                        {
                            startIdx = 1; // skip the zone number
                        }

                        sE = matches[startIdx].Value.Replace(',', '.');
                        sN = matches[startIdx + 1].Value.Replace(',', '.');
                    }

                    if (float.TryParse(sE, NumberStyles.Any, CultureInfo.InvariantCulture, out float easting) &&
                        float.TryParse(sN, NumberStyles.Any, CultureInfo.InvariantCulture, out float northing))
                    {
                        // Only treat values as UTM if user explicitly provided a zone/band or the word 'UTM'.
                        if (hasBand || hasUtmWord)
                        {
                            if (int.TryParse(zoneMatch.Groups["zone"].Value, out int zone))
                            {
                                char band = hasBand ? zoneMatch.Groups["band"].Value.ToUpper()[0] : ' ';
                                var utm = new System.Drawing.PointF(easting, northing);
                                if (_coordinateConverter.TryUTMToLatLng(utm, zone, band, out double plat, out double plng))
                                {
                                    lat = plat; lng = plng;
                                    return true;
                                }
                            }
                        }
                    }
                }

                // MGRS check was moved to top of method to prioritize it over ambiguous UTM parsing.
                // Fallback to simple Lat/Lng parsing if not utmOnly
                if (utmOnly) return false;


                if (!utmOnly)
                {
                    // Fallback: attempt lat,lng using first two numbers
                    if (matches.Count >= 2)
                    {
                        string sLat = matches[0].Value.Replace(',', '.');
                        string sLng = matches[1].Value.Replace(',', '.');

                        if (double.TryParse(sLat, NumberStyles.Any, CultureInfo.InvariantCulture, out lat) &&
                            double.TryParse(sLng, NumberStyles.Any, CultureInfo.InvariantCulture, out lng))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private void TextBoxLimitTopLeft_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingMapLimitsUi) return;

            // For Map Limits we accept ONLY explicit UTM inputs (zone/band or 'UTM' token)
            if (TryParseLatLng(TextBoxLimitTopLeft.Text, out double lat, out double lng, utmOnly: true))
            {
                _settingsService.StartSettings.LimitTopLeftLat = lat;
                _settingsService.StartSettings.LimitTopLeftLng = lng;
                UpdateBoundsOfMap();
                TextBoxLimitTopLeft.BorderBrush = System.Windows.Media.Brushes.Green;
                TextBoxLimitTopLeft.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            }
            else
            {
                TextBoxLimitTopLeft.BorderBrush = System.Windows.Media.Brushes.Red;
                TextBoxLimitTopLeft.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            }
        }

        private void TextBoxLimitBottomRight_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_isUpdatingMapLimitsUi) return;

            // For Map Limits we accept ONLY explicit UTM inputs (zone/band or 'UTM' token)
            if (TryParseLatLng(TextBoxLimitBottomRight.Text, out double lat, out double lng, utmOnly: true))
            {
                _settingsService.StartSettings.LimitBottomRightLat = lat;
                _settingsService.StartSettings.LimitBottomRightLng = lng;
                UpdateBoundsOfMap();
                TextBoxLimitBottomRight.BorderBrush = System.Windows.Media.Brushes.Green;
                TextBoxLimitBottomRight.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            }
            else
            {
                TextBoxLimitBottomRight.BorderBrush = System.Windows.Media.Brushes.Red;
                TextBoxLimitBottomRight.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
            }
        }

        private void button7_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                RenderTargetBitmap bmp = ToImageSource(MainMap);
                PngBitmapEncoder png = new PngBitmapEncoder();
                png.Frames.Add(BitmapFrame.Create(bmp));


                string path = "map_capture.png";
                using (Stream stream = File.Create(path))
                {
                    png.Save(stream);
                }
                MessageBox.Show($"Image saved to {path}");
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        private void button4_Click(object sender, RoutedEventArgs e)
        {
            if (MainMap.Manager.PrimaryCache != null)
            {
                MainMap.Manager.PrimaryCache.DeleteOlderThan(DateTime.Now, null);
                MessageBox.Show("Cache cleared.");
            }
        }

        private void button5_Click(object sender, RoutedEventArgs e)
        {
            // MainMap.ShowImportDialog(); // Not standard in WPF GMap
            MessageBox.Show("Use Shift+Alt+Drag to export logic (WinForms feature port pending).");
        }

        private void button6_Click(object sender, RoutedEventArgs e)
        {
            // MainMap.ShowExportDialog(); // Not standard in WPF GMap
            MessageBox.Show("Use Prefetch for basic cache population.");
        }

        private void buttonCacheDiag_Click(object sender, RoutedEventArgs e)
        {
            var w = new CacheStatsWindow();
            w.Owner = this;
            w.Stats = GetCacheStats();
            w.Show();
        }

        private void checkBox1_Checked(object sender, RoutedEventArgs e)
        {
            MainMap.ShowTileGridLines = true;
        }

        private void checkBox1_Unchecked(object sender, RoutedEventArgs e)
        {
            MainMap.ShowTileGridLines = false;
        }

        private void CheckBoxShowCoordinates_Checked(object sender, RoutedEventArgs e)
        {
            MainMap.ShowCoordinates = true;
            MainMap.InvalidateVisual();
        }

        private void CheckBoxShowCoordinates_Unchecked(object sender, RoutedEventArgs e)
        {
            MainMap.ShowCoordinates = false;
            MainMap.InvalidateVisual();
        }

        private void checkBoxCacheRoute_Checked(object sender, RoutedEventArgs e)
        {
            MainMap.Manager.UseRouteCache = CheckBoxCacheRoute.IsChecked == true;
        }

        private void checkBoxGeoCache_Checked(object sender, RoutedEventArgs e)
        {
            MainMap.Manager.UseGeocoderCache = CheckBoxGeoCache.IsChecked == true;
        }

        private void comboBoxMode_DropDownClosed(object sender, EventArgs e)
        {
            MainMap.Manager.Mode = (AccessMode)ComboBoxMode.SelectedItem;
            // Reset counters so UI reflects counts _after_ mode change
            ResetCacheCounters();

            if (_settingsService != null)
            {
                _settingsService.StartSettings.AccessMode = MainMap.Manager.Mode.ToString();
                _settingsService.SaveStartSettings();
            }

            // Refresh immediate UI
            try { CacheStatsUpdateTimer_Tick(null, EventArgs.Empty); } catch { }
        }

        private void ColumnSplitter_DragCompleted(object sender, DragCompletedEventArgs e)
        {
            try
            {
                MainMap.IsViewportResizeBoundsCorrectionSuspended = false;

                var width = ClampRightPanelWidth(GetCurrentRightPanelWidth());

                SetRightPanelWidth(width);

                Dispatcher.BeginInvoke(
                    DispatcherPriority.Render,
                    new Action(() =>
                    {
                        if (_settingsService.StartSettings.IsMapLimitsEnabled &&
                            MainMap.BoundsOfMap.HasValue)
                        {
                            EnsureZoomLimitsCompatibleWithBounds();

                            MainMap.ApplyBoundsOfMapToViewport();
                        }

                        if (_settingsService != null)
                        {
                            _settingsService.StartSettings.RightPanelWidth = width;
                            _settingsService.SaveStartSettings();
                        }

                        DebugSplitterState("SPLITTER DRAG COMPLETED AFTER LAYOUT");
                    })
                );
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SPLITTER] ERROR: {ex}");
            }
        }

        private double GetMaxAllowedRightPanelWidth()
        {
            var availableWidth = MainLayoutGrid.ActualWidth;

            if (availableWidth <= 0)
            {
                return MaxRightPanelWidth;
            }

            var maxByMapWidth = availableWidth - SplitterColumn.ActualWidth - MinMapWidth;

            return Math.Max(
                MinRightPanelWidth,
                Math.Min(MaxRightPanelWidth, maxByMapWidth)
            );
        }

        private double ClampRightPanelWidth(double requestedWidth)
        {
            var maxAllowedWidth = GetMaxAllowedRightPanelWidth();

            return Math.Max(
                MinRightPanelWidth,
                Math.Min(maxAllowedWidth, requestedWidth)
            );
        }

        private void SetRightPanelWidth(double width)
        {
            var clampedWidth = ClampRightPanelWidth(width);

            RightColumn.Width = new GridLength(
                clampedWidth,
                GridUnitType.Pixel
            );
        }

        private void ColumnSplitter_DragDelta(object sender, DragDeltaEventArgs e)
        {
            try
            {
                var currentWidth = GetCurrentRightPanelWidth();

                var requestedWidth = currentWidth - e.HorizontalChange;
                var clampedWidth = ClampRightPanelWidth(requestedWidth);

                if (Math.Abs(clampedWidth - currentWidth) < 0.1)
                {
                    e.Handled = true;
                    return;
                }

                var futureMapWidth = MainLayoutGrid.ActualWidth -
                                     SplitterColumn.ActualWidth -
                                     clampedWidth;

                futureMapWidth = Math.Max(
                    MinMapWidth,
                    futureMapWidth
                );

                DebugSplitterState(
                    "SPLITTER DRAG DELTA BEFORE SET WIDTH",
                    e.HorizontalChange,
                    currentWidth,
                    requestedWidth,
                    clampedWidth,
                    futureMapWidth
                );

                SetRightPanelWidth(clampedWidth);

                DebugSplitterState(
                    "SPLITTER DRAG DELTA AFTER SET WIDTH IMMEDIATE",
                    e.HorizontalChange,
                    currentWidth,
                    requestedWidth,
                    clampedWidth,
                    futureMapWidth
                );

                Dispatcher.BeginInvoke(
    DispatcherPriority.Render,
    new Action(() =>
    {
        EnsureZoomLimitsCompatibleWithBounds();

        DebugSplitterState(
            "SPLITTER DRAG DELTA AFTER LAYOUT",
            e.HorizontalChange,
            currentWidth,
            requestedWidth,
            clampedWidth,
            futureMapWidth
        );
    })
);

                e.Handled = true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SPLITTER] ERROR: {ex}");
            }
        }

        private void ColumnSplitter_DragStarted(object sender, DragStartedEventArgs e)
        {
            MainMap.IsViewportResizeBoundsCorrectionSuspended = false;

            DebugSplitterState("SPLITTER DRAG STARTED");
        }

        private double GetCurrentRightPanelWidth()
        {
            if (RightColumn.Width.GridUnitType == GridUnitType.Pixel &&
                RightColumn.Width.Value > 0)
            {
                return RightColumn.Width.Value;
            }

            return RightColumn.ActualWidth;
        }

        private void ResetCacheCounters()
        {
            try
            {
                // Reset core counters
                GMaps.Instance.TilesFromMemoryCache = 0;
                GMaps.Instance.TilesFromSQLiteCache = 0;
                GMaps.Instance.TilesFromNetwork = 0;

            }
            catch { }
        }

        private void CheckBoxZoomLimits_Checked(object sender, RoutedEventArgs e)
        {
            _settingsService.StartSettings.IsZoomLimitsEnabled = true;
            ApplyZoomLimits();
        }

        private void CheckBoxZoomLimits_Unchecked(object sender, RoutedEventArgs e)
        {
            _settingsService.StartSettings.IsZoomLimitsEnabled = false;
            ApplyZoomLimits();
        }

        private void TextBoxMinZoom_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateAndApplyZoomLimits();
        }

        private void TextBoxMaxZoom_TextChanged(object sender, TextChangedEventArgs e)
        {
            ValidateAndApplyZoomLimits();
        }

        private void ValidateAndApplyZoomLimits()
        {
            if (!IsLoaded || _isUpdatingZoomLimitsUi)
            {
                return;
            }

            var minParsed = int.TryParse(TextBoxMinZoom.Text, out var minZoom);
            var maxParsed = int.TryParse(TextBoxMaxZoom.Text, out var maxZoom);

            var minValid =
                minParsed &&
                minZoom >= TechnicalMinZoom &&
                minZoom <= TechnicalMaxZoom;

            var maxValid =
                maxParsed &&
                maxZoom >= TechnicalMinZoom &&
                maxZoom <= TechnicalMaxZoom;

            TextBoxMinZoom.BorderBrush = minValid
                ? Brushes.Gray
                : Brushes.Red;

            TextBoxMaxZoom.BorderBrush = maxValid
                ? Brushes.Gray
                : Brushes.Red;

            TextBoxMinZoom.ToolTip = null;
            TextBoxMaxZoom.ToolTip = null;

            if (!minValid || !maxValid)
            {
                return;
            }

            if (minZoom > maxZoom)
            {
                TextBoxMinZoom.BorderBrush = Brushes.Red;
                TextBoxMaxZoom.BorderBrush = Brushes.Red;

                TextBoxMaxZoom.ToolTip =
                    "Максимальний zoom не може бути меншим за мінімальний.";

                return;
            }

            var requiredMaxZoom = GetRequiredMaxZoomForActiveBounds();

            if (requiredMaxZoom.HasValue &&
                maxZoom < requiredMaxZoom.Value)
            {
                TextBoxMaxZoom.BorderBrush = Brushes.Red;

                TextBoxMaxZoom.ToolTip =
                    $"Для поточної робочої області потрібен MaxZoom не менше {requiredMaxZoom.Value}.";

                return;
            }

            _settingsService.StartSettings.MinZoom = minZoom;
            _settingsService.StartSettings.MaxZoom = maxZoom;

            ApplyZoomLimits();
        }

        // Перевіряє, чи поточний користувацький MaxZoom дозволяє
        // виконати BoundsOfMap після зміни розміру viewport.
        //
        // Якщо MaxZoom став недостатнім через resize карти,
        // він автоматично піднімається до найменшого сумісного значення.
        private void EnsureZoomLimitsCompatibleWithBounds()
        {
            if (!_settingsService.StartSettings.IsZoomLimitsEnabled)
            {
                return;
            }

            var requiredMaxZoom = GetRequiredMaxZoomForActiveBounds();

            if (!requiredMaxZoom.HasValue ||
                requiredMaxZoom.Value <= _settingsService.StartSettings.MaxZoom)
            {
                return;
            }

            if (requiredMaxZoom.Value > TechnicalMaxZoom)
            {
                TextBoxMaxZoom.BorderBrush = Brushes.Red;
                TextBoxMaxZoom.ToolTip =
                    "Робоча область надто мала для поточного розміру карти навіть при MaxZoom 24.";

                return;
            }

            _isUpdatingZoomLimitsUi = true;

            try
            {
                _settingsService.StartSettings.MaxZoom = requiredMaxZoom.Value;

                MainMap.MaxZoom = requiredMaxZoom.Value;

                TextBoxMaxZoom.Text = requiredMaxZoom.Value.ToString(
                    CultureInfo.InvariantCulture
                );

                TextBoxMaxZoom.BorderBrush = Brushes.Gray;
                TextBoxMaxZoom.ToolTip =
                    $"MaxZoom автоматично піднято до {requiredMaxZoom.Value}, щоб робоча область залишалась коректною.";
            }
            finally
            {
                _isUpdatingZoomLimitsUi = false;
            }

            _settingsService.SaveStartSettings();

            MainMap.ApplyBoundsOfMapToViewport();
        }

        private void ApplyZoomLimits()
        {
            if (_settingsService.StartSettings.IsZoomLimitsEnabled)
            {
                MainMap.MinZoom = _settingsService.StartSettings.MinZoom;
                MainMap.MaxZoom = _settingsService.StartSettings.MaxZoom;

                // If current zoom is outside new limits, move it to the nearest limit
                double currentZoom = MainMap.Zoom;
                if (currentZoom < MainMap.MinZoom)
                {
                    MainMap.Zoom = MainMap.MinZoom;
                }
                else if (currentZoom > MainMap.MaxZoom)
                {
                    MainMap.Zoom = MainMap.MaxZoom;
                }
            }
            else
            {
                // Reset to default GMap.NET limits
                MainMap.MinZoom = 1;
                MainMap.MaxZoom = 24;
            }
        }

        // ----------------- Reports UI handlers -----------------
        private void NotificationRaisedHandler(string message, Services.NotificationType type)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    LabelReportStatus.Content = message;

                    var interval = TimeSpan.FromSeconds(type == NotificationType.Warning ? 6 : type == NotificationType.Error ? 8 : 3);

                    if (_notificationTimer == null)
                    {
                        _notificationTimer = new DispatcherTimer();
                        _notificationTimer.Tick += (s, e) => { LabelReportStatus.Content = ""; _notificationTimer.Stop(); };
                    }
                    _notificationTimer.Stop();
                    _notificationTimer.Interval = interval;
                    _notificationTimer.Start();
                });
            }
            catch { }
        }

        private void UpdateFlightCitiesList()
        {
            var input = TextBoxFlightDirection?.Text ?? string.Empty;
            var cities = ParseFlightCities(input);

            ListBoxFlightCities.ItemsSource = cities.Count > 0
                ? cities
                : null;
        }

        private void RemoveInvalidSelectedFlightCities()
        {
            var input = TextBoxFlightDirection?.Text ?? string.Empty;
            var availableCities = ParseFlightCities(input);

            var selected = _settingsService.StartSettings.SelectedFlightCities
                ?? new List<string>();

            var validSelected = selected
                .Where(x => availableCities.Contains(x, StringComparer.CurrentCultureIgnoreCase))
                .Distinct(StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            _settingsService.StartSettings.SelectedFlightCities = validSelected;
            _lastSavedSelectedCities = new List<string>(validSelected);
        }

        private void ButtonStartOfWork_Click(object sender, RoutedEventArgs e)
        {
            if (_reportService != null)
            {
                try { _reportService.Start_of_Work_Click(sender, EventArgs.Empty); }
                catch (Exception ex) { _notificationService?.Notify($"Помилка генерації: {ex.Message}", NotificationType.Error); }
            }
            else
            {
                _notificationService?.Notify("Контролер звітів не ініціалізовано", NotificationType.Warning);
            }
        }

        private void ButtonEndOfWork_Click(object sender, RoutedEventArgs e)
        {
            if (_reportService != null)
            {
                try { _reportService.End_of_Work_Click(sender, EventArgs.Empty); }
                catch (Exception ex) { _notificationService?.Notify($"Помилка генерації: {ex.Message}", NotificationType.Error); }
            }
            else
            {
                _notificationService?.Notify("Контролер звітів не ініціалізовано", NotificationType.Warning);
            }
        }

        private void ButtonCombatWork_Click(object sender, RoutedEventArgs e)
        {
            if (_reportService != null)
            {
                try { _reportService.Combat_Work_Click(sender, EventArgs.Empty); }
                catch (Exception ex) { _notificationService?.Notify($"Помилка генерації: {ex.Message}", NotificationType.Error); }
            }
            else
            {
                _notificationService?.Notify("Контролер звітів не ініціалізовано", NotificationType.Warning);

            }
        }

        private double GetDistanceFromLabel()
        {
            try
            {
                if (LabelDistanceValue.Content is string text && !string.IsNullOrWhiteSpace(text))
                {
                    // Очікуємо формат "X.X km"
                    string numberPart = text.Replace("km", "").Trim();
                    if (double.TryParse(numberPart, NumberStyles.Any, CultureInfo.InvariantCulture, out double km))
                        return km;
                }
            }
            catch { }
            return 0;
        }

        // Minimal IReportContext implementation to start migration
        public string GetSelectedPosition() => ComboBoxPosition?.Text ?? string.Empty;
        public string GetSelectedPilot() => ComboBoxPilot?.Text ?? string.Empty;
        public string GetSelectedDrone() => ComboBoxDroneBy?.Text ?? string.Empty;

        public string GetShootingTarget() => ComboBoxTargetType?.Text ?? string.Empty;
        public string GetHeightText() => TextBoxHeight?.Text ?? string.Empty;
        public int GetSelectedRange()
        {
            try
            {
                // LabelDistanceValue.Content містить рядок формату "X.X km"
                if (LabelDistanceValue.Content is string text && !string.IsNullOrWhiteSpace(text))
                {
                    // Відрізаємо " km" і пробіли
                    string numberPart = text.Replace("km", "").Trim();

                    // Спочатку парсимо як double, потім округлюємо до int
                    if (double.TryParse(numberPart, System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture, out double km))
                        return (int)Math.Round(km);
                }
            }
            catch { }
            return 0;
        }

        private bool TryGetTargetMarkerUtm(out System.Drawing.PointF utm)
        {
            utm = System.Drawing.PointF.Empty;

            var targetMarker = _targetMarkerManager.CurrentMarker;

            if (targetMarker == null)
            {
                return false;
            }

            var position = targetMarker.Position;

            try
            {
                if (_mapService != null &&
                    _mapService.TryLatLngToUTM(
                        position.Lat,
                        position.Lng,
                        out var result,
                        out var zone,
                        out var band))
                {
                    utm = result;
                    _lastUtmZone = zone;
                    _lastUtmBand = band;
                    return true;
                }

                if (_coordinateConverter.TryLatLngToUTM(
                        position.Lat,
                        position.Lng,
                        out var fallback,
                        out var fallbackZone,
                        out var fallbackBand))
                {
                    utm = fallback;
                    _lastUtmZone = fallbackZone;
                    _lastUtmBand = fallbackBand;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        public bool TryGetUTM(out System.Drawing.PointF utm)
        {
            return TryGetTargetMarkerUtm(out utm);
        }

        public string FormatShortMGRSFromUTM(System.Drawing.PointF utm)
        {
            if (_lastUtmZone != 0)
            {
                if (_mapService != null) return _mapService.FormatShortMGRSFromUTM(utm, _lastUtmZone, _lastUtmBand);
                return _coordinateConverter.FormatShortMGRSFromUTM(utm, _lastUtmZone, _lastUtmBand);
            }
            return "-";
        }

        public string FindClosestLocality(System.Drawing.PointF utm)
        {
            if (_coordinateConverter.TryUTMToLatLng(utm, out double lat, out double lng))
            {
                var latlng = new PointLatLng(lat, lng);
                return FindNearestLocality(latlng);
            }

            return string.Empty;
        }

        public bool TryGetClickedLatLng(out PointLatLng latlng)
        {
            latlng = PointLatLng.Empty;

            var targetMarker = _targetMarkerManager.CurrentMarker;

            if (targetMarker == null)
            {
                return false;
            }

            latlng = targetMarker.Position;
            return true;
        }

        public string FormatShortMGRSFromLatLng(PointLatLng latlng)
        {
            if (_coordinateConverter.TryLatLngToUTM(latlng.Lat, latlng.Lng, out var utm, out var zone, out var band))
            {
                return _coordinateConverter.FormatShortMGRSFromUTM(utm, zone, band);
            }
            return "-";
        }

        public string FindClosestLocalityFromLatLng(PointLatLng latlng)
        {
            return FindNearestLocality(latlng);
        }

        public string GetTargetType()
        {
            return ComboBoxTargetType?.Text ?? string.Empty;
        }

        private string FindNearestLocality(PointLatLng latlng)
        {
            if (TryFindNearestSettlementFromGeometryCache(latlng, out var settlementResult))
            {
                return settlementResult.Name;
            }

            if (_settingsService?.StartSettings == null)
            {
                return string.Empty;
            }

            var settings = _settingsService.StartSettings;

            if (!settings.CachedTargetLat.HasValue ||
                !settings.CachedTargetLng.HasValue)
            {
                return string.Empty;
            }

            var cachedPoint = new PointLatLng(
                settings.CachedTargetLat.Value,
                settings.CachedTargetLng.Value
            );

            if (!AreSamePoint(cachedPoint, latlng))
            {
                return string.Empty;
            }

            var cachedLocality = NormalizeLocalityName(settings.LastTargetLocationName);

            return IsUsableSettlementName(cachedLocality)
                ? cachedLocality
                : string.Empty;
        }

        private bool TryFindNearestSettlementFromGeometryCache(
    PointLatLng latlng,
    out SettlementSearchResult result)
        {
            result = new SettlementSearchResult();

            if (_settlementGeometryService == null ||
                !_settlementGeometryService.HasSettlements)
            {
                return false;
            }

            if (!TryConvertLatLngToUtm(latlng, out var utm))
            {
                return false;
            }

            if (!_settlementGeometryService.TryFindSettlement(utm, out var found))
            {
                return false;
            }

            var name = NormalizeLocalityName(found.Name);

            if (!IsUsableSettlementName(name))
            {
                return false;
            }

            found.Name = name;
            result = found;

            return true;
        }

        private bool TryConvertLatLngToUtm(
            PointLatLng latlng,
            out System.Drawing.PointF utm)
        {
            utm = System.Drawing.PointF.Empty;

            try
            {
                if (_mapService != null &&
                    _mapService.TryLatLngToUTM(
                        latlng.Lat,
                        latlng.Lng,
                        out var mapServiceUtm,
                        out var mapServiceZone,
                        out var mapServiceBand))
                {
                    utm = mapServiceUtm;
                    _lastUtmZone = mapServiceZone;
                    _lastUtmBand = mapServiceBand;
                    return true;
                }

                if (_coordinateConverter.TryLatLngToUTM(
                        latlng.Lat,
                        latlng.Lng,
                        out var converterUtm,
                        out var converterZone,
                        out var converterBand))
                {
                    utm = converterUtm;
                    _lastUtmZone = converterZone;
                    _lastUtmBand = converterBand;
                    return true;
                }
            }
            catch
            {
            }

            return false;
        }

        private static string BuildSettlementAddressText(SettlementSearchResult result)
        {
            if (result == null || string.IsNullOrWhiteSpace(result.Name))
            {
                return string.Empty;
            }

            if (result.IsInsidePolygon)
            {
                return "точка в межах НП";
            }

            var distanceKm = result.DistanceMeters / 1000.0;

            return "до межі НП: " +
                   distanceKm.ToString("F1", CultureInfo.InvariantCulture) +
                   " км";
        }

        private static bool AreSamePoint(PointLatLng a, PointLatLng b)
        {
            return Math.Abs(a.Lat - b.Lat) < 0.000001 &&
                   Math.Abs(a.Lng - b.Lng) < 0.000001;
        }

        private static string NormalizeLocalityName(string? value)
        {
            return string.IsNullOrWhiteSpace(value)
                ? string.Empty
                : value.Trim();
        }

        private static bool IsUsableSettlementName(string? value)
        {
            var name = NormalizeLocalityName(value);

            if (string.IsNullOrWhiteSpace(name))
            {
                return false;
            }

            var lower = name.ToLower(new CultureInfo("uk-UA"));

            string[] blockedWords =
            {
        "область",
        "район",
        "територіальна громада",
        "міська громада",
        "селищна громада",
        "сільська громада",
        "громада",
        "україна",
        "ukraine",
        "невідомо",
        "не знайдено"
    };

            if (blockedWords.Any(lower.Contains))
            {
                return false;
            }

            string[] addressObjectWords =
            {
        "вулиця",
        "вул.",
        "площа",
        "проспект",
        "пр-т",
        "провулок",
        "узвіз",
        "бульвар",
        "набережна",
        "дорога",
        "шосе",
        "траса",
        "станція",
        "зупинка",
        "стадіон",
        "ринок",
        "парк",
        "кладовище",
        "урочище",
        "мікрорайон",

        "улица",
        "площадь",
        "проспект",
        "переулок",
        "дорога",
        "шоссе",
        "станция",
        "остановка",
        "стадион",
        "рынок",
        "парк"
    };

            if (addressObjectWords.Any(lower.Contains))
            {
                return false;
            }

            return true;
        }

        private DateTime _reportStartTime = DateTime.MinValue;
        public string GetAndSetTime()
        {
            _reportStartTime = DateTime.Now;
            return _reportStartTime.ToString("HH.mm");
        }

        public string GetCurrentTimeString() => DateTime.Now.ToString("HH.mm");

        public TemplateService GetShablon()
        {
            if (_templateService != null)
                return _templateService;

            if (_reportService?.TemplateService != null)
            {
                _templateService = _reportService.TemplateService;
                return _templateService;
            }

            throw new InvalidOperationException("TemplateService не ініціалізовано.");
        }

        public bool IsTargetDestroyed() => CheckBoxTargetDestroyed.IsChecked == true;
        public bool IsTargetBoardLost() => CheckBoxTargetBoard.IsChecked != true;

        public void SetReportText(string text)
        {
            try
            {
                // If DataContext is ReportViewModel, update its ReportText property so bindings reflect the change
                if (this.DataContext is ViewModels.ReportViewModel vm)
                {
                    vm.ReportText = text ?? string.Empty;
                }
                else
                {
                    Dispatcher.Invoke(() => TextBoxReportOutput.Text = text);
                }
            }
            catch { }
        }

        public void CopyToClipboardWithNotification(string text)
        {
            try
            {
                if (_clipboardService != null && _clipboardService.TrySetText(text))
                    _notificationService?.Notify("Звіт скопійований у буфер");
                else
                    _notificationService?.Notify("Не вдалося скопіювати в буфер", NotificationType.Warning);
            }
            catch { _notificationService?.Notify("Не вдалося скопіювати в буфер", NotificationType.Warning); }
        }

        public System.Drawing.PointF? GetClickedPoint()
        {
            if (TryGetTargetMarkerUtm(out var utm))
            {
                return utm;
            }

            return null;
        }

        public System.Drawing.PointF GetAttackPoint()
        {
            try
            {
                if (_settingsService.AttackSettings.IsSet)
                {
                    if (_mapService != null)
                    {
                        if (_mapService.TryLatLngToUTM(_settingsService.AttackSettings.Lat, _settingsService.AttackSettings.Lng, out var utm, out var zone, out var band))
                        {
                            _lastUtmZone = zone;
                            _lastUtmBand = band;
                            return utm;
                        }
                    }

                    if (_coordinateConverter.TryLatLngToUTM(_settingsService.AttackSettings.Lat, _settingsService.AttackSettings.Lng, out var fallbackUtm, out var fzone, out var fband))
                    {
                        _lastUtmZone = fzone;
                        _lastUtmBand = fband;
                        return fallbackUtm;
                    }
                }
            }
            catch { }
            return System.Drawing.PointF.Empty;
        }

        private void SaveSelectedFlightCities()
        {
            try
            {
                var selected = ListBoxFlightCities.SelectedItems.Cast<string>().ToList();
                _settingsService.StartSettings.SelectedFlightCities = selected;
                _settingsService.SaveStartSettings();
                _lastSavedSelectedCities = new List<string>(selected);
            }
            catch { }
        }

        public string GetLabelScaleText()
        {
            return LabelAngleValue?.Content?.ToString() ?? "-";
        }

        public void SetAzimuthDisplay(string text)
        {
            try { Dispatcher.Invoke(() => { if (LabelAzimuthValue != null) LabelAzimuthValue.Content = text; try { MainMap.AzimuthText = text; MainMap.InvalidateVisual(); } catch { } }); } catch { }
        }

        public string GetAzimuthText()
        {
            try { return LabelAzimuthValue?.Content?.ToString() ?? string.Empty; } catch { return string.Empty; }
        }

        // Генерація тепер обробляється у TemplateEditorViewModel.GenerateCommand (без code-behind)
        private void RefreshTemplatesAndHistories()
        {
            try
            {
                // Після переходу на 3 окремі кнопки генерації
                // у вкладці "Звіти" більше немає ComboBoxTemplateSelect.
                //
                // Списки шаблонів, позицій, пілотів, дронів і цілей
                // оновлює TemplateEditorViewModel через binding.
                //
                // ComboBox редактора шаблонів залишився окремо:
                // ComboBoxTemplateSelectSettings.
            }
            catch
            {
            }
        }

        // Allow external injection of TemplateService (so DI can provide singleton instance)
        public void SetTemplateService(Services.TemplateService templateService)
        {
            if (templateService == null)
            {
                throw new ArgumentNullException(nameof(templateService));
            }

            if (_reportService != null &&
                !ReferenceEquals(_reportService.TemplateService, templateService))
            {
                templateService = _reportService.TemplateService;
            }

            _templateService = templateService;

            try
            {
                _templateService.LoadAllData();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TemplateService load failed: {ex.Message}");
            }

            try
            {
                RefreshTemplatesAndHistories();
            }
            catch { }
        }

        private void ComboBoxTemplateSelectSettings_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            // No diagnostic logging (exceptions only)
        }

        private void ComboBoxTemplateSelectSettings_PreviewMouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // No diagnostic logging (exceptions only)
        }

        private void ComboBoxTemplateSelectSettings_DropDownOpened(object sender, EventArgs e)
        {
            // No diagnostic logging (exceptions only)
        }

        private void ComboBoxTemplateSelectSettings_DropDownClosed(object sender, EventArgs e)
        {
            // No diagnostic logging (exceptions only)
        }

        //private void ComboBoxTemplateSelect_SelectionChanged(object sender, SelectionChangedEventArgs e)
        //{
        //    // Auto-generation removed as per request
        //}

        // Allow external injection of NotificationService (subscribe handler) and ClipboardService
        public void SetNotificationService(Services.NotificationService notificationService)
        {
            if (notificationService == null) throw new ArgumentNullException(nameof(notificationService));
            try { if (_notificationService != null) _notificationService.NotificationRaised -= NotificationRaisedHandler; } catch { }
            _notificationService = notificationService;
            _notificationService.NotificationRaised += NotificationRaisedHandler;
        }

        public void SetClipboardService(Services.ClipboardService clipboardService)
        {
            if (clipboardService == null) throw new ArgumentNullException(nameof(clipboardService));
            _clipboardService = clipboardService;
        }

        // Initialize ReportService and set up ReportViewModel + TemplateEditorViewModel
        public void InitializeReportService(ReportService reportService)
        {
            if (reportService == null)
            {
                throw new ArgumentNullException(nameof(reportService));
            }

            _reportService = reportService;

            // Важливо: редактор шаблонів і ReportService мають використовувати один TemplateService.
            _templateService = _reportService.TemplateService;

            try
            {
                _templateService.LoadAllData();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"TemplateService load failed: {ex.Message}");
            }

            var reportVm = new ViewModels.ReportViewModel(
                _reportService,
                _clipboardService,
                _notificationService
            );

            this.DataContext = reportVm;

            if (string.IsNullOrWhiteSpace(reportVm.SelectedReportTemplate))
            {
                reportVm.SelectedReportTemplate = "Report";
            }

            var templateVm = new ViewModels.TemplateEditorViewModel(
                _templateService,
                _notificationService,
                _statisticsService,
                (templateName, lines) => _reportService != null
                    ? _reportService.GenerateTextFromTemplateEditor(templateName, lines)
                    : string.Empty,
                generated =>
                {
                    if (string.IsNullOrWhiteSpace(generated))
                    {
                        return;
                    }

                    SetReportText(generated);
                    _notificationService?.Notify("Згенеровано звіт", NotificationType.Info);
                }
            );

            try
            {
                TemplateEditorGroup.DataContext = templateVm;
            }
            catch { }

            try
            {
                RefreshTemplatesAndHistories();
            }
            catch { }
        }
    }

}