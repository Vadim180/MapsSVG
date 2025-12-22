using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Drawing;
using System.Text.RegularExpressions;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Demo.WindowsForms;
using MapsWPF.CustomMarkers;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsPresentation;

namespace MapsWPF
{
    public partial class MainWindow : Window
    {
        // routing (set start/end/add route) removed

        // marker
        GMapMarker currentMarker;

        // zones list
        List<GMapMarker> Circles = new List<GMapMarker>();

        // coordinate converter
        private CoordinateConverter _coordinateConverter = new CoordinateConverter();

        // Cache statistics debouncing
        private DispatcherTimer _cacheStatsUpdateTimer;
        private int _lastMemCache = -1;
        private int _lastSqliteCache = -1;
        private int _lastNetwork = -1;

        public MainWindow()
        {
            InitializeComponent();

            // Initialize cache statistics update timer (debounced to 500ms)
            _cacheStatsUpdateTimer = new DispatcherTimer();
            _cacheStatsUpdateTimer.Interval = TimeSpan.FromMilliseconds(500);
            _cacheStatsUpdateTimer.Tick += CacheStatsUpdateTimer_Tick;

            // add your custom map db provider
            //MySQLPureImageCache ch = new MySQLPureImageCache();
            //ch.ConnectionString = @"server=sql2008;User Id=trolis;Persist Security Info=True;database=gmapnetcache;password=trolis;";
            //MainMap.Manager.SecondaryCache = ch;

            // set your proxy here if need
            //GMapProvider.IsSocksProxy = true;
            //GMapProvider.WebProxy = new WebProxy("127.0.0.1", 1080);
            //GMapProvider.WebProxy.Credentials = new NetworkCredential("ogrenci@bilgeadam.com", "bilgeada");
            // or
            //GMapProvider.WebProxy = WebRequest.DefaultWebProxy;
            //

            // set cache mode only if no internet avaible
            if (!Stuff.PingNetwork("google.com"))
            {
                MainMap.Manager.Mode = AccessMode.CacheOnly;
                MessageBox.Show("No internet connection available, going to CacheOnly mode.",
                    "MapsWPF",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
            }

            GoogleMapProvider.Instance.ApiKey = Stuff.GoogleMapsApiKey;

            // config map (default: Google Hybrid, default position: Kyiv)
            MainMap.MapProvider = GMapProviders.GoogleHybridMap;
            MainMap.Position = new PointLatLng(50.4501, 30.52001953125); // Kyiv

            // ensure zoom is set to default 7 (some providers may reset zoom during initialization)
            MainMap.Zoom = 7;

            // PERFORMANCE: Throttling for smooth drag operations
            // UpdateBounds is throttled to prevent lag during map panning (default: 150ms)
            // Uncomment to adjust if needed:
            // MainMap.Manager.Core.UpdateBoundsThrottleMs = 100; // More responsive (50-100ms)
            // MainMap.Manager.Core.UpdateBoundsThrottleMs = 200; // Smoother on slow PCs (200-300ms)

            MainMap.TouchEnabled = false;
            MainMap.MultiTouchEnabled = true;
            
            //// 20200313 (jokubokla): Demo of the new Sweden Map with Mercator instead of SWEREF99
            //MainMap.MapProvider = GMapProviders.SwedenMapAlternative;
            //MainMap.Position = new PointLatLng(58.406298501604, 15.5825614929199); // Linköping
            //MainMap.MinZoom = 1;
            //MainMap.MaxZoom = 15;
            //MainMap.Zoom = 11;
            //TextBoxGeo.Text = "Linköping";

            //MainMap.ScaleMode = ScaleModes.Dynamic;

            //-- map events
            MainMap.OnPositionChanged += MainMap_OnCurrentPositionChanged;
            MainMap.OnMapZoomChanged += MainMap_OnMapZoomChanged;
            MainMap.OnTileLoadComplete += MainMap_OnTileLoadComplete;
            MainMap.OnTileLoadStart += MainMap_OnTileLoadStart;
            MainMap.OnMapTypeChanged += MainMap_OnMapTypeChanged;
            MainMap.MouseMove += MainMap_MouseMove;
            MainMap.MouseRightButtonDown += MainMap_MouseRightButtonDown; // place marker with right click
            MainMap.MouseEnter += MainMap_MouseEnter;
            MainMap.MouseWheel += MainMap_MouseWheel;
            MainMap.Loaded += MainMap_Loaded; // ensure default zoom after control initialization

            // initialize WASD timer for smooth continuous panning on hold
            _wasdTimer = new DispatcherTimer();
            _wasdTimer.Interval = TimeSpan.FromMilliseconds(_wasdTickIntervalMs);
            _wasdTimer.Tick += WasdTimer_Tick;

            // Provide CoordinateFormatter so Map overlay can show UTM / MGRS like WinForms app
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

            // get map types (order: Google (Hybrid first), Bing (Hybrid first), OpenStreet, Others)
            var providers = GMapProviders.List.ToList();

            Func<GMapProvider, bool> isGoogle = p => p.Name.IndexOf("google", StringComparison.OrdinalIgnoreCase) >= 0;
            Func<GMapProvider, bool> isBing = p => p.Name.IndexOf("bing", StringComparison.OrdinalIgnoreCase) >= 0;
            Func<GMapProvider, bool> isOSM = p => p.Name.IndexOf("openstreet", StringComparison.OrdinalIgnoreCase) >= 0 || p.Name.IndexOf("open street", StringComparison.OrdinalIgnoreCase) >= 0 || p.Name.IndexOf("osm", StringComparison.OrdinalIgnoreCase) >= 0;

            Func<GMapProvider, bool> isChina = p => p.Name.IndexOf("china", StringComparison.OrdinalIgnoreCase) >= 0;
            Func<GMapProvider, bool> isHybrid = p => p.Name.IndexOf("hybrid", StringComparison.OrdinalIgnoreCase) >= 0;

            var google = providers.Where(isGoogle)
                .OrderBy(p => isHybrid(p) ? 0 : (isChina(p) ? 2 : 1))
                .ThenBy(p => p.Name)
                .ToList();

            var bing = providers.Where(isBing)
                .OrderBy(p => isHybrid(p) ? 0 : (isChina(p) ? 2 : 1))
                .ThenBy(p => p.Name)
                .ToList();

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

            // acccess mode
            ComboBoxMode.ItemsSource = Enum.GetValues(typeof(AccessMode));
            ComboBoxMode.SelectedItem = MainMap.Manager.Mode;

            // get cache modes
            CheckBoxCacheRoute.IsChecked = MainMap.Manager.UseRouteCache;
            CheckBoxGeoCache.IsChecked = MainMap.Manager.UseGeocoderCache;

            // setup zoom min/max (zoom control removed)

            // get position
            TextBoxLat.Text = MainMap.Position.Lat.ToString(CultureInfo.InvariantCulture);
            TextBoxLng.Text = MainMap.Position.Lng.ToString(CultureInfo.InvariantCulture);

            // get marker state
            CheckBoxCurrentMarker.IsChecked = true;

            // can drag map
            CheckBoxDragMap.IsChecked = MainMap.CanDragMap;

#if DEBUG
            CheckBoxDebug.IsChecked = true;
#endif

            //validator.Window = this;

            // set current marker
            currentMarker = new GMapMarker(MainMap.Position);
            {
                currentMarker.Shape = new CustomMarkerRed(this, currentMarker, "custom position marker");
                currentMarker.Offset = new System.Windows.Point(-15, -15);
                currentMarker.ZIndex = int.MaxValue;
                MainMap.Markers.Add(currentMarker);
            }

            //if(false)
            {
                // add my city location for demo
                GeoCoderStatusCode status;

                var city = GMapProviders.GoogleMap.GetPoint("Lithuania, Vilnius", out status);
                if (city != null && status == GeoCoderStatusCode.OK)
                {
                    var it = new GMapMarker(city.Value);
                    {
                        it.ZIndex = 55;
                        it.Shape = new CustomMarkerDemo(this, it, "Welcome to Lithuania! ;}");
                    }
                    MainMap.Markers.Add(it);

                    #region -- add some markers and zone around them --

                    //if(false)
                    {
                        var objects = new List<PointAndInfo>();
                        {
                            string area = "Antakalnis";
                            var pos = GMapProviders.GoogleMap.GetPoint("Lithuania, Vilnius, " + area, out status);
                            if (pos != null && status == GeoCoderStatusCode.OK)
                            {
                                objects.Add(new PointAndInfo(pos.Value, area));
                            }
                        }
                        {
                            string area = "Senamiestis";
                            var pos = GMapProviders.GoogleMap.GetPoint("Lithuania, Vilnius, " + area, out status);
                            if (pos != null && status == GeoCoderStatusCode.OK)
                            {
                                objects.Add(new PointAndInfo(pos.Value, area));
                            }
                        }
                        {
                            string area = "Pilaite";
                            var pos = GMapProviders.GoogleMap.GetPoint("Lithuania, Vilnius, " + area, out status);
                            if (pos != null && status == GeoCoderStatusCode.OK)
                            {
                                objects.Add(new PointAndInfo(pos.Value, area));
                            }
                        }
                        AddDemoZone(8.8, city.Value, objects);
                    }

                    #endregion
                }

                if (MainMap.Markers.Count > 1)
                {
                    MainMap.ZoomAndCenterMarkers(null);
                }

                    // enforce default zoom and update marker/text fields
                    MainMap.Zoom = 7; // default zoom level per project settings
                    currentMarker.Position = MainMap.Position;
                    TextBoxLat.Text = MainMap.Position.Lat.ToString(CultureInfo.InvariantCulture);
                    TextBoxLng.Text = MainMap.Position.Lng.ToString(CultureInfo.InvariantCulture);

                    // Attack zone: Subscribe to mouse events and load saved point
                    MainMap.MouseLeftButtonDown += MainMap_MouseLeftButtonDown;
                    LoadAttackPoint();

                    // Initialize attack zone text boxes with default values
                    TextBoxAttackAngle.Text = _attackAngle.ToString("F2");
                    TextBoxRayLength.Text = _attackRayLength.ToString("F0");
                    TextBoxSectorRadius.Text = _attackSectorRadius.ToString("F0");
                    TextBoxSectorWidth.Text = _attackSectorWidth.ToString("F2");
                    // Initialize rotate step inputs
                    TextBoxRotateStep.Text = rotateAngleStep.ToString("F3", CultureInfo.InvariantCulture);
                    TextBoxRotateShiftStep.Text = rotateAngelShiftStep.ToString("F3", CultureInfo.InvariantCulture);
                }

            // performance test removed

            // transport demo removed
        }

        void MainMap_MouseEnter(object sender, MouseEventArgs e)
        {
            MainMap.Focus();
        }

        #region -- performance test--

        public RenderTargetBitmap ToImageSource(FrameworkElement obj)
        {
            // Save current canvas transform
            var transform = obj.LayoutTransform;
            obj.LayoutTransform = null;

            // fix margin offset as well
            var margin = obj.Margin;
            obj.Margin = new Thickness(0, 0, margin.Right - margin.Left, margin.Bottom - margin.Top);

            // Get the size of canvas
            var size = new System.Windows.Size(obj.Width, obj.Height);

            // force control to Update
            obj.Measure(size);
            obj.Arrange(new Rect(size));

            var bmp = new RenderTargetBitmap((int)size.Width, (int)size.Height, 96, 96, PixelFormats.Pbgra32);
            bmp.Render(obj);

            if (bmp.CanFreeze)
            {
                bmp.Freeze();
            }

            // return values as they were before
            obj.LayoutTransform = transform;
            obj.Margin = margin;

            return bmp;
        }

        double NextDouble(Random rng, double min, double max)
        {
            return min + rng.NextDouble() * (max - min);
        }

        Random r = new Random();

        // Performance test removed (timer and helper methods removed)

        #endregion

        // Transport demo removed

        // BackgroundWorker transport removed (transport demo deleted)

        // transport collections removed

        // transport_ProgressChanged removed (transport demo deleted)

        // transport demo removed

        void MainMap_Loaded(object sender, RoutedEventArgs e)
        {
            // enforce defaults once control is loaded (some providers may change settings during init)
            MainMap.Zoom = 7;
            MainMap.Position = new PointLatLng(50.4501, 30.52001953125); // Kyiv

            TextBoxLat.Text = MainMap.Position.Lat.ToString(CultureInfo.InvariantCulture);
            TextBoxLng.Text = MainMap.Position.Lng.ToString(CultureInfo.InvariantCulture);
            currentMarker.Position = MainMap.Position;
        }

        // add objects and zone around them
        void AddDemoZone(double areaRadius, PointLatLng center, List<PointAndInfo> objects)
        {
            var objectsInArea = from p in objects
                where MainMap.MapProvider.Projection.GetDistance(center, p.Point) <= areaRadius
                select new {Obj = p, Dist = MainMap.MapProvider.Projection.GetDistance(center, p.Point)};
            if (objectsInArea.Any())
            {
                var maxDistObject = (from p in objectsInArea
                    orderby p.Dist descending
                    select p).First();

                // add objects to zone
                foreach (var o in objectsInArea)
                {
                    var it = new GMapMarker(o.Obj.Point);
                    {
                        it.ZIndex = 55;
                        var s = new CustomMarkerDemo(this,
                            it,
                            o.Obj.Info + ", distance from center: " + o.Dist + "km.");
                        it.Shape = s;
                    }

                    MainMap.Markers.Add(it);
                }

                // add zone circle
                //if(false)
                {
                    var it = new GMapMarker(center);
                    it.ZIndex = -1;

                    var c = new Circle();
                    c.Center = center;
                    c.Bound = maxDistObject.Obj.Point;
                    c.Tag = it;
                    c.IsHitTestVisible = false;

                    UpdateCircle(c);
                    Circles.Add(it);

                    it.Shape = c;
                    MainMap.Markers.Add(it);
                }
            }
        }

        // calculates circle radius
        void UpdateCircle(Circle c)
        {
            var pxCenter = MainMap.FromLatLngToLocal(c.Center);
            var pxBounds = MainMap.FromLatLngToLocal(c.Bound);

            double a = pxBounds.X - pxCenter.X;
            double b = pxBounds.Y - pxCenter.Y;
            double pxCircleRadius = Math.Sqrt(a * a + b * b);

            c.Width = 55 + pxCircleRadius * 2;
            c.Height = 55 + pxCircleRadius * 2;
            (c.Tag as GMapMarker).Offset = new System.Windows.Point(-c.Width / 2, -c.Height / 2);
        }

        void MainMap_OnMapTypeChanged(GMapProvider type)
        {
            // zoom control removed; no slider to update
        }

        void MainMap_MouseRightButtonDown(object sender, MouseButtonEventArgs e)
        {
            // place marker with right-click
            var p = e.GetPosition(MainMap);
            currentMarker.Position = MainMap.FromLocalToLatLng((int)p.X, (int)p.Y);
        }

        // move current marker with right holding
        void MainMap_MouseMove(object sender, MouseEventArgs e)
        {
            if (e.RightButton == MouseButtonState.Pressed)
            {
                var p = e.GetPosition(MainMap);
                currentMarker.Position = MainMap.FromLocalToLatLng((int)p.X, (int)p.Y);
            }
        }

        // zoo max & center markers
        private void button13_Click(object sender, RoutedEventArgs e)
        {
            MainMap.ZoomAndCenterMarkers(null);

            /*
            PointAnimation panMap = new PointAnimation();
            panMap.Duration = TimeSpan.FromSeconds(1);
            panMap.From = new Point(MainMap.Position.Lat, MainMap.Position.Lng);
            panMap.To = new Point(0, 0);
            Storyboard.SetTarget(panMap, MainMap);
            Storyboard.SetTargetProperty(panMap, new PropertyPath(GMapControl.MapPointProperty));
   
            Storyboard panMapStoryBoard = new Storyboard();
            panMapStoryBoard.Children.Add(panMap);
            panMapStoryBoard.Begin(this);
             */
        }

        // tile louading starts
        void MainMap_OnTileLoadStart()
        {
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                    new Action(() =>
                    {
                        ProgressBar1.Visibility = Visibility.Visible;
                    }));
            }
            catch
            {
            }
        }

