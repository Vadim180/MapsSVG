using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsPresentation;

using MapsWPF.CustomMarkers;
using MapsWPF.Models;
using MapsWPF.Services;

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

namespace MapsWPF
{
    public partial class MainWindow : Window
    {
        // marker
        GMapMarker currentMarker;

        // zones list
        List<GMapMarker> Circles = [];

        // coordinate converter
        private CoordinateConverter _coordinateConverter = new();

        // Geocoding cancellation token
        private CancellationTokenSource _geocodingCts;

        // Cache statistics debouncing
        private DispatcherTimer _cacheStatsUpdateTimer;
        private int _lastMemCache = -1;
        private int _lastSqliteCache = -1;
        private int _lastNetwork = -1;


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

            // set cache mode only if no internet
            if (!PingNetwork("google.com"))
            {
                MainMap.Manager.Mode = AccessMode.CacheOnly;
                MessageBox.Show("No internet connection available, going to CacheOnly mode.",
                    "MapsWPF",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            // Load startup settings
            _settingsManager = new SettingsManager();

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

            // Load Expander states
            ExpanderCoordinates.IsExpanded = _settingsManager.StartSettings.IsCoordinatesExpanded;
            ExpanderGmap.IsExpanded = _settingsManager.StartSettings.IsGmapExpanded;
            ExpanderCache.IsExpanded = _settingsManager.StartSettings.IsCacheExpanded;
            ExpanderGo.IsExpanded = _settingsManager.StartSettings.IsGoExpanded;
            ExpanderRay.IsExpanded = _settingsManager.StartSettings.IsRayExpanded;
            ExpanderTarget.IsExpanded = _settingsManager.StartSettings.IsTargetExpanded;
            ExpanderMapLimits.IsExpanded = _settingsManager.StartSettings.IsMapLimitsExpanded;

            // Load Map Limits UI
            CheckBoxLimitMap.IsChecked = _settingsManager.StartSettings.IsMapLimitsEnabled;
            if (_settingsManager.StartSettings.LimitTopLeftLat.HasValue && _settingsManager.StartSettings.LimitTopLeftLng.HasValue)
                TextBoxLimitTopLeft.Text = $"{_settingsManager.StartSettings.LimitTopLeftLat.Value.ToString(CultureInfo.InvariantCulture)}, {_settingsManager.StartSettings.LimitTopLeftLng.Value.ToString(CultureInfo.InvariantCulture)}";


            if (_settingsManager.StartSettings.LimitBottomRightLat.HasValue && _settingsManager.StartSettings.LimitBottomRightLng.HasValue)
                TextBoxLimitBottomRight.Text = $"{_settingsManager.StartSettings.LimitBottomRightLat.Value.ToString(CultureInfo.InvariantCulture)}, {_settingsManager.StartSettings.LimitBottomRightLng.Value.ToString(CultureInfo.InvariantCulture)}";

            // Initialize map bounds from settings
            UpdateBoundsOfMap();

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
                        var mgrsStr = _coordinateConverter.FormatShortMGRSFromUTM(utm);
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

            CheckBoxCacheRoute.IsChecked = MainMap.Manager.UseRouteCache;
            CheckBoxGeoCache.IsChecked = MainMap.Manager.UseGeocoderCache;

            TextBoxLat.Text = MainMap.Position.Lat.ToString(CultureInfo.InvariantCulture);
            TextBoxLng.Text = MainMap.Position.Lng.ToString(CultureInfo.InvariantCulture);

            CheckBoxCurrentMarker.IsChecked = true;
            CheckBoxDragMap.IsChecked = MainMap.CanDragMap;
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

            // Save Expander states
            _settingsManager.StartSettings.IsCoordinatesExpanded = ExpanderCoordinates.IsExpanded;
            _settingsManager.StartSettings.IsGmapExpanded = ExpanderGmap.IsExpanded;
            _settingsManager.StartSettings.IsCacheExpanded = ExpanderCache.IsExpanded;
            _settingsManager.StartSettings.IsGoExpanded = ExpanderGo.IsExpanded;
            _settingsManager.StartSettings.IsRayExpanded = ExpanderRay.IsExpanded;
            _settingsManager.StartSettings.IsTargetExpanded = ExpanderTarget.IsExpanded;
            _settingsManager.StartSettings.IsMapLimitsExpanded = ExpanderMapLimits.IsExpanded;

            // Save Window State
            if (this.WindowState == System.Windows.WindowState.Normal)
            {
                _settingsManager.StartSettings.WindowTop = this.Top;
                _settingsManager.StartSettings.WindowLeft = this.Left;
                _settingsManager.StartSettings.WindowWidth = this.Width;
                _settingsManager.StartSettings.WindowHeight = this.Height;
            }
            _settingsManager.StartSettings.WindowState = (int)this.WindowState;

            // Map Limits settings are updated in their respective event handlers/setters, 
            // but saving calls SaveStartSettings for all.
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


            _settingsManager.AttackSettings.PropertyChanged += AttackSettings_PropertyChanged;
        }

        private void AttackSettings_PropertyChanged(object sender, PropertyChangedEventArgs e)
        {
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
                    LabelDistance.Content = "Distance: -";

                    // Task 1: Clear fields on delete

                    LabelTargetLocation.Content = "Населений пункт: -";
                    LabelTargetAddress.Content = "Адреса: -";
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

        void AddDemoZone(double areaRadius, PointLatLng center, List<PointAndInfo> objects)
        {
            // Demo zone logic preserved but unused in default path
        }

        void UpdateCircle(Circle c)
        {
            // Circle update logic preserved

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


                LabelDistance.Content = $"Distance: {distM:F0} m";
                MainMap.TargetDistance = distM;
                MainMap.InvalidateVisual();
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
                LabelTargetLocation.Content = $"Населений пункт: {_settingsManager.StartSettings.LastTargetLocationName}";
                LabelTargetAddress.Content = $"Адреса: {_settingsManager.StartSettings.LastTargetAddress}";
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
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() => ProgressBar1.Visibility = Visibility.Visible));
        }

