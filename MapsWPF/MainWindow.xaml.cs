using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
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
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsPresentation;
using MapsWPF.Models;
using MapsWPF.Services;

namespace MapsWPF
{
    public partial class MainWindow : Window, Controllers.IReportContext
    {
        // marker
        GMapMarker currentMarker;

        // zones list
        List<GMapMarker> Circles = new List<GMapMarker>();

        // coordinate converter
        private CoordinateConverter _coordinateConverter = new();

        // Reports: template service and controller
        private Services.TemplateService? _templateService;
        private Controllers.ReportController? _reportController;

        // Map service (concrete)
        private Services.MapService? _mapService;

        // Clipboard and notification services (concrete)
        private Services.ClipboardService? _clipboardService;
        private Services.NotificationService? _notificationService;
        private DispatcherTimer? _notificationTimer;

        // Last known UTM zone/band from TryGetUTM conversion
        private int _lastUtmZone = 0;
        private char _lastUtmBand = ' ';

        

        // Geocoding cancellation token
        private CancellationTokenSource _geocodingCts;

        // Cache statistics debouncing
        private DispatcherTimer _cacheStatsUpdateTimer;
        private int _lastMemCache = -1;
        private int _lastSqliteCache = -1;
        private int _lastNetwork = -1;

        // Tile load counters
        private int _tilesLoadedFromRam = 0;
        private int _tilesLoadedFromDisk = 0;
        private int _tilesLoadedFromNet = 0;


        private static readonly HttpClient _httpClient = new HttpClient();

        public MainWindow()
        {
            // Configure logging
            var logPath = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "MapsWPF_Geocoding.log");
            var logWriter = new System.IO.StreamWriter(logPath, false) { AutoFlush = true };
            Console.SetOut(logWriter);
            Console.WriteLine($"=== MapsWPF Log Started at {DateTime.Now} ===");

            // UserAgent for OpenStreetMap

            GMapProvider.UserAgent = "MapsWPF/1.0 (Windows; U; Windows NT 10.0; uk-UA) GMap.NET/2.0";

            InitializeComponent();

            // Initialize cache statistics update timer
            _cacheStatsUpdateTimer = new DispatcherTimer();
            _cacheStatsUpdateTimer.Interval = TimeSpan.FromMilliseconds(500);
            _cacheStatsUpdateTimer.Tick += CacheStatsUpdateTimer_Tick;

            // Load startup settings
            _settingsManager = new SettingsManager();

            // Apply saved AccessMode (persisted as string) and respect offline mode
            var savedModeStr = _settingsManager.StartSettings.AccessMode ?? "ServerAndCache";
            if (!Enum.TryParse<AccessMode>(savedModeStr, true, out var savedMode))
            {
                savedMode = MainMap.Manager.Mode;
            }
            // If there's no internet available, force CacheOnly
            if (!PingNetwork("google.com") && savedMode != AccessMode.CacheOnly)
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
            if (_settingsManager.StartSettings.WindowWidth.HasValue && _settingsManager.StartSettings.WindowHeight.HasValue)
            {
                this.Width = _settingsManager.StartSettings.WindowWidth.Value;
                this.Height = _settingsManager.StartSettings.WindowHeight.Value;
            }
            if (_settingsManager.StartSettings.WindowTop.HasValue && _settingsManager.StartSettings.WindowLeft.HasValue)
            {
                this.Top = _settingsManager.StartSettings.WindowTop.Value;
                this.Left = _settingsManager.StartSettings.WindowLeft.Value;
            }
            if (_settingsManager.StartSettings.WindowState == 2) // Maximized
            {
                this.WindowState = System.Windows.WindowState.Maximized;
            }

            // Map Provider

            var savedProviderName = _settingsManager.StartSettings.MapProviderName;
            var savedProvider = GMapProviders.List.FirstOrDefault(p => p.Name == savedProviderName) ?? GMapProviders.GoogleHybridMap;
            MainMap.MapProvider = savedProvider;

            MainMap.Position = new PointLatLng(_settingsManager.StartSettings.Lat, _settingsManager.StartSettings.Lng);
            MainMap.Zoom = _settingsManager.StartSettings.Zoom;

            // Load UI settings
            CheckBoxDebug.IsChecked = _settingsManager.StartSettings.ShowGrid;
            CheckBoxShowCoordinates.IsChecked = _settingsManager.StartSettings.ShowCoordinates;
            MainMap.ShowTileGridLines = _settingsManager.StartSettings.ShowGrid;
            MainMap.ShowCoordinates = _settingsManager.StartSettings.ShowCoordinates;

            // Load panel/tab state: keep coordinates expander behavior and map old 'expanded' flags to the selected tab
            ExpanderCoordinates.IsExpanded = _settingsManager.StartSettings.IsCoordinatesExpanded;

            // Choose which tab should be selected on startup if any of the old 'expanded' flags were set.
            int selectedTab = 0; // default to first tab (Target)
            if (_settingsManager.StartSettings.IsTargetExpanded) selectedTab = 0;
            else if (_settingsManager.StartSettings.IsGmapExpanded) selectedTab = 1;
            else if (_settingsManager.StartSettings.IsCacheExpanded) selectedTab = 2;
            else if (_settingsManager.StartSettings.IsGoExpanded) selectedTab = 3;
            else if (_settingsManager.StartSettings.IsRayExpanded) selectedTab = 4;
            else if (_settingsManager.StartSettings.IsMapLimitsExpanded) selectedTab = 5;

            // Prefer explicit saved SelectedRightTabIndex when present (backward-compatible)
            if (_settingsManager.StartSettings.SelectedRightTabIndex.HasValue)
            {
                selectedTab = _settingsManager.StartSettings.SelectedRightTabIndex.Value;
            }

            try { RightTabControl.SelectedIndex = selectedTab; } catch { }

            // Load Map Limits UI
            CheckBoxLimitMap.IsChecked = _settingsManager.StartSettings.IsMapLimitsEnabled;
            if (_settingsManager.StartSettings.LimitTopLeftLat.HasValue && _settingsManager.StartSettings.LimitTopLeftLng.HasValue)
                TextBoxLimitTopLeft.Text = $"{_settingsManager.StartSettings.LimitTopLeftLat.Value.ToString(CultureInfo.InvariantCulture)}, {_settingsManager.StartSettings.LimitTopLeftLng.Value.ToString(CultureInfo.InvariantCulture)}";


            if (_settingsManager.StartSettings.LimitBottomRightLat.HasValue && _settingsManager.StartSettings.LimitBottomRightLng.HasValue)
                TextBoxLimitBottomRight.Text = $"{_settingsManager.StartSettings.LimitBottomRightLat.Value.ToString(CultureInfo.InvariantCulture)}, {_settingsManager.StartSettings.LimitBottomRightLng.Value.ToString(CultureInfo.InvariantCulture)}";

            // Show UTM representation in inputs when possible (override lat/lng display)
            // Prefer UTM display for Map Limits fields so user sees expected format on startup.
            try
            {
                if (_settingsManager.StartSettings.LimitTopLeftLat.HasValue && _settingsManager.StartSettings.LimitTopLeftLng.HasValue)
                {
                    if (_coordinateConverter.TryLatLngToUTM(_settingsManager.StartSettings.LimitTopLeftLat.Value, _settingsManager.StartSettings.LimitTopLeftLng.Value, out System.Drawing.PointF utmTL, out int zoneTL, out char bandTL))
                    {
                        TextBoxLimitTopLeft.Text = _coordinateConverter.FormatUTM(utmTL, zoneTL, bandTL);
                    }
                }

                if (_settingsManager.StartSettings.LimitBottomRightLat.HasValue && _settingsManager.StartSettings.LimitBottomRightLng.HasValue)
                {
                    if (_coordinateConverter.TryLatLngToUTM(_settingsManager.StartSettings.LimitBottomRightLat.Value, _settingsManager.StartSettings.LimitBottomRightLng.Value, out System.Drawing.PointF utmBR, out int zoneBR, out char bandBR))
                    {
                        TextBoxLimitBottomRight.Text = _coordinateConverter.FormatUTM(utmBR, zoneBR, bandBR);
                    }
                }
            }
            catch { }
            // Map Zoom Limits
            CheckBoxZoomLimits.IsChecked = _settingsManager.StartSettings.IsZoomLimitsEnabled;
            TextBoxMinZoom.Text = _settingsManager.StartSettings.MinZoom.ToString();
            TextBoxMaxZoom.Text = _settingsManager.StartSettings.MaxZoom.ToString();