        // tile loading stops
        void MainMap_OnTileLoadComplete(long elapsedMilliseconds)
        {
            MainMap.ElapsedMilliseconds = elapsedMilliseconds;

            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Loaded,
                    new Action(() =>
                    {
                        ProgressBar1.Visibility = Visibility.Hidden;
                        GroupBox3.Header = "Loading: " + MainMap.ElapsedMilliseconds + "ms";

                        // Start/restart debounced cache statistics update timer
                        if (!_cacheStatsUpdateTimer.IsEnabled)
                        {
                            _cacheStatsUpdateTimer.Start();
                        }
                        else
                        {
                            _cacheStatsUpdateTimer.Stop();
                            _cacheStatsUpdateTimer.Start();
                        }
                    }));
            }
            catch
            {
            }
        }

        // Debounced cache statistics update (called max once per 500ms)
        private void CacheStatsUpdateTimer_Tick(object sender, EventArgs e)
        {
            _cacheStatsUpdateTimer.Stop();

            try
            {
                        // Get current statistics
                        int memCache = MainMap.Manager.TilesFromMemoryCache;
                        int sqliteCache = MainMap.Manager.TilesFromSQLiteCache;
                        int network = MainMap.Manager.TilesFromNetwork;

                        // Only update UI if values changed
                        if (memCache != _lastMemCache || sqliteCache != _lastSqliteCache || network != _lastNetwork)
                        {
                            _lastMemCache = memCache;
                            _lastSqliteCache = sqliteCache;
                            _lastNetwork = network;

                            LabelCacheStats.Content = $"Cache: RAM: {memCache} SQLite: {sqliteCache} Net: {network}";

                            // Color code based on cache hits
                            if (network == 0 && (memCache > 0 || sqliteCache > 0))
                            {
                                LabelCacheStats.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Green);
                            }
                            else if (sqliteCache > 0 && network > 0)
                            {
                                LabelCacheStats.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Orange);
                            }
                            else if (network > 0)
                            {
                                LabelCacheStats.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Red);
                            }
                            else
                            {
                                LabelCacheStats.Foreground = new System.Windows.Media.SolidColorBrush(System.Windows.Media.Colors.Gray);
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                // current location changed
                void MainMap_OnCurrentPositionChanged(PointLatLng point)
        {
            try
            {
                LabelLatLng.Content = "Lat: " + point.Lat.ToString("F8", CultureInfo.InvariantCulture) + ", Lng: " + point.Lng.ToString("F8", CultureInfo.InvariantCulture);

                // Конвертація в UTM та MGRS
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
            finally
            {
            }
        }

        // zoom changed
        void MainMap_OnMapZoomChanged()
        {
            try
            {
                Dispatcher.BeginInvoke(DispatcherPriority.Normal, new Action(() =>
                {
                    LabelZoom.Content = "Zoom: " + MainMap.Zoom.ToString(CultureInfo.InvariantCulture);
                }));
            }
            catch
            {
            }
        }

        // immediate wheel zoom update
        void MainMap_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            try
            {
                // schedule update at Render priority to ensure map applied the zoom change
                Dispatcher.BeginInvoke(DispatcherPriority.Render, new Action(() =>
                {
                    LabelZoom.Content = "Zoom: " + MainMap.Zoom.ToString(CultureInfo.InvariantCulture);
                }));
            }
            catch
            {
            }
        }

        // reload
        private void button1_Click(object sender, RoutedEventArgs e)
        {
            MainMap.ReloadMap();
        }

        // enable current marker
        private void checkBoxCurrentMarker_Checked(object sender, RoutedEventArgs e)
        {
            if (currentMarker != null)
            {
                MainMap.Markers.Add(currentMarker);
            }
        }

        // disable current marker
        private void checkBoxCurrentMarker_Unchecked(object sender, RoutedEventArgs e)
        {
            if (currentMarker != null)
            {
                MainMap.Markers.Remove(currentMarker);
            }
        }

        // enable map dragging
        private void checkBoxDragMap_Checked(object sender, RoutedEventArgs e)
        {
            MainMap.CanDragMap = true;
        }

        // disable map dragging
        private void checkBoxDragMap_Unchecked(object sender, RoutedEventArgs e)
        {
            MainMap.CanDragMap = false;
        }

        // goto!
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

        // goto by geocoder
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

        private void SearchByKeywords()
        {
            var status = MainMap.SetPositionByKeywords(TextBoxGeo.Text);
            if (status != GeoCoderStatusCode.OK)
            {
                MessageBox.Show("Geocoder can't find: '" + TextBoxGeo.Text + "', reason: " + status.ToString(),
                    "GMap.NET",
                    MessageBoxButton.OK,
                    MessageBoxImage.Exclamation);
            }
            else
            {
                currentMarker.Position = MainMap.Position;
            }
        }

        // zoom changed handler removed (zoom control removed)

        // zoom up
        private void czuZoomUp_Click(object sender, RoutedEventArgs e)
        {
            MainMap.Zoom = (int)MainMap.Zoom + 1;
        }

        // zoom down
        private void czuZoomDown_Click(object sender, RoutedEventArgs e)
        {
            MainMap.Zoom = (int)(MainMap.Zoom + 0.99) - 1;
        }

        // prefetch
        private void button3_Click(object sender, RoutedEventArgs e)
        {
            var area = MainMap.SelectedArea;
            if (!area.IsEmpty)
            {
                for (int i = (int)MainMap.Zoom; i <= MainMap.MaxZoom; i++)
                {
                    var res = MessageBox.Show("Ready ripp at Zoom = " + i + " ?",
                        "GMap.NET",
                        MessageBoxButton.YesNoCancel);

                    if (res == MessageBoxResult.Yes)
                    {
                        var obj = new TilePrefetcher();
                        obj.Owner = this;
                        obj.ShowCompleteMessage = true;
                        obj.Start(area, i, MainMap.MapProvider, 100);
                    }
                    else if (res == MessageBoxResult.No)
                    {
                        continue;
                    }
                    else if (res == MessageBoxResult.Cancel)
                    {
                        break;
                    }
                }
            }
            else
            {
                MessageBox.Show("Select map area holding ALT",
                    "GMap.NET",
                    MessageBoxButton.OK,
                    MessageBoxImage.Exclamation);
            }
        }

        // access mode
        private void comboBoxMode_DropDownClosed(object sender, EventArgs e)
        {
            MainMap.Manager.Mode = (AccessMode)ComboBoxMode.SelectedItem;
            MainMap.ReloadMap();
        }

        // clear cache
        private void button4_Click(object sender, RoutedEventArgs e)
        {
            if (MessageBox.Show("Are You sure?",
                    "Clear GMap.NET cache?",
                    MessageBoxButton.OKCancel,
                    MessageBoxImage.Warning) == MessageBoxResult.OK)
            {
                try
                {
                    MainMap.Manager.PrimaryCache.DeleteOlderThan(DateTime.Now, null);
                    MessageBox.Show("Done. Cache is clear.");
                }
                catch (Exception ex)
                {
                    MessageBox.Show(ex.Message);
                }
            }
        }

        // export
        private void button6_Click(object sender, RoutedEventArgs e)
        {
            MainMap.ShowExportDialog();
        }

        // import
        private void button5_Click(object sender, RoutedEventArgs e)
        {
            MainMap.ShowImportDialog();
        }

        // use route cache
        private void checkBoxCacheRoute_Checked(object sender, RoutedEventArgs e)
        {
            MainMap.Manager.UseRouteCache = CheckBoxCacheRoute.IsChecked.Value;
        }

        // use geocoding cahce
        private void checkBoxGeoCache_Checked(object sender, RoutedEventArgs e)
        {
            MainMap.Manager.UseGeocoderCache = CheckBoxGeoCache.IsChecked.Value;
            MainMap.Manager.UsePlacemarkCache = MainMap.Manager.UseGeocoderCache;
        }

        // cache diagnostics
        private void buttonCacheDiag_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var cache = MainMap.Manager.PrimaryCache as GMap.NET.CacheProviders.SQLitePureImageCache;
                if (cache == null)
                {
                    MessageBox.Show("PrimaryCache is not SQLitePureImageCache!", "Cache Diagnostics", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var dbPath = System.IO.Path.Combine(cache.GtileCache, GMap.NET.MapProviders.GMapProvider.LanguageStr, "Data.gmdb");
                var fileInfo = new System.IO.FileInfo(dbPath);

                int tileCount = cache.GetTileCount();

                var statsObj = new CacheStats
                {
                    DbPath = dbPath,
                    FileSizeMb = fileInfo.Length / (1024.0 * 1024.0),
                    LastModified = fileInfo.LastWriteTime,
                    TileCount = tileCount,
                    FromRam = MainMap.Manager.TilesFromMemoryCache,
                    FromSQLite = MainMap.Manager.TilesFromSQLiteCache,
                    FromNetwork = MainMap.Manager.TilesFromNetwork,
                    Mode = MainMap.Manager.Mode.ToString(),
                    UseMemoryCache = MainMap.Manager.UseMemoryCache,
                    CacheOnIdleRead = MainMap.Manager.CacheOnIdleRead,
                    BoostCacheEngine = MainMap.Manager.BoostCacheEngine
                };

                var wnd = new CacheStatsWindow();
                wnd.Owner = this;
                wnd.Stats = statsObj;
                wnd.ShowDialog();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Error: " + ex.Message, "Cache Diagnostics", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // save currnt view
        private void button7_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var img = MainMap.ToImageSource();
                var en = new PngBitmapEncoder();
                en.Frames.Add(BitmapFrame.Create(img as BitmapSource));

                var dlg = new Microsoft.Win32.SaveFileDialog();
                dlg.FileName = "GMap.NET Image"; // Default file name
                dlg.DefaultExt = ".png"; // Default file extension
                dlg.Filter = "Image (.png)|*.png"; // Filter files by extension
                dlg.AddExtension = true;
                dlg.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);

                // Show save file dialog box
                bool? result = dlg.ShowDialog();

                // Process save file dialog box results
                if (result == true)
                {
                    // Save document
                    string filename = dlg.FileName;

                    using (Stream st = File.OpenWrite(filename))
                    {
                        en.Save(st);
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message);
            }
        }

        // clear all markers
        private void button10_Click(object sender, RoutedEventArgs e)
        {
            var clear = MainMap.Markers.Where(p => p != null && p != currentMarker);
            if (clear != null)
            {
                for (int i = 0; i < clear.Count(); i++)
                {
                    MainMap.Markers.Remove(clear.ElementAt(i));
                    i--;
                }
            }


        }

        // add marker
        private void button8_Click(object sender, RoutedEventArgs e)
        {
            var m = new GMapMarker(currentMarker.Position);
            {
                Placemark? p = null;
                if (CheckBoxPlace.IsChecked.Value)
                {
                    GeoCoderStatusCode status;
                    var plret = GMapProviders.GoogleMap.GetPlacemark(currentMarker.Position, out status);
                    if (status == GeoCoderStatusCode.OK && plret != null)
                    {
                        p = plret;
                    }
                }

                string toolTipText;
                if (p != null)
                {
                    toolTipText = p.Value.Address;
                }
                else
                {
                    toolTipText = currentMarker.Position.ToString();
                }

                m.Shape = new CustomMarkerDemo(this, m, toolTipText);
                m.ZIndex = 55;
            }
            MainMap.Markers.Add(m);
        }

        // Attack zone fields
        private System.Drawing.PointF _attackPoint = System.Drawing.PointF.Empty;
        private float _attackAngle = 0f;
        private float _attackRayLength = 500;
        private float _attackSectorRadius = 500;
        private float _attackSectorWidth = 30f;

        private float rotateAngleStep = 0.5f;
        private float rotateAngelShiftStep = 0.2f;

        // Attack point click tracking
        private DateTime _lastClickTime = DateTime.MinValue;
        private int _clickCount = 0;
        private const int DoubleClickMaxMs = 500;

        // Throttling for InvalidateVisual (60 FPS limit)
        private DispatcherTimer _invalidateTimer;
        private bool _needsInvalidate = false;
        private DateTime _lastInvalidateTime = DateTime.MinValue;
        private const int InvalidateThrottleMs = 16; // 60 FPS (~16ms per frame)

        // WASD pan timer and state
        private DispatcherTimer _wasdTimer;
        private readonly HashSet<Key> _wasdHeld = new HashSet<Key>();
        private int _wasdTickIntervalMs = 16; // ~60 FPS
        // Increase base pan speed for snappier movement. Hold Shift to multiply.
        private int _wasdPanPxPerTick = 20; // base pixels per tick (was 8)
        // fractional accumulator to avoid integer rounding/jumps when rotated
        private double _wasdAccumX = 0.0;
        private double _wasdAccumY = 0.0;

        // Attack point file path
        private readonly string _attackPointPath = System.IO.Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory, "settings", "attack_point.json");

        // Window key handlers
        private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            // Map rotation: Q (clockwise), E (counter-clockwise)
            if (e.Key == Key.Q)
            {
                MainMap.Bearing++;
                e.Handled = true;
            }
            else if (e.Key == Key.E)
            {
                MainMap.Bearing--;
                e.Handled = true;
            }
            // Attack ray rotation: Left (counter-clockwise), Right (clockwise)
            // Ultra-smooth: 0.05° default, 0.01° with Shift
            // Only if MainMap is focused and no Ctrl/Alt modifiers
            else if (e.Key == Key.Left && MainMap.IsFocused && 
                     !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && 
                     !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                float step = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ? rotateAngelShiftStep : rotateAngleStep;
                _attackAngle -= step;
                if (_attackAngle < 0) _attackAngle += 360f;
                TextBoxAttackAngle.Text = _attackAngle.ToString("F2");
                UpdateMapAttackZone();
                InvalidateThrottled(); // Throttled to 60 FPS
                e.Handled = true;
            }
            else if (e.Key == Key.Right && MainMap.IsFocused && 
                     !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && 
                     !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                float step = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ? rotateAngelShiftStep : rotateAngleStep;
                _attackAngle += step;
                if (_attackAngle >= 360) _attackAngle -= 360f;
                TextBoxAttackAngle.Text = _attackAngle.ToString("F2");
                UpdateMapAttackZone();
                InvalidateThrottled(); // Throttled to 60 FPS
                e.Handled = true;
            }
            // Attack sector width: Up (increase), Down (decrease)
            else if (e.Key == Key.Up && MainMap.IsFocused && 
                     !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && 
                     !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                float step = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ? rotateAngelShiftStep : rotateAngleStep;
                _attackSectorWidth += step;
                if (_attackSectorWidth > 180f) _attackSectorWidth = 180f;
                TextBoxSectorWidth.Text = _attackSectorWidth.ToString("F2");
                UpdateMapAttackZone();
                InvalidateThrottled(); // Throttled to 60 FPS
                e.Handled = true;
            }
            else if (e.Key == Key.Down && MainMap.IsFocused && 
                     !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) && 
                     !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                float step = Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ? rotateAngelShiftStep : rotateAngleStep;
                _attackSectorWidth -= step;
                if (_attackSectorWidth < 5f) _attackSectorWidth = 5f;
                TextBoxSectorWidth.Text = _attackSectorWidth.ToString("F2");
                UpdateMapAttackZone();
                InvalidateThrottled(); // Throttled to 60 FPS
                e.Handled = true;
            }
            // WASD handling: start tracking held key and start continuous pan timer
            else if ((e.Key == Key.W || e.Key == Key.A || e.Key == Key.S || e.Key == Key.D) && MainMap.IsFocused &&
                     !Keyboard.Modifiers.HasFlag(ModifierKeys.Control) &&
                     !Keyboard.Modifiers.HasFlag(ModifierKeys.Alt))
            {
                if (!_wasdHeld.Contains(e.Key))
                {
                    _wasdHeld.Add(e.Key);
                }

                if (!_wasdTimer.IsEnabled)
                {
                    // reset accumulators when starting new continuous pan
                    _wasdAccumX = 0.0;
                    _wasdAccumY = 0.0;
                    _wasdTimer.Start();
                }

                e.Handled = true;
            }
        }

        // Attack point setting with Ctrl+Shift+DoubleClick
        private void MainMap_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Only process if Ctrl+Shift are pressed
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Control) || !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
                return;

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

                // Get mouse position relative to MainMap
                var pos = e.GetPosition(MainMap);

                // Convert screen coordinates to LatLng
                var latlng = MainMap.FromLocalToLatLng((int)pos.X, (int)pos.Y);

                // Store as screen pixel coordinates
                _attackPoint = new System.Drawing.PointF((float)pos.X, (float)pos.Y);

                SaveAttackPoint(_attackPoint);
                UpdateMapAttackZone();

                MainMap.InvalidateVisual();
            }
        }

        // TextBox handlers (throttled invalidation)
        private void TextBoxAttackAngle_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (float.TryParse(TextBoxAttackAngle.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float angle))
            {
                _attackAngle = angle % 360;
                UpdateMapAttackZone();
                InvalidateThrottled();
            }
        }

        private void TextBoxRayLength_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (float.TryParse(TextBoxRayLength.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float length))
            {
                _attackRayLength = Math.Max(0, length);
                UpdateMapAttackZone();
                InvalidateThrottled();
            }
        }

        private void TextBoxSectorRadius_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (float.TryParse(TextBoxSectorRadius.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float radius))
            {
                _attackSectorRadius = Math.Max(0, radius);
                UpdateMapAttackZone();
                InvalidateThrottled();
            }
        }

        private void TextBoxSectorWidth_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (float.TryParse(TextBoxSectorWidth.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float width))
            {
                _attackSectorWidth = Math.Clamp(width, 5f, 180f);
                UpdateMapAttackZone();
                InvalidateThrottled();
            }
        }

        private void TextBoxRotateStep_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (float.TryParse(TextBoxRotateStep.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
            {
                // keep reasonable bounds
                rotateAngleStep = Math.Max(0f, val);
                Debug.WriteLine($"rotateAngleStep set to {rotateAngleStep}");
            }
        }

        private void TextBoxRotateShiftStep_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (float.TryParse(TextBoxRotateShiftStep.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float val))
            {
                rotateAngelShiftStep = Math.Max(0f, val);
                Debug.WriteLine($"rotateAngelShiftStep set to {rotateAngelShiftStep}");
            }
        }

        // Update map attack zone parameters
        private void UpdateMapAttackZone()
        {
            MainMap.AttackPoint = _attackPoint;
            MainMap.AttackAngle = _attackAngle;
            MainMap.AttackRayLength = _attackRayLength;
            MainMap.AttackSectorRadius = _attackSectorRadius;
            MainMap.AttackSectorWidth = _attackSectorWidth;
        }

        // Throttled invalidate - limits redraws to max 60 FPS (16ms intervals)
        // NOTE: GMap.NET has an internal InvalidatorWatch (Core.cs) that refreshes every ~111ms (9 FPS).
        // That is too slow for smooth rotation/interaction.
        // This method forces a WPF render pass at 60 FPS to ensure smooth animation
        // bypassing the slow internal background timer.
        private void InvalidateThrottled()
        {
            var now = DateTime.Now;
            var timeSinceLastInvalidate = (now - _lastInvalidateTime).TotalMilliseconds;

            if (timeSinceLastInvalidate >= InvalidateThrottleMs)
            {
                // Enough time passed - invalidate immediately
                // Use InvalidateVisual(true) to force redraw and bypass GMap.NET's internal 111ms throttle
                MainMap.InvalidateVisual(true);
                _lastInvalidateTime = now;
                _needsInvalidate = false;
            }
            else
            {
                // Too soon - schedule for later
                _needsInvalidate = true;

                if (_invalidateTimer == null)
                {
                    _invalidateTimer = new DispatcherTimer();
                    _invalidateTimer.Interval = TimeSpan.FromMilliseconds(InvalidateThrottleMs);
                    _invalidateTimer.Tick += (s, e) =>
                    {
                        if (_needsInvalidate)
                        {
                            // Use InvalidateVisual(true) to force redraw and bypass GMap.NET's internal 111ms throttle
                            MainMap.InvalidateVisual(true);
                            _lastInvalidateTime = DateTime.Now;
                            _needsInvalidate = false;
                        }
                        _invalidateTimer.Stop();
                    };
                }

                if (!_invalidateTimer.IsEnabled)
                {
                    _invalidateTimer.Start();
                }
            }
        }

        // Save attack point to JSON
        private void SaveAttackPoint(System.Drawing.PointF point)
        {
            try
            {
                var dir = Path.GetDirectoryName(_attackPointPath);
                if (!Directory.Exists(dir))
                    Directory.CreateDirectory(dir);

                var data = new { X = point.X, Y = point.Y };
                File.WriteAllText(_attackPointPath, Newtonsoft.Json.JsonConvert.SerializeObject(data));
                Debug.WriteLine($"Attack point saved: {point}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save attack point: {ex.Message}");
            }
        }

        // Load attack point from JSON
        private void LoadAttackPoint()
        {
            try
            {
                if (File.Exists(_attackPointPath))
                {
                    var json = File.ReadAllText(_attackPointPath);
                    var data = Newtonsoft.Json.JsonConvert.DeserializeAnonymousType(json, new { X = 0f, Y = 0f });
                    _attackPoint = new System.Drawing.PointF(data.X, data.Y);
                    UpdateMapAttackZone();
                    Debug.WriteLine($"Attack point loaded: {_attackPoint}");
                }
            }
            catch (Exception ex)
            {
                        Debug.WriteLine($"Failed to load attack point: {ex.Message}");
                    }
                }

                // sets route start removed

                // sets route end removed

        // adds route
        // Add route functionality removed

        // enables tile grid view
        private void checkBox1_Checked(object sender, RoutedEventArgs e)
        {
            MainMap.ShowTileGridLines = true;
        }

        // disables tile grid view
        private void checkBox1_Unchecked(object sender, RoutedEventArgs e)
        {
            MainMap.ShowTileGridLines = false;
        }

        private void Window_KeyUp(object sender, KeyEventArgs e)
        {
            // Arrow keys are handled in Window_PreviewKeyDown for attack zone control
            // Only handle zoom shortcuts here
            if (MainMap.IsFocused)
            {
                if (e.Key == Key.Add)
                {
                    czuZoomUp_Click(null, null);
                }
                else if (e.Key == Key.Subtract)
                {
                    czuZoomDown_Click(null, null);
                }
            }

            // stop WASD movement on KeyUp
            if (e.Key == Key.W || e.Key == Key.A || e.Key == Key.S || e.Key == Key.D)
            {
                if (_wasdHeld.Contains(e.Key))
                    _wasdHeld.Remove(e.Key);

                if (_wasdHeld.Count == 0 && _wasdTimer != null && _wasdTimer.IsEnabled)
                {
                    _wasdTimer.Stop();
                }
            }
        }

        private void WasdTimer_Tick(object? sender, EventArgs e)
        {
            if (!_wasdHeld.Any())
            {
                _wasdTimer.Stop();
                return;
            }

            // Base pan per tick, scale with Shift for faster movement
            int perTick = _wasdPanPxPerTick * (Keyboard.IsKeyDown(Key.LeftShift) || Keyboard.IsKeyDown(Key.RightShift) ? 3 : 1);

            // User requested to invert directions for WASD
            // Build direction vector and normalize to keep diagonal speed same
            double vx = 0.0, vy = 0.0;
            foreach (var k in _wasdHeld)
            {
                switch (k)
                {
                    // Standard screen directions (up is negative Y)
                    case Key.W:
                        vy -= 1.0; // up
                        break;
                    case Key.S:
                        vy += 1.0; // down
                        break;
                    case Key.A:
                        vx -= 1.0; // left
                        break;
                    case Key.D:
                        vx += 1.0; // right
                        break;
                }
            }

            // Build screen-space direction (inverted for "camera" movement feel)
            // W = move map down (camera up), S = move map up (camera down)
            // A = move map right (camera left), D = move map left (camera right)
            double screenDx = 0.0, screenDy = 0.0;
            if (vx < 0) screenDx += perTick; // A pressed -> move map right
            if (vx > 0) screenDx -= perTick; // D pressed -> move map left
            if (vy < 0) screenDy += perTick; // W pressed -> move map down
            if (vy > 0) screenDy -= perTick; // S pressed -> move map up

            if (screenDx != 0 || screenDy != 0)
            {
                try
                {
                    // Accumulate fractional part for smooth sub-pixel movement
                    _wasdAccumX += screenDx;
                    _wasdAccumY += screenDy;

                    int iDx = (int)Math.Round(_wasdAccumX);
                    int iDy = (int)Math.Round(_wasdAccumY);

                    if (iDx != 0 || iDy != 0)
                    {
                        MainMap.Offset(iDx, iDy);
                        _wasdAccumX -= iDx;
                        _wasdAccumY -= iDy;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine("WasdTimer pan failed: " + ex.Message);
                }
            }
        }

        // Real-time controls removed; no action required on change.

        private void Label_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (sender is Label lbl)
            {
                string text = lbl.Content?.ToString() ?? string.Empty;
                string toCopy = text;

                if (lbl.Name == "LabelLatLng")
                {
                    // extract two numeric values (lat, lng)
                    var matches = Regex.Matches(text, "-?\\d+[\\.,]?\\d*");
                    if (matches.Count >= 2)
                    {
                        // normalize decimal separator to dot
                        var a = matches[0].Value.Replace(',', '.');
                        var b = matches[1].Value.Replace(',', '.');
                        toCopy = a + ", " + b;
                    }
                    else
                    {
                        int idx = text.IndexOf(':');
                        if (idx >= 0) toCopy = text.Substring(idx + 1).Trim();
                    }
                }
                else if (lbl.Name == "LabelZoom")
                {
                    // Do not copy zoom on click
                    return;
                }
                else
                {
                    int idx = text.IndexOf(':');
                    if (idx >= 0) toCopy = text.Substring(idx + 1).Trim();
                }

                try
                {
                    Clipboard.SetText(toCopy);
                    Debug.WriteLine($"Label clicked, copied: {toCopy}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to copy to clipboard: {ex.Message}");
                }
            }
        }
    }

    public class MapValidationRule : ValidationRule
    {
        bool _userAcceptedLicenseOnce;
        internal MainWindow Window;

        public override ValidationResult Validate(object value, CultureInfo cultureInfo)
        {
            return new ValidationResult(true, null);
        }
    }
}