        void MainMap_OnTileLoadComplete(long elapsedMilliseconds)
        {
            MainMap.ElapsedMilliseconds = elapsedMilliseconds;
            Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
            {
                ProgressBar1.Visibility = Visibility.Hidden;
                GroupBox3.Header = "Loading: " + MainMap.ElapsedMilliseconds + "ms";
                if (!_cacheStatsUpdateTimer.IsEnabled) _cacheStatsUpdateTimer.Start();
                else { _cacheStatsUpdateTimer.Stop(); _cacheStatsUpdateTimer.Start(); }
            }));
        }

        private void CacheStatsUpdateTimer_Tick(object sender, EventArgs e)
        {
            _cacheStatsUpdateTimer.Stop();
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

        void MainMap_OnCurrentPositionChanged(PointLatLng point)
        {
            CheckMapLimits(point);

            LabelLatLng.Content = "Lat: " + point.Lat.ToString("F8", CultureInfo.InvariantCulture) + ", Lng: " + point.Lng.ToString("F8", CultureInfo.InvariantCulture);

            if (_coordinateConverter.TryLatLngToUTM(point.Lat, point.Lng, out System.Drawing.PointF utm, out int utmZone, out char bandLetter))
            {
                LabelUTM.Content = _coordinateConverter.FormatUTM(utm, utmZone, bandLetter);
                LabelMGRS.Content = "MGRS: " + _coordinateConverter.FormatShortMGRSFromUTM(utm);
            }
            else
            {
                LabelUTM.Content = "UTM: Помилка конвертації";
                LabelMGRS.Content = "MGRS: Помилка конвертації";
            }

            LabelZoom.Content = "Zoom: " + MainMap.Zoom.ToString(CultureInfo.InvariantCulture);
        }

        void MainMap_OnMapZoomChanged()
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
            {
                LabelZoom.Content = "Zoom: " + MainMap.Zoom.ToString(CultureInfo.InvariantCulture);
            }));
        }

        void MainMap_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
            {
                LabelZoom.Content = "Zoom: " + MainMap.Zoom.ToString(CultureInfo.InvariantCulture);
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
                string toCopy = text;
                if (lbl.Name == "LabelLatLng")
                {
                    var matches = Regex.Matches(text, "-?\\d+[\\.,]?\\d*");
                    if (matches.Count >= 2)
                    {
                        var a = matches[0].Value.Replace(',', '.');
                        var b = matches[1].Value.Replace(',', '.');
                        toCopy = a + ", " + b;
                    }
                }
                else if (lbl.Name == "LabelZoom") return;
                else
                {
                    int idx = text.IndexOf(':');
                    if (idx >= 0) toCopy = text.Substring(idx + 1).Trim();
                }

                try { Clipboard.SetText(toCopy); } catch { }
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
            CheckMapLimits(MainMap.Position);
        }

        private void CheckBoxLimitMap_Unchecked(object sender, RoutedEventArgs e)
        {
            _settingsManager.StartSettings.IsMapLimitsEnabled = false;
            UpdateBoundsOfMap();
        }

        // Robust coordinate parser
        private bool TryParseLatLng(string text, out double lat, out double lng)
        {
            lat = 0; lng = 0;
            if (string.IsNullOrWhiteSpace(text)) return false;

            try
            {
                // Find all numbers (supports dot or comma decimal)
                var matches = Regex.Matches(text, @"-?\d+(?:[.,]\d+)?");
                if (matches.Count >= 2)
                {
                    // Normalize decimal separator to dot
                    string sLat = matches[0].Value.Replace(',', '.');
                    string sLng = matches[1].Value.Replace(',', '.');

                    if (double.TryParse(sLat, NumberStyles.Any, CultureInfo.InvariantCulture, out lat) &&
                        double.TryParse(sLng, NumberStyles.Any, CultureInfo.InvariantCulture, out lng))
                    {
                        return true;
                    }
                }
            }
            catch { }
            return false;
        }

        private void TextBoxLimitTopLeft_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TryParseLatLng(TextBoxLimitTopLeft.Text, out double lat, out double lng))
            {
                _settingsManager.StartSettings.LimitTopLeftLat = lat;
                _settingsManager.StartSettings.LimitTopLeftLng = lng;
                UpdateBoundsOfMap();
                TextBoxLimitTopLeft.BorderBrush = System.Windows.Media.Brushes.Green;
            }
            else
            {
                TextBoxLimitTopLeft.BorderBrush = System.Windows.Media.Brushes.Red;
            }
        }

        private void TextBoxLimitBottomRight_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TryParseLatLng(TextBoxLimitBottomRight.Text, out double lat, out double lng))
            {
                _settingsManager.StartSettings.LimitBottomRightLat = lat;
                _settingsManager.StartSettings.LimitBottomRightLng = lng;
                UpdateBoundsOfMap();
                TextBoxLimitBottomRight.BorderBrush = System.Windows.Media.Brushes.Green;
            }
            else
            {
                TextBoxLimitBottomRight.BorderBrush = System.Windows.Media.Brushes.Red;
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

        private void buttonInsertCoords_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var pos = MainMap.Position;
                TextBoxLat.Text = pos.Lat.ToString(CultureInfo.InvariantCulture);
                TextBoxLng.Text = pos.Lng.ToString(CultureInfo.InvariantCulture);
            }
            catch (Exception ex)
            {
                Debug.WriteLine("Insert coords failed: " + ex.Message);
            }
        }

        private void buttonCacheDiag_Click(object sender, RoutedEventArgs e)
        {
            var w = new CacheStatsWindow();
            w.Owner = this;
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
        }

        private void CheckBoxShowCoordinates_Unchecked(object sender, RoutedEventArgs e)
        {
            MainMap.ShowCoordinates = false;
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

    }

    public class MapValidationRule : ValidationRule
    {
        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            return new ValidationResult(true, null);
        }
    }
}