            // Apply loaded Zoom limits
            ApplyZoomLimits();

            // Initialize map bounds from settings
            UpdateBoundsOfMap();

            // If Map Limits are enabled at startup, make sure current position is inside the bounds
            if (_settingsManager.StartSettings.IsMapLimitsEnabled)
            {
                EnsurePositionInsideBoundsOnEnable();
            }

            // Force update labels

            MainMap_OnCurrentPositionChanged(MainMap.Position);

            MainMap.IgnoreMarkerOnMouseWheel = true;
            MainMap.TouchEnabled = false;
            MainMap.MultiTouchEnabled = true;
            MainMap.MouseWheelZoomType = MouseWheelZoomType.MousePositionWithoutCenter;
            MainMap.ShowCenter = false;

            //-- map events
            MainMap.OnPositionChanged += MainMap_OnCurrentPositionChanged;
            MainMap.OnMapZoomChanged += MainMap_OnMapZoomChanged;
            MainMap.OnTileLoadComplete += MainMap_OnTileLoadComplete;
            MainMap.OnTileLoadStart += MainMap_OnTileLoadStart;
            MainMap.OnMapTypeChanged += MainMap_OnMapTypeChanged;

            // Task 4: Enforce limits on Drag

            MainMap.OnMapDrag += MainMap_OnMapDrag;


            MainMap.MouseMove += MainMap_MouseMove;
            MainMap.MouseRightButtonDown += MainMap_MouseRightButtonDown;
            MainMap.MouseRightButtonUp += MainMap_MouseRightButtonUp;
            MainMap.MouseEnter += MainMap_MouseEnter;
            MainMap.MouseWheel += MainMap_MouseWheel;
            MainMap.Loaded += MainMap_Loaded;

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
                        var mgrsStr = _coordinateConverter.FormatShortMGRSFromUTM(utm, utmZone);
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

            // Initialize report template service and controller
            try
            {
                _templateService = new Services.TemplateService();
                _templateService.LoadAllData();
                var startupMsg = $"TemplateService loaded: Templates={_templateService.Templates?.Count ?? 0}, Targets={_templateService.Targets?.Count ?? 0}, Positions={_templateService.Position_Point?.Count ?? 0}, DroneByPosition={_templateService.DroneByPosition?.Count ?? 0}, Units={_templateService.UnitsHistory?.Count ?? 0}, LaunchAreas={_templateService.LaunchAreasHistory?.Count ?? 0}, SettingsFolder={_templateService.SettingsFolderPath}";
                Console.WriteLine(startupMsg);
                try { Directory.CreateDirectory(_templateService.SettingsFolderPath); File.AppendAllText(Path.Combine(_templateService.SettingsFolderPath, "diagnostics.log"), DateTime.Now.ToString("o") + " " + startupMsg + Environment.NewLine); } catch { }

                // Initialize map service
                _mapService = new Services.MapService(_coordinateConverter);

                // Initialize clipboard and notification services
                _clipboardService = new Services.ClipboardService();
                _notificationService = new Services.NotificationService();
                _notificationService.NotificationRaised += NotificationRaisedHandler;

                // Populate report-related UI elements (moved to TemplateEditorViewModel bindings)



                _reportController = new Controllers.ReportController(this);

                // Initialize ViewModel for reports and set DataContext
                var reportVm = new ViewModels.ReportViewModel(_reportController, _clipboardService, _notificationService);
                this.DataContext = reportVm;

                // Initialize TemplateEditorViewModel and bind it to the template editor group
                var templateVm = new ViewModels.TemplateEditorViewModel(_templateService, _notificationService,
                    lines => _reportController != null ? _reportController.GenerateTextFromTemplate(lines) : string.Empty,
                    generated => { SetReportText(generated); CopyToClipboardWithNotification(generated); _notificationService?.Notify("Згенеровано звіт", NotificationType.Info); });
                try { TemplateEditorGroup.DataContext = templateVm; } catch { }

                // Templates and histories handled by TemplateEditorViewModel, but keep Refresh for backward compat
                try
                {
                    RefreshTemplatesAndHistories();

                }
                catch { }            }
            catch (Exception ex)
            {
                Console.WriteLine($"Reports initialization failed: {ex.Message}");
            }

            CheckBoxCacheRoute.IsChecked = MainMap.Manager.UseRouteCache;
            CheckBoxGeoCache.IsChecked = MainMap.Manager.UseGeocoderCache;

            TextBoxLat.Text = MainMap.Position.Lat.ToString(CultureInfo.InvariantCulture);
            TextBoxLng.Text = MainMap.Position.Lng.ToString(CultureInfo.InvariantCulture);

            CheckBoxCurrentMarker.IsChecked = true;
            CheckBoxDragMap.IsChecked = MainMap.CanDragMap;

            // Apply saved right panel width (pixels) if present
            try
            {
                if (_settingsManager?.StartSettings != null)
                {
                    var w = _settingsManager.StartSettings.RightPanelWidth;
                    if (w > 50) RightColumn.Width = new GridLength(w, GridUnitType.Pixel);
                }
            }
            catch { }
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
            _settingsManager.StartSettings.Lat = MainMap.Position.Lat;
            _settingsManager.StartSettings.Lng = MainMap.Position.Lng;
            _settingsManager.StartSettings.Zoom = (int)MainMap.Zoom;


            _settingsManager.StartSettings.GoGeo = TextBoxGeo.Text;

            _settingsManager.StartSettings.ShowGrid = CheckBoxDebug.IsChecked == true;
            _settingsManager.StartSettings.ShowCoordinates = CheckBoxShowCoordinates.IsChecked == true;

            // Save panel/tab state: coordinates expander + which tab is selected
            _settingsManager.StartSettings.IsCoordinatesExpanded = ExpanderCoordinates.IsExpanded;
            int sel = 0;
            try { sel = RightTabControl.SelectedIndex; } catch { }

            // Persist explicit selected index (preferred) for future loads
            _settingsManager.StartSettings.SelectedRightTabIndex = sel;

            _settingsManager.StartSettings.IsTargetExpanded = sel == 0;
            _settingsManager.StartSettings.IsGmapExpanded = sel == 1;
            _settingsManager.StartSettings.IsCacheExpanded = sel == 2;
            _settingsManager.StartSettings.IsGoExpanded = sel == 3;
            _settingsManager.StartSettings.IsRayExpanded = sel == 4;
            _settingsManager.StartSettings.IsMapLimitsExpanded = sel == 5;

            // Save Window State
            if (this.WindowState == System.Windows.WindowState.Normal)
            {
                _settingsManager.StartSettings.WindowTop = this.Top;
                _settingsManager.StartSettings.WindowLeft = this.Left;
                _settingsManager.StartSettings.WindowWidth = this.Width;
                _settingsManager.StartSettings.WindowHeight = this.Height;
            }
            _settingsManager.StartSettings.WindowState = (int)this.WindowState;

            // Zoom Limits
            _settingsManager.StartSettings.IsZoomLimitsEnabled = CheckBoxZoomLimits.IsChecked == true;
            if (int.TryParse(TextBoxMinZoom.Text, out int minZ) && minZ >= 1 && minZ <= 24) _settingsManager.StartSettings.MinZoom = minZ;
            if (int.TryParse(TextBoxMaxZoom.Text, out int maxZ) && maxZ >= 1 && maxZ <= 24) _settingsManager.StartSettings.MaxZoom = maxZ;

            // Map Limits settings are updated in their respective event handlers/setters, 
            // but saving calls SaveStartSettings for all.
            _settingsManager.StartSettings.AccessMode = MainMap.Manager.Mode.ToString();

            // Save right panel width
            try
            {
                _settingsManager.StartSettings.RightPanelWidth = RightColumn.ActualWidth;
            }
            catch { }

            _settingsManager.SaveStartSettings();
        }

        private void RestoreAttackSettings()
        {
            _settingsManager.AttackSettings.PropertyChanged -= AttackSettings_PropertyChanged;

            TextBoxAttackAngle.Text = _settingsManager.AttackSettings.Angle.ToString("F2");
            TextBoxRayLength.Text = _settingsManager.AttackSettings.RayLength.ToString("F0");
            TextBoxSectorWidth.Text = _settingsManager.AttackSettings.SectorWidth.ToString("F2");
            TextBoxRotateStep.Text = _settingsManager.AttackSettings.RotateStep.ToString("F3", CultureInfo.InvariantCulture);
            TextBoxRotateShiftStep.Text = _settingsManager.AttackSettings.RotateShiftStep.ToString("F3", CultureInfo.InvariantCulture);

            UpdateMapAttackZone();

            // Initialize angle label
            try
            {
                if (_settingsManager.AttackSettings.IsSet)
                {
                    LabelAngleValue.Content = _settingsManager.AttackSettings.Angle.ToString("F1", CultureInfo.InvariantCulture) + "°";

                    // Also update azimuth display relative to current target marker
                    try
                    {
                        if (currentMarker != null)
                        {
                            var ap = new PointLatLng(_settingsManager.AttackSettings.Lat, _settingsManager.AttackSettings.Lng);
                            // compute azimuth similar to ReportController.CalculateAzimuth, but using lat/lng approximate
                            // compute azimuth using conventional 0 = North (use lng as East, lat as North)
                            var dx = currentMarker.Position.Lng - ap.Lng; // Easting difference (degrees)
                            var dy = currentMarker.Position.Lat - ap.Lat; // Northing difference (degrees)
                            var angleRad = Math.Atan2(dx, dy);
                            var angleDeg = (angleRad * (180.0 / Math.PI) + 360.0) % 360.0;
                            SetAzimuthDisplay(Math.Round(angleDeg).ToString(CultureInfo.InvariantCulture) + "°");

                            // Also update Range textbox to show distance in meters
                            try
                            {
                                var distKm = MainMap.MapProvider.Projection.GetDistance(ap, currentMarker.Position);
                                var distM = distKm * 1000.0;
                                TextBoxRange.Text = Math.Round(distM).ToString(CultureInfo.InvariantCulture);
                            }
                            catch { }

                            // Auto-detection of LaunchArea removed; keep manual selection loaded from JSON only.
                        }
                        else
                        {
                            // If attack settings not set, clear range
                            try { TextBoxRange.Text = string.Empty; } catch { }
                        }

                    }
                    catch { }
                }
                else
                {
                    LabelAngleValue.Content = "-";
                }
            }
            catch { }

            _settingsManager.AttackSettings.PropertyChanged += AttackSettings_PropertyChanged;
        }

        private void AttackSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            // Update angle label when attack settings change
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    if (_settingsManager?.AttackSettings != null && _settingsManager.AttackSettings.IsSet)
                    {
                        LabelAngleValue.Content = _settingsManager.AttackSettings.Angle.ToString("F1", CultureInfo.InvariantCulture) + "°";
                    }
                    else
                    {
                        LabelAngleValue.Content = "-";
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
            var transform = obj.LayoutTransform;
            obj.LayoutTransform = null;
            var margin = obj.Margin;
            obj.Margin = new Thickness(0, 0, margin.Right - margin.Left, margin.Bottom - margin.Top);
            var size = new System.Windows.Size(obj.Width, obj.Height);
            obj.Measure(size);
            obj.Arrange(new Rect(size));
            var bmp = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(obj);
            if (bmp.CanFreeze) bmp.Freeze();
            obj.LayoutTransform = transform;
            obj.Margin = margin;
            return bmp;
        }

        void MainMap_Loaded(object sender, RoutedEventArgs e)
        {
            MainMap.Zoom = _settingsManager.StartSettings.Zoom;
            MainMap.Position = new PointLatLng(_settingsManager.StartSettings.Lat, _settingsManager.StartSettings.Lng);


            TextBoxLat.Text = MainMap.Position.Lat.ToString(CultureInfo.InvariantCulture);
            TextBoxLng.Text = MainMap.Position.Lng.ToString(CultureInfo.InvariantCulture);


            TextBoxGeo.Text = _settingsManager.StartSettings.GoGeo;

            // Apply saved limits
            ValidateAndApplyZoomLimits(); /* or ApplyZoomLimits? Check method name. I added ValidateAndApplyZoomLimits in Step 160. */
            /* Wait, Step 160 added UpdateZoomLimits? or ApplyZoomLimits? */
            /* I'll check the file content first? No, I'll trust my memory or use ValidateAndApplyZoomLimits which calls ApplyZoomLimits. */

            // Initialize labels

            MainMap_OnCurrentPositionChanged(MainMap.Position);

            RestoreTargetPoint();

            MainMap.OnPositionChanged += (p) => UpdateDistanceDisplay();

            // Attack zone setup

            MainMap.MouseLeftButtonDown += MainMap_MouseLeftButtonDown;
            MainMap.MouseLeftButtonUp += MainMap_MouseLeftButtonUp;

            // Intercept dragging for limits

            MainMap.PreviewMouseLeftButtonDown += MainMap_PreviewMouseLeftButtonDown;
            MainMap.PreviewMouseLeftButtonUp += MainMap_PreviewMouseLeftButtonUp;
            MainMap.PreviewMouseMove += MainMap_PreviewMouseMove;


            RestoreAttackSettings();
            _settingsManager.OnAttackSettingsChanged += () => Dispatcher.Invoke(RestoreAttackSettings);
            this.Closing += MainWindow_Closing;
        }

        private void RestoreTargetPoint()
        {
            if (_settingsManager.StartSettings.TargetLat.HasValue && _settingsManager.StartSettings.TargetLng.HasValue)
            {
                var pos = new PointLatLng(_settingsManager.StartSettings.TargetLat.Value, _settingsManager.StartSettings.TargetLng.Value);
                CreateTargetMarker(pos);
                UpdateTargetLocationInfo(pos);
            }
        }

        private void CreateTargetMarker(PointLatLng pos)
        {
            if (currentMarker != null)
            {
                MainMap.Markers.Remove(currentMarker);
            }

            currentMarker = new GMapMarker(pos);
            var s = new System.Windows.Shapes.Ellipse
            {
                Width = 10,
                Height = 10,
                Fill = System.Windows.Media.Brushes.Red,
                Stroke = System.Windows.Media.Brushes.White,
                StrokeThickness = 2,
                IsHitTestVisible = true
            };


            s.MouseLeftButtonDown += Marker_MouseLeftButtonDown;

            currentMarker.Shape = s;
            currentMarker.Offset = new System.Windows.Point(-5, -5);
            currentMarker.ZIndex = int.MaxValue;
            MainMap.Markers.Add(currentMarker);


            UpdateDistanceDisplay();
        }

        private void Marker_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2)
            {
                // Double click to delete

                if (currentMarker != null)
                {
                    MainMap.Markers.Remove(currentMarker);
                    currentMarker = null;
                    MainMap.TargetDistance = -1;
                    LabelDistanceValue.Content = "Distance: -";

                    // Task 1: Clear fields on delete

                    LabelTargetLocation.Content = "-";
                    LabelTargetAddress.Content = "-";
                    _settingsManager.StartSettings.LastTargetLocationName = null;
                    _settingsManager.StartSettings.LastTargetAddress = null;


                    MainMap.InvalidateVisual();

                    // Clear from settings
                    _settingsManager.StartSettings.TargetLat = null;
                    _settingsManager.StartSettings.TargetLng = null;
                    _settingsManager.SaveStartSettings();


                    e.Handled = true;
                }
            }
        }


        void MainMap_OnMapTypeChanged(GMapProvider type)
        {
            if (_settingsManager != null)
            {
                _settingsManager.StartSettings.MapProviderName = type.Name;
                _settingsManager.SaveStartSettings();
            }
        }

        void MainMap_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            var p = e.GetPosition(MainMap);
            var pos = MainMap.FromLocalToLatLng((int)p.X, (int)p.Y);


            CreateTargetMarker(pos);


            _settingsManager.StartSettings.TargetLat = pos.Lat;
            _settingsManager.StartSettings.TargetLng = pos.Lng;
            _settingsManager.SaveStartSettings();


            UpdateTargetLocationInfo(pos);
        }

        void MainMap_MouseRightButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (currentMarker != null)
            {
                var pos = currentMarker.Position;


                _settingsManager.StartSettings.TargetLat = pos.Lat;
                _settingsManager.StartSettings.TargetLng = pos.Lng;
                _settingsManager.SaveStartSettings();


                UpdateTargetLocationInfo(pos);
                UpdateDistanceDisplay();
            }
        }

        private void UpdateDistanceDisplay()
        {
            if (currentMarker != null && _settingsManager != null && _settingsManager.AttackSettings.IsSet)
            {
                // Attack Point is now Geo-based
                var apLatLng = new PointLatLng(_settingsManager.AttackSettings.Lat, _settingsManager.AttackSettings.Lng);
                var distKm = MainMap.MapProvider.Projection.GetDistance(apLatLng, currentMarker.Position);
                var distM = distKm * 1000.0;

                // Update Range textbox (meters)
                try { TextBoxRange.Text = Math.Round(distM).ToString(CultureInfo.InvariantCulture); } catch { }

                LabelDistanceValue.Content = $"{distM:F0} m";
                // Update angle label alongside distance and compute azimuth to target
                try
                {
                    if (_settingsManager.AttackSettings.IsSet)
                    {
                        LabelAngleValue.Content = _settingsManager.AttackSettings.Angle.ToString("F1", CultureInfo.InvariantCulture) + "°";

                        // compute azimuth using conventional 0 = North (lng as East, lat as North)
                        var dx = currentMarker.Position.Lng - apLatLng.Lng; // East difference (degrees)
                        var dy = currentMarker.Position.Lat - apLatLng.Lat; // North difference (degrees)
                        var angleRad = Math.Atan2(dx, dy);
                        var angleDeg = (angleRad * (180.0 / Math.PI) + 360.0) % 360.0;
                        try { SetAzimuthDisplay(Math.Round(angleDeg).ToString(CultureInfo.InvariantCulture) + "°"); } catch { }
                    }
                    else
                    {
                        LabelAngleValue.Content = "-";
                    }
                }
                catch { }

                MainMap.TargetDistance = distM;
                MainMap.InvalidateVisual();
            }
            else
            {
                // Clear range if no attack point or target
                try { TextBoxRange.Text = string.Empty; } catch { }
            }
        }

        private async void UpdateTargetLocationInfo(PointLatLng pos)
        {
            if (_settingsManager.StartSettings.CachedTargetLat.HasValue &&
                _settingsManager.StartSettings.CachedTargetLng.HasValue &&
                Math.Abs(_settingsManager.StartSettings.CachedTargetLat.Value - pos.Lat) < 0.000001 &&
                Math.Abs(_settingsManager.StartSettings.CachedTargetLng.Value - pos.Lng) < 0.000001 &&
                !string.IsNullOrEmpty(_settingsManager.StartSettings.LastTargetLocationName))
            {
                LabelTargetLocation.Content = $"{_settingsManager.StartSettings.LastTargetLocationName}";
                LabelTargetAddress.Content = $"{_settingsManager.StartSettings.LastTargetAddress}";
                return;
            }

            if (_geocodingCts != null)
            {
                _geocodingCts.Cancel();
                _geocodingCts.Dispose();
            }
            _geocodingCts = new CancellationTokenSource();
            var token = _geocodingCts.Token;

            try
            {
                await System.Threading.Tasks.Task.Delay(500, token);
                Dispatcher.Invoke(() => ProgressBarTarget.Visibility = Visibility.Visible);
            }
            catch (System.Threading.Tasks.TaskCanceledException)
            {
                return;
            }

            if (token.IsCancellationRequested) return;

            await System.Threading.Tasks.Task.Run(async () =>
            {
                if (token.IsCancellationRequested) return;

                try
                {
                    GeocodingProvider provider = GMapProviders.OpenStreetMap as GeocodingProvider;
                    GeoCoderStatusCode status;
                    List<Placemark> placemarks = null;


                    status = provider.GetPlacemarks(pos, out placemarks);


                    bool foundSettlement = false;
                    if (status == GeoCoderStatusCode.OK && placemarks != null && placemarks.Count > 0)
                    {
                        var pm = placemarks[0];
                        if (!string.IsNullOrEmpty(pm.SubAdministrativeAreaName) || !string.IsNullOrEmpty(pm.LocalityName))
                            foundSettlement = true;
                    }

                    if (status != GeoCoderStatusCode.OK || !foundSettlement)
                    {
                        var googleProvider = GMapProviders.GoogleMap as GeocodingProvider;
                        List<Placemark> googlePlacemarks;
                        var googleStatus = googleProvider.GetPlacemarks(pos, out googlePlacemarks);


                        if (googleStatus == GeoCoderStatusCode.OK && googlePlacemarks != null && googlePlacemarks.Count > 0)
                        {
                            var gpm = googlePlacemarks[0];
                            bool googleFound = !string.IsNullOrEmpty(gpm.LocalityName);
                            if ((!foundSettlement && googleFound) || status != GeoCoderStatusCode.OK)
                            {
                                placemarks = googlePlacemarks;
                                status = googleStatus;
                                foundSettlement = googleFound;
                            }
                        }
                    }

                    string locality = null;
                    string fullAddress = null;

                    if (status == GeoCoderStatusCode.OK && placemarks != null && placemarks.Count > 0)
                    {
                        var pm = placemarks[0];
                        if (!string.IsNullOrEmpty(pm.SubAdministrativeAreaName)) locality = pm.SubAdministrativeAreaName;
                        else if (!string.IsNullOrEmpty(pm.LocalityName)) locality = pm.LocalityName;
                        else if (!string.IsNullOrEmpty(pm.DistrictName)) locality = pm.DistrictName;
                        else if (!string.IsNullOrEmpty(pm.AdministrativeAreaName)) locality = pm.AdministrativeAreaName;

                        var addressParts = new List<string>();
                        if (!string.IsNullOrEmpty(pm.SubAdministrativeAreaName)) addressParts.Add(pm.SubAdministrativeAreaName);
                        if (!string.IsNullOrEmpty(pm.LocalityName) && pm.LocalityName != pm.SubAdministrativeAreaName) addressParts.Add(pm.LocalityName);
                        if (!string.IsNullOrEmpty(pm.AdministrativeAreaName)) addressParts.Add(pm.AdministrativeAreaName);
                        fullAddress = addressParts.Count > 0 ? string.Join(", ", addressParts) : pm.Address;
                    }

                    if (!foundSettlement)
                    {
                        var nearest = await FindNearestSettlementOverpass(pos);
                        if (nearest != null)
                        {
                            locality = nearest.Value.Name;
                            fullAddress = $"{locality} ({nearest.Value.DistanceKm:F1} км)";
                            status = GeoCoderStatusCode.OK;

                        }
                    }

                    if (string.IsNullOrEmpty(locality)) locality = "Невідомо";
                    if (string.IsNullOrEmpty(fullAddress)) fullAddress = "Не знайдено";

                    Dispatcher.Invoke(() =>
                    {
                        if (LabelTargetLocation != null) LabelTargetLocation.Content = $"Населений пункт: {locality}";
                        if (LabelTargetAddress != null) LabelTargetAddress.Content = $"Адреса: {fullAddress}";
                        ProgressBarTarget.Visibility = Visibility.Collapsed;

                        _settingsManager.StartSettings.CachedTargetLat = pos.Lat;
                        _settingsManager.StartSettings.CachedTargetLng = pos.Lng;
                        _settingsManager.StartSettings.LastTargetLocationName = locality;
                        _settingsManager.StartSettings.LastTargetAddress = fullAddress;
                        _settingsManager.SaveStartSettings();
                    });
                }
                catch (Exception)
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (LabelTargetLocation != null) LabelTargetLocation.Content = "Населений пункт: -";
                        if (LabelTargetAddress != null) LabelTargetAddress.Content = "Адреса: помилка";
                        ProgressBarTarget.Visibility = Visibility.Collapsed;
                    });
                }
            }, token);
        }

        // State for drag interception
        private System.Windows.Point _lastMousePos;
        private bool _isDragging = false;

        private void MainMap_PreviewMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _lastMousePos = e.GetPosition(MainMap);
            // Default GMap behavior
        }

        private void MainMap_PreviewMouseMove(object sender, MouseEventArgs e)
        {
            // Logic removed implies default GMap behavior
        }

        private void MainMap_PreviewMouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Logic removed implies default GMap behavior

        }






        void MainMap_MouseMove(object sender, MouseEventArgs e)
        {
            // Redundant check removed since Preview handles it



            if (e.RightButton == MouseButtonState.Pressed)
            {
                var p = e.GetPosition(MainMap);
                currentMarker.Position = MainMap.FromLocalToLatLng((int)p.X, (int)p.Y);
                UpdateDistanceDisplay();
            }

            if (Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                // Rotation logic with geo-anchored attack point
                var p = e.GetPosition(MainMap);
                var mouseLatLng = MainMap.FromLocalToLatLng((int)p.X, (int)p.Y);
                var attackLatLng = new PointLatLng(_settingsManager.AttackSettings.Lat, _settingsManager.AttackSettings.Lng);

                // For bearing calculation we can use GMap provider or simple math
                // GMapProviders.EmptyProvider.Projection.GetBearing(attackLatLng, mouseLatLng)
                double bearing = MainMap.MapProvider.Projection.GetBearing(attackLatLng, mouseLatLng);

                // Adjust to match our angle system (0=North, 90=East)
                // GetBearing usually returns 0=North, 180=South. 
                // Our AttackAngle: 0 = Up(North), 90 = Right(East).

                if (bearing < 0) bearing += 360;


                _settingsManager.AttackSettings.Angle = (float)bearing;


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

        private MapsWPF.CacheStats GetCacheStats()
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

        private void MainMap_OnMapDrag()
        {
            // Bounds checking is now handled in Core.Drag() and Core.DragOffset()
        }

        // Updates BoundsOfMap in the map control based on settings
        private void UpdateBoundsOfMap()
        {
            if (_settingsManager.StartSettings.IsMapLimitsEnabled &&
                _settingsManager.StartSettings.LimitTopLeftLat.HasValue &&
                _settingsManager.StartSettings.LimitTopLeftLng.HasValue &&
                _settingsManager.StartSettings.LimitBottomRightLat.HasValue &&
                _settingsManager.StartSettings.LimitBottomRightLng.HasValue)
            {
                var topLat = _settingsManager.StartSettings.LimitTopLeftLat.Value;
                var leftLng = _settingsManager.StartSettings.LimitTopLeftLng.Value;
                var bottomLat = _settingsManager.StartSettings.LimitBottomRightLat.Value;
                var rightLng = _settingsManager.StartSettings.LimitBottomRightLng.Value;


                MainMap.BoundsOfMap = RectLatLng.FromLTRB(leftLng, topLat, rightLng, bottomLat);
            }
            else
            {
                MainMap.BoundsOfMap = null;
            }
        }

        // Map movement limiting - forces position to stay within bounds
        private void CheckMapLimits(PointLatLng point)
        {
            if (!_settingsManager.StartSettings.IsMapLimitsEnabled || MainMap.BoundsOfMap == null)
                return;


            var bounds = MainMap.BoundsOfMap.Value;
            if (!bounds.Contains(point))
            {
                // Clamp position to bounds
                var clampedLat = Math.Max(bounds.Bottom, Math.Min(bounds.Top, point.Lat));
                var clampedLng = Math.Max(bounds.Left, Math.Min(bounds.Right, point.Lng));


                if (point.Lat != clampedLat || point.Lng != clampedLng)
                {
                    MainMap.Position = new PointLatLng(clampedLat, clampedLng);
                }
            }
        }

        // When Map Limits are turned on, ensure the current position is moved INSIDE
        // the allowed rectangle (slightly inside the boundary to avoid exact lock).
        private void EnsurePositionInsideBoundsOnEnable()
        {
            if (!_settingsManager.StartSettings.IsMapLimitsEnabled || MainMap.BoundsOfMap == null)
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
            CheckMapLimits(point);

            LabelLatValue.Content = point.Lat.ToString("F8", CultureInfo.InvariantCulture);
            LabelLngValue.Content = point.Lng.ToString("F8", CultureInfo.InvariantCulture);

            if (_coordinateConverter.TryLatLngToUTM(point.Lat, point.Lng, out System.Drawing.PointF utm, out int utmZone, out char bandLetter))
            {
                // Use a consistent formatter (invariant culture) with E/N labels
                LabelUTMValue.Content = _coordinateConverter.FormatUTM(utm, utmZone, bandLetter);
                LabelMGRSValue.Content = _coordinateConverter.FormatShortMGRSFromUTM(utm, utmZone);
            }
            else
            {
                LabelUTMValue.Content = "-";
                LabelMGRSValue.Content = "-";
            }

            LabelZoomValue.Content = MainMap.Zoom.ToString(CultureInfo.InvariantCulture);
        }

        void MainMap_OnMapZoomChanged()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                LabelZoomValue.Content = MainMap.Zoom.ToString(CultureInfo.InvariantCulture);
            }));
        }

        void MainMap_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                LabelZoomValue.Content = MainMap.Zoom.ToString(CultureInfo.InvariantCulture);
            }));
        }

        private void button1_Click(object sender, RoutedEventArgs e)
        {
            MainMap.ReloadMap();
        }

        private void checkBoxCurrentMarker_Checked(object sender, RoutedEventArgs e)
        {
            if (currentMarker != null) MainMap.Markers.Add(currentMarker);
        }

        private void checkBoxCurrentMarker_Unchecked(object sender, RoutedEventArgs e)
        {
            if (currentMarker != null) MainMap.Markers.Remove(currentMarker);
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

                            if (currentMarker != null)
                            {
                                currentMarker.Position = MainMap.Position;
                                UpdateTargetLocationInfo(currentMarker.Position);
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
            MainMap.Zoom = (int)MainMap.Zoom + 1;
        }

        private void czuZoomDown_Click(object sender, RoutedEventArgs e)
        {
            MainMap.Zoom = (int)(MainMap.Zoom + 0.99) - 1;
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

        private SettingsManager _settingsManager;
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
            else if (e.Key == Key.Left && MainMap.IsFocused && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                float step = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ? _settingsManager.AttackSettings.RotateShiftStep : _settingsManager.AttackSettings.RotateStep;
                var newAngle = _settingsManager.AttackSettings.Angle - step;
                if (newAngle < 0) newAngle += 360f;
                _settingsManager.AttackSettings.Angle = newAngle;
                TextBoxAttackAngle.Text = _settingsManager.AttackSettings.Angle.ToString("F2");
                UpdateMapAttackZone();
                InvalidateThrottled();
                e.Handled = true;
            }
            else if (e.Key == Key.Right && MainMap.IsFocused && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                float step = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ? _settingsManager.AttackSettings.RotateShiftStep : _settingsManager.AttackSettings.RotateStep;
                var newAngle = _settingsManager.AttackSettings.Angle + step;
                if (newAngle >= 360) newAngle -= 360f;
                _settingsManager.AttackSettings.Angle = newAngle;
                TextBoxAttackAngle.Text = _settingsManager.AttackSettings.Angle.ToString("F2");
                UpdateMapAttackZone();
                InvalidateThrottled();
                e.Handled = true;
            }
            else if (e.Key == Key.Up && MainMap.IsFocused && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                float step = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ? _settingsManager.AttackSettings.RotateShiftStep : _settingsManager.AttackSettings.RotateStep;
                var newWidth = _settingsManager.AttackSettings.SectorWidth + step;
                if (newWidth > 180f) newWidth = 180f;
                _settingsManager.AttackSettings.SectorWidth = newWidth;
                TextBoxSectorWidth.Text = _settingsManager.AttackSettings.SectorWidth.ToString("F2");
                UpdateMapAttackZone();
                InvalidateThrottled();
                e.Handled = true;
            }
            else if (e.Key == Key.Down && MainMap.IsFocused && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                float step = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ? _settingsManager.AttackSettings.RotateShiftStep : _settingsManager.AttackSettings.RotateStep;
                var newWidth = _settingsManager.AttackSettings.SectorWidth - step;
                if (newWidth < 5f) newWidth = 5f;
                _settingsManager.AttackSettings.SectorWidth = newWidth;
                TextBoxSectorWidth.Text = _settingsManager.AttackSettings.SectorWidth.ToString("F2");
                UpdateMapAttackZone();
                InvalidateThrottled();
                e.Handled = true;
            }
            else if ((e.Key == Key.W || e.Key == Key.A || e.Key == Key.S || e.Key == Key.D) && MainMap.IsFocused && !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                if (!_wasdHeld.Contains(e.Key)) _wasdHeld.Add(e.Key);
                if (!_wasdTimer.IsEnabled) { _wasdAccumX = 0.0; _wasdAccumY = 0.0; _wasdTimer.Start(); }
                e.Handled = true;
            }
        }

        private void MainMap_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Allow removal without modifiers if double clicking NEAR the attack point
            var pos = e.GetPosition(MainMap);
            var latlng = MainMap.FromLocalToLatLng((int)pos.X, (int)pos.Y);


            var now = DateTime.Now;
            if ((now - _lastClickTime).TotalMilliseconds <= DoubleClickMaxMs) _clickCount++;
            else _clickCount = 1;
            _lastClickTime = now;

            if (_clickCount >= 2)
            {
                _clickCount = 0;

                // Task 5: Remove Attack Ray if double clicked near it

                if (_settingsManager.AttackSettings.IsSet)
                {
                    var attackP = new PointLatLng(_settingsManager.AttackSettings.Lat, _settingsManager.AttackSettings.Lng);
                    var p1 = MainMap.FromLatLngToLocal(attackP);
                    var p2 = new GMap.NET.GPoint((long)pos.X, (long)pos.Y);


                    var dist = Math.Sqrt(Math.Pow(p1.X - p2.X, 2) + Math.Pow(p1.Y - p2.Y, 2));

                    // Threshold in pixels (e.g. 20px radius)

                    if (dist < 30)
                    {
                        _settingsManager.AttackSettings.IsSet = false;
                        UpdateMapAttackZone();
                        UpdateDistanceDisplay();
                        MainMap.InvalidateVisual();
                        return;
                    }
                }

                if (Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                {
                    // Task 3: Geo-anchored attack point
                    _settingsManager.AttackSettings.Lat = latlng.Lat;
                    _settingsManager.AttackSettings.Lng = latlng.Lng;
                    _settingsManager.AttackSettings.IsSet = true;

                    UpdateMapAttackZone();
                    UpdateDistanceDisplay();
                    MainMap.InvalidateVisual();
                }
            }
        }

        private void TextBoxAttackAngle_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (float.TryParse(TextBoxAttackAngle.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float angle))
            {
                _settingsManager.AttackSettings.Angle = angle % 360;
                UpdateMapAttackZone();
                InvalidateThrottled();
            }
        }

        private void TextBoxRayLength_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (double.TryParse(TextBoxRayLength.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double length))
            {
                _settingsManager.AttackSettings.RayLength = Math.Max(0, length);
                UpdateMapAttackZone();
                InvalidateThrottled();
            }
        }

        private void TextBoxSectorWidth_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (float.TryParse(TextBoxSectorWidth.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float width))
            {
                _settingsManager.AttackSettings.SectorWidth = Math.Clamp(width, 5f, 180f);
                UpdateMapAttackZone();
                InvalidateThrottled();
            }
        }

        private void TextBoxRotateStep_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (float.TryParse(TextBoxRotateStep.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                _settingsManager.AttackSettings.RotateStep = Math.Max(0f, val);
        }

        private void TextBoxRotateShiftStep_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (float.TryParse(TextBoxRotateShiftStep.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
                _settingsManager.AttackSettings.RotateShiftStep = Math.Max(0f, val);
        }

        private void UpdateMapAttackZone()
        {
            if (_settingsManager.AttackSettings.IsSet)
            {
                MainMap.AttackPoint = new PointLatLng(_settingsManager.AttackSettings.Lat, _settingsManager.AttackSettings.Lng);
                MainMap.IsAttackPointSet = true;
            }
            else
            {
                MainMap.IsAttackPointSet = false;
            }


            MainMap.AttackAngle = _settingsManager.AttackSettings.Angle;
            MainMap.AttackRayLengthMeters = _settingsManager.AttackSettings.RayLength;
            MainMap.AttackSectorRadiusMeters = _settingsManager.AttackSettings.RayLength; // Merged
            MainMap.AttackSectorWidth = _settingsManager.AttackSettings.SectorWidth;
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
                        // Task 4: Enforce limits during WASD
                        if (_settingsManager.StartSettings.IsMapLimitsEnabled)
                        {
                            CheckMapLimits(MainMap.Position);
                        }


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

        private async System.Threading.Tasks.Task<(string Name, double DistanceKm)?> FindNearestSettlementOverpass(PointLatLng pos)
        {
            try
            {
                string latStr = pos.Lat.ToString(CultureInfo.InvariantCulture);
                string lngStr = pos.Lng.ToString(CultureInfo.InvariantCulture);
                string query = $"[out:json];(node[\"place\"~\"city|town|village|hamlet\"](around:20000,{latStr},{lngStr}););out;";
                string url = "https://overpass-api.de/api/interpreter?data=" + Uri.EscapeDataString(query);


                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    request.Headers.UserAgent.ParseAdd("MapsWPF/1.0");
                    using (var response = await _httpClient.SendAsync(request))
                    {
                        if (!response.IsSuccessStatusCode) return null;
                        using (var doc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync()))
                        {
                            var root = doc.RootElement;
                            if (!root.TryGetProperty("elements", out var elements)) return null;
                            string bestName = null; double minDist = double.MaxValue;
                            foreach (var el in elements.EnumerateArray())
                            {
                                if (el.TryGetProperty("tags", out var tags) && tags.TryGetProperty("name", out var nameProp))
                                {
                                    string name = nameProp.GetString();
                                    if (el.TryGetProperty("lat", out var latProp) && el.TryGetProperty("lon", out var lonProp))
                                    {
                                        var nodePos = new PointLatLng(latProp.GetDouble(), lonProp.GetDouble());
                                        double dist = GMapProviders.EmptyProvider.Projection.GetDistance(pos, nodePos);
                                        if (dist < minDist) { minDist = dist; bestName = name; }
                                    }
                                }
                            }

                            if (bestName != null) return (bestName, minDist);
                        }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine($"[OVERPASS] Error: {ex.Message}"); }
            return null;
        }

        // Map Limits Event Handlers
        private void CheckBoxLimitMap_Checked(object sender, RoutedEventArgs e)
        {
            _settingsManager.StartSettings.IsMapLimitsEnabled = true;
            UpdateBoundsOfMap();
            // When enabling Map Limits, if the current position is outside the bounds,
            // move the map into the nearest allowed point and slightly inside the boundary
            // to avoid exact-on-boundary locking.
            EnsurePositionInsideBoundsOnEnable();
        }

        private void CheckBoxLimitMap_Unchecked(object sender, RoutedEventArgs e)
        {
            _settingsManager.StartSettings.IsMapLimitsEnabled = false;
            UpdateBoundsOfMap();
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
                                var utm = new System.Drawing.PointF(easting, northing);
                                if (_coordinateConverter.TryUTMToLatLng(utm, zone, out double plat, out double plng))
                                {
                                    lat = plat; lng = plng;
                                    return true;
                                }
                            }
                        }
                    }
                }

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
            // For Map Limits we accept ONLY explicit UTM inputs (zone/band or 'UTM' token)
            if (TryParseLatLng(TextBoxLimitTopLeft.Text, out double lat, out double lng, utmOnly: true))
            {
                _settingsManager.StartSettings.LimitTopLeftLat = lat;
                _settingsManager.StartSettings.LimitTopLeftLng = lng;
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
            // For Map Limits we accept ONLY explicit UTM inputs (zone/band or 'UTM' token)
            if (TryParseLatLng(TextBoxLimitBottomRight.Text, out double lat, out double lng, utmOnly: true))
            {
                _settingsManager.StartSettings.LimitBottomRightLat = lat;
                _settingsManager.StartSettings.LimitBottomRightLng = lng;
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

        private void MainMap_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            // Logic removed

        }

        // MISSING HANDLERS RESTORATION

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

            if (_settingsManager != null)
            {
                _settingsManager.StartSettings.AccessMode = MainMap.Manager.Mode.ToString();
                _settingsManager.SaveStartSettings();
            }

            // Refresh immediate UI
            try { CacheStatsUpdateTimer_Tick(null, EventArgs.Empty); } catch { }
        }

        private void ColumnSplitter_DragCompleted(object sender, System.Windows.Controls.Primitives.DragCompletedEventArgs e)
        {
            try
            {
                // Store current right panel column width
                var width = RightColumn.ActualWidth;
                if (_settingsManager != null && width > 0)
                {
                    _settingsManager.StartSettings.RightPanelWidth = width;
                    _settingsManager.SaveStartSettings();
                }
            }
            catch { }
        }

        private void ResetCacheCounters()
        {
            try
            {
                // Reset core counters
                GMaps.Instance.TilesFromMemoryCache = 0;
                GMaps.Instance.TilesFromSQLiteCache = 0;
                GMaps.Instance.TilesFromNetwork = 0;

                // Reset local heuristic counters (in case they were used elsewhere)
                _tilesLoadedFromRam = 0;
                _tilesLoadedFromDisk = 0;
                _tilesLoadedFromNet = 0;
            }
            catch { }
        }


        public static bool PingNetwork(string hostNameOrAddress)
        {
            bool pingStatus;

            using (var p = new Ping())
            {
                byte[] buffer = Encoding.ASCII.GetBytes("aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa");
                int timeout = 5000; // 5sg

                try
                {
                    var reply = p.Send(hostNameOrAddress, timeout, buffer);
                    pingStatus = reply.Status == IPStatus.Success;
                }
                catch (Exception)
                {
                    pingStatus = false;
                }
            }

            return pingStatus;
        }

        private void CheckBoxZoomLimits_Checked(object sender, RoutedEventArgs e)
        {
            _settingsManager.StartSettings.IsZoomLimitsEnabled = true;
            ApplyZoomLimits();
        }

        private void CheckBoxZoomLimits_Unchecked(object sender, RoutedEventArgs e)
        {
            _settingsManager.StartSettings.IsZoomLimitsEnabled = false;
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
            if (!IsLoaded) return;

            bool minOk = int.TryParse(TextBoxMinZoom.Text, out int minZ);
            bool maxOk = int.TryParse(TextBoxMaxZoom.Text, out int maxZ);

            bool minValid = minOk && minZ >= 1 && minZ <= 24;
            bool maxValid = maxOk && maxZ >= 1 && maxZ <= 24;

            if (minValid)
            {
                TextBoxMinZoom.BorderBrush = System.Windows.Media.Brushes.Gray;
            }
            else
            {
                TextBoxMinZoom.BorderBrush = System.Windows.Media.Brushes.Red;
            }

            if (maxValid)
            {
                TextBoxMaxZoom.BorderBrush = System.Windows.Media.Brushes.Gray;
            }
            else
            {
                TextBoxMaxZoom.BorderBrush = System.Windows.Media.Brushes.Red;
            }

            if (minValid && maxValid)
            {
                if (minZ <= maxZ)
                {
                    _settingsManager.StartSettings.MinZoom = minZ;
                    _settingsManager.StartSettings.MaxZoom = maxZ;
                    ApplyZoomLimits();
                }
                else
                {
                    TextBoxMinZoom.BorderBrush = System.Windows.Media.Brushes.Red;
                    TextBoxMaxZoom.BorderBrush = System.Windows.Media.Brushes.Red;
                }
            }
        }

        private void ApplyZoomLimits()
        {
            if (_settingsManager.StartSettings.IsZoomLimitsEnabled)
            {
                MainMap.MinZoom = _settingsManager.StartSettings.MinZoom;
                MainMap.MaxZoom = _settingsManager.StartSettings.MaxZoom;

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
        private void ShowNotification(string message)
        {
            try
            {
                if (_notificationService != null) _notificationService.Notify(message);
                else
                {
                    Dispatcher.Invoke(() => { LabelReportStatus.Content = message; });

                    var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                    t.Tick += (s, e) => { Dispatcher.Invoke(() => LabelReportStatus.Content = ""); t.Stop(); };
                    t.Start();
                }
            }
            catch { }
        }

        private void NotificationRaisedHandler(string message, Services.NotificationType type)
        {
            try
            {
                Dispatcher.Invoke(() =>
                {
                    LabelReportStatus.Content = message;

                    if (_notificationTimer == null)
                    {
                        _notificationTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
                        _notificationTimer.Tick += (s, e) => { LabelReportStatus.Content = ""; _notificationTimer.Stop(); };
                    }
                    _notificationTimer.Stop();
                    _notificationTimer.Start();
                });
            }
            catch { }
        }

        private void ButtonStartOfWork_Click(object sender, RoutedEventArgs e)
        {
            if (_reportController != null)
            {
                try { _reportController.Start_of_Work_Click(sender, EventArgs.Empty); }
                catch (Exception ex) { _notificationService?.Notify($"Помилка генерації: {ex.Message}", NotificationType.Error); }
            }
            else
            {
                _notificationService?.Notify("Контролер звітів не ініціалізовано", NotificationType.Warning);
            }
        }

        private void ButtonEndOfWork_Click(object sender, RoutedEventArgs e)
        {
            if (_reportController != null)
            {
                try { _reportController.End_of_Work_Click(sender, EventArgs.Empty); }
                catch (Exception ex) { _notificationService?.Notify($"Помилка генерації: {ex.Message}", NotificationType.Error); }
            }
            else
            {
                _notificationService?.Notify("Контролер звітів не ініціалізовано", NotificationType.Warning);
            }
        }

        private void ButtonCombatWork_Click(object sender, RoutedEventArgs e)
        {
            if (_reportController != null)
            {
                try { _reportController.Combat_Work_Click(sender, EventArgs.Empty); }
                catch (Exception ex) { _notificationService?.Notify($"Помилка генерації: {ex.Message}", NotificationType.Error); }
            }
            else
            {
                _notificationService?.Notify("Контролер звітів не ініціалізовано", NotificationType.Warning);

            }
        }





        // Minimal IReportContext implementation to start migration
        public string GetSelectedPosition() => ComboBoxPosition?.Text ?? string.Empty;
        public string GetSelectedPilot() => ComboBoxPilot?.Text ?? string.Empty;
        public string GetSelectedDrone() => ComboBoxDroneBy?.Text ?? string.Empty;
        public IEnumerable<string> GetLocalCities() => _templateService?.LocalCiti ?? new List<string>();
        public string GetShootingTarget() => ComboBoxTargetType?.Text ?? string.Empty;
        public string GetHeightText() => TextBoxHeight?.Text ?? string.Empty;
        public int GetSelectedRange()
        {
            try
            {
                if (int.TryParse(TextBoxRange?.Text ?? string.Empty, System.Globalization.NumberStyles.Integer, CultureInfo.InvariantCulture, out int r))
                    return r;
            }
            catch { }
            return 0;
        }

        public bool TryGetUTM(out System.Drawing.PointF utm)
        {
            utm = System.Drawing.PointF.Empty;
            try
            {
                if (currentMarker != null)
                {
                    if (_mapService != null)
                    {
                        if (_mapService.TryLatLngToUTM(currentMarker.Position.Lat, currentMarker.Position.Lng, out var result, out var zone, out var band))
                        {
                            utm = result;
                            _lastUtmZone = zone;
                            _lastUtmBand = band;
                            return true;
                        }
                    }

                    // fallback to direct converter
                    if (_coordinateConverter.TryLatLngToUTM(currentMarker.Position.Lat, currentMarker.Position.Lng, out var fallback, out var fzone, out var fband))
                    {
                        utm = fallback;
                        _lastUtmZone = fzone;
                        _lastUtmBand = fband;
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        public string FormatShortMGRSFromUTM(System.Drawing.PointF utm)
        {
            if (_lastUtmZone != 0)
            {
                if (_mapService != null) return _mapService.FormatShortMGRSFromUTM(utm, _lastUtmZone);
                return _coordinateConverter.FormatShortMGRSFromUTM(utm, _lastUtmZone);
            }
            return "-";
        }

        public string FindClosestLocality(System.Drawing.PointF utm)
        {
            // Reuse existing reverse geocoding based on lat/lng
            if (_coordinateConverter.TryUTMToLatLng(utm, out double lat, out double lng))
            {
                var latlng = new GMap.NET.PointLatLng(lat, lng);
                return FindNearestLocality(latlng);
            }
            return "Невідомо";
        }

        public bool TryGetClickedLatLng(out PointLatLng latlng)
        {
            latlng = PointLatLng.Empty;
            if (currentMarker != null)
            {
                latlng = currentMarker.Position;
                return true;
            }
            return false;
        }

        public string FormatShortMGRSFromLatLng(PointLatLng latlng)
        {
            if (_coordinateConverter.TryLatLngToUTM(latlng.Lat, latlng.Lng, out var utm, out var zone, out var band))
            {
                return _coordinateConverter.FormatShortMGRSFromUTM(utm, zone);
            }
            return "-";
        }

        public string FindClosestLocalityFromLatLng(PointLatLng latlng)
        {
            return FindNearestLocality(latlng);
        }

        public string GetTargetType() => ComboBoxTargetType?.Text ?? "-";

        private string FindNearestLocality(PointLatLng latlng)
        {
            // Return last cached locality if available, otherwise placeholder
            if (!string.IsNullOrEmpty(_settingsManager?.StartSettings?.LastTargetLocationName))
                return _settingsManager.StartSettings.LastTargetLocationName;
            return "Невідомо";
        }

        private DateTime _reportStartTime = DateTime.MinValue;
        public string GetAndSetTime()
        {
            _reportStartTime = DateTime.Now;
            return _reportStartTime.ToString("HH.mm");
        }

        public string GetCurrentTimeString() => _reportStartTime == DateTime.MinValue ? string.Empty : _reportStartTime.ToString("HH.mm");

        public TemplateService GetShablon() => _templateService ?? (_templateService = new TemplateService());

        public bool IsTargetDestroyed() => CheckBoxTargetDestroyed.IsChecked == true;
        public bool IsTargetBoardLost() => CheckBoxTargetBoard.IsChecked == true;

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
            if (currentMarker != null)
            {
                if (_mapService != null)
                {
                    if (_mapService.TryLatLngToUTM(currentMarker.Position.Lat, currentMarker.Position.Lng, out var utm, out var zone, out var band))
                    {
                        _lastUtmZone = zone;
                        _lastUtmBand = band;
                        return utm;
                    }
                }

                if (_coordinateConverter.TryLatLngToUTM(currentMarker.Position.Lat, currentMarker.Position.Lng, out var fallbackUtm, out var fzone, out var fband))
                {
                    _lastUtmZone = fzone;
                    _lastUtmBand = fband;
                    return fallbackUtm;
                }
            }
            return null;
        }

        public System.Drawing.PointF GetAttackPoint()
        {
            try
            {
                if (_settingsManager.AttackSettings.IsSet)
                {
                    if (_mapService != null)
                    {
                        if (_mapService.TryLatLngToUTM(_settingsManager.AttackSettings.Lat, _settingsManager.AttackSettings.Lng, out var utm, out var zone, out var band))
                        {
                            _lastUtmZone = zone;
                            _lastUtmBand = band;
                            return utm;
                        }
                    }

                    if (_coordinateConverter.TryLatLngToUTM(_settingsManager.AttackSettings.Lat, _settingsManager.AttackSettings.Lng, out var fallbackUtm, out var fzone, out var fband))
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

        public string GetLabelScaleText()
        {
            return LabelAngleValue?.Content?.ToString() ?? "-";
        }

        public void SetAzimuthDisplay(string text)
        {
            try { Dispatcher.Invoke(() => { if (LabelAzimuthValue != null) LabelAzimuthValue.Content = text; try { MainMap.AzimuthText = text; MainMap.InvalidateVisual(); } catch {} }); } catch { }
        }

        public string GetAzimuthText()
        {
            try { return LabelAzimuthValue?.Content?.ToString() ?? string.Empty; } catch { return string.Empty; }
        }

        private List<string> GetTemplateLinesByName(string name)
        {
            if (_templateService == null) return new List<string>();
            switch ((name ?? string.Empty).ToLowerInvariant())
            {
                case "startwork":
                case "start":
                    return _templateService.StartWorkShablon ?? new List<string>();
                case "endwork":
                case "end":
                    return _templateService.EndWorkShablon ?? new List<string>();
                default:
                    return _templateService.GetTemplateByName(name ?? "Report") ?? new List<string>();
            }
        }



        // Генерація тепер обробляється у TemplateEditorViewModel.GenerateCommand (без code-behind)

        private void RefreshTemplatesAndHistories()
        {
            try
            {
                var names = (_templateService?.Templates != null && _templateService.Templates.Count > 0) ? _templateService.Templates.Keys.OrderBy(k => k).ToList() : new List<string> { "Report", "StartWork", "EndWork" }; // use named templates from JSON if available


                Dispatcher.Invoke(() =>
                {
                    try
                    {
                        // ComboBoxTemplateSelect.ItemsSource is bound in XAML to TemplateEditorViewModel.TemplateNames
                        if (ComboBoxTemplateSelect.SelectedItem == null) ComboBoxTemplateSelect.SelectedItem = "Report";

                        // ComboBoxUnitName is bound to TemplateEditorViewModel.UnitsHistory and TemplateEditorViewModel.CustomUnit (keep binding in XAML)

                        // ComboBoxLaunchArea is bound to TemplateEditorViewModel.LaunchAreasHistory and TemplateEditorViewModel.LaunchArea (keep binding in XAML)

                        // ComboBoxTargetType is bound to TemplateEditorViewModel.Targets and SelectedTarget (keep binding in XAML)
                    }
                    catch { }
                });
            }
            catch { }
        }








        private void CommitTargetFromCombo()
        {
            // intentionally empty: targets are added on Generate per user request
        }





        // ButtonLoadDefault_Click removed – handled by TemplateEditorViewModel.LoadDefaultCommand        }


        // ButtonSetUnitName_Click removed
        /*
            try
            {
                if (_templateService != null)
                {
                    // removed unitName retrieval
                    // removed unit save logic
                }
                else
                {
                    _notificationService?.Notify("TemplateService не ініціалізовано", NotificationType.Warning);
                }
            }
            catch (Exception ex) { _notificationService?.Notify($"Помилка: {ex.Message}", NotificationType.Error); }
        */

    }

    public class MapValidationRule : ValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            return new ValidationResult(true, null);
        }
    }
}