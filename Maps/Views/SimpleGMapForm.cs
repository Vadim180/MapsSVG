using System;
using System.Drawing;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;
using System.Reflection;

namespace Maps.Views
{
    /// <summary>
    /// Спрощена форма з базовою функціональністю GMap.NET
    /// </summary>
    public partial class SimpleGMapForm : Form
    {
        private GMapControl mapControl;
        private Panel controlPanel;
        private ComboBox comboBoxMapType;
        private Button btnZoomIn;
        private Button btnZoomOut;
        private Label lblPosition;

        // Layers
        GMapOverlay overlay;
        PointLatLng start;


        public SimpleGMapForm()
        {
            InitializeComponent();
            InitializeMap();
        }

        private void InitializeComponent()
        {
            this.Text = "GMap.NET - Спрощена карта";
            this.Size = new Size(1000, 700);
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MinimumSize = new Size(800, 600);

            // Створюємо панель управління
            controlPanel = new Panel
            {
                Dock = DockStyle.Top,
                Height = 60,
                BackColor = Color.LightGray
            };

            // Комбобокс для вибору типу карти
            comboBoxMapType = new ComboBox
            {
                Location = new Point(10, 15),
                Width = 200,
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            comboBoxMapType.SelectedIndexChanged += ComboBoxMapType_SelectedIndexChanged;

            // Кнопка збільшення
            btnZoomIn = new Button
            {
                Text = "+",
                Location = new Point(220, 15),
                Size = new Size(40, 30)
            };
            btnZoomIn.Click += BtnZoomIn_Click;

            // Кнопка зменшення
            btnZoomOut = new Button
            {
                Text = "-",
                Location = new Point(270, 15),
                Size = new Size(40, 30)
            };
            btnZoomOut.Click += BtnZoomOut_Click;

            // Лейбл з координатами
            lblPosition = new Label
            {
                Location = new Point(320, 20),
                AutoSize = true,
                Text = "Позиція: -"
            };

            controlPanel.Controls.AddRange(new Control[] 
            { 
                comboBoxMapType, 
                btnZoomIn, 
                btnZoomOut, 
                lblPosition 
            });

            // Створюємо контрол карти
            mapControl = new GMapControl
            {
                Dock = DockStyle.Fill,
                MinZoom = 2,
                MaxZoom = 20,
                Zoom = 10
            };

            // Додаємо обробники подій
            mapControl.OnPositionChanged += MapControl_OnPositionChanged;
            mapControl.OnMapZoomChanged += MapControl_OnMapZoomChanged;
            mapControl.MouseMove += MapControl_MouseMove;


            this.Controls.Add(mapControl);
            this.Controls.Add(controlPanel);
        }

        private void InitializeMap()
        {
            try
            {

                // Ініціалізуємо custom overlay
                mapControl.Overlays.Add(overlay = new GMapOverlay("markers"));

                // Налаштування карти
                mapControl.MapProvider = GMapProviders.OpenStreetMap;
                mapControl.Position = new PointLatLng(50.4501, 30.5234); // Київ за замовчуванням
                mapControl.DragButton = MouseButtons.Left;
                mapControl.CanDragMap = true;
                mapControl.ShowCenter = false;

                // Заповнюємо комбобокс провайдерами карт
                var providers = new GMapProvider[]
                {
                    GMapProviders.OpenStreetMap,
                    GMapProviders.GoogleMap,
                    GMapProviders.GoogleSatelliteMap,
                    GMapProviders.GoogleHybridMap,
                    GMapProviders.BingMap,
                    GMapProviders.BingSatelliteMap,
                    GMapProviders.BingHybridMap
                };

                foreach (var provider in providers)
                {
                    comboBoxMapType.Items.Add(provider.Name);
                }

                comboBoxMapType.SelectedIndex = 0;
                // Ensure initial label shows position and zoom (zoom rounded to 2 decimals)
                MapControl_OnMapZoomChanged();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Помилка ініціалізації карти: {ex.Message}", 
                    "Помилка", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ComboBoxMapType_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (comboBoxMapType.SelectedItem == null) return;

            string selectedProvider = comboBoxMapType.SelectedItem.ToString();
            
            switch (selectedProvider)
            {
                case "OpenStreetMap":
                    mapControl.MapProvider = GMapProviders.OpenStreetMap;
                    break;
                case "GoogleMap":
                    mapControl.MapProvider = GMapProviders.GoogleMap;
                    break;
                case "GoogleSatelliteMap":
                    mapControl.MapProvider = GMapProviders.GoogleSatelliteMap;
                    break;
                case "GoogleHybridMap":
                    mapControl.MapProvider = GMapProviders.GoogleHybridMap;
                    break;
                case "BingMap":
                    mapControl.MapProvider = GMapProviders.BingMap;
                    break;
                case "BingSatelliteMap":
                    mapControl.MapProvider = GMapProviders.BingSatelliteMap;
                    break;
                case "BingHybridMap":
                    mapControl.MapProvider = GMapProviders.BingHybridMap;
                    break;
            }

            // Reload map only when control and form are created/shown.
            // Calling ReloadMap before the form is loaded causes GMap to warn and do nothing.
            if (this.IsHandleCreated && mapControl.IsHandleCreated)
            {
                mapControl.ReloadMap();
            }
        }

        private void BtnZoomIn_Click(object sender, EventArgs e)
        {
            if (mapControl.Zoom < mapControl.MaxZoom)
            {
                mapControl.Zoom++;
            }
        }

        private void BtnZoomOut_Click(object sender, EventArgs e)
        {
            if (mapControl.Zoom > mapControl.MinZoom)
            {
                mapControl.Zoom--;
            }
        }

        private void MapControl_OnPositionChanged(PointLatLng point)
        {
            // Always include zoom as well, rounded to 2 decimal places for readability
            lblPosition.Text = $"Позиція: {point.Lat:F4}, {point.Lng:F4} | Zoom: {mapControl.Zoom:F2}";
        }

        private void MapControl_OnMapZoomChanged()
        {
            double lat = mapControl.Position.Lat;
            double lng = mapControl.Position.Lng;

            string dmsLat = ToDMS(lat, true);
            string dmsLng = ToDMS(lng, false);

            // UTM and MGRS
            var (zone, hemisphere, easting, northing) = LatLonToUTM(lat, lng);
            string utmText = $"Zone {zone}{hemisphere}  E: {easting:F1}  N: {northing:F1}";

            string fullMgrs = "N/A";
            try
            {
                var utmType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
                    .FirstOrDefault(t => string.Equals(t.Name, "UniversalTransverseMercator", StringComparison.Ordinal));

                if (utmType != null)
                {
                    // Create UTM instance (hemisphere as string, zone, easting, northing)
                    var utmInstance = Activator.CreateInstance(utmType, hemisphere.ToString(), zone, easting, northing);
                    var convertMethod = utmType.GetMethod("ConvertUTMtoLatLong", BindingFlags.Public | BindingFlags.Static);
                    if (convertMethod != null && utmInstance != null)
                    {
                        var corrected = convertMethod.Invoke(null, new object[] { utmInstance });
                        if (corrected != null)
                        {
                            var mgrsProp = corrected.GetType().GetProperty("MGRS");
                            if (mgrsProp != null)
                            {
                                var mgrsVal = mgrsProp.GetValue(corrected);
                                if (mgrsVal != null)
                                    fullMgrs = mgrsVal.ToString() ?? "N/A";
                            }
                        }
                    }
                }
            }
            catch
            {
                fullMgrs = "N/A";
            }

            lblPosition.Text = $"Позиція: {lat:F4}, {lng:F4} | Zoom: {mapControl.Zoom:F2}" +
                               $"\nDMS: {dmsLat}  {dmsLng}" +
                               $"\nUTM: {utmText}" +
                               $"\nMGRS: {fullMgrs}";
        }

        private static string ToDMS(double value, bool isLatitude)
        {
            double absVal = Math.Abs(value);
            int deg = (int)Math.Floor(absVal);
            double rem = (absVal - deg) * 60.0;
            int min = (int)Math.Floor(rem);
            double sec = (rem - min) * 60.0;
            string dir;
            if (isLatitude)
                dir = value >= 0 ? "N" : "S";
            else
                dir = value >= 0 ? "E" : "W";
            return $"{deg}°{min:00}'{sec:00.##}\"{dir}";
        }

        // Convert lat/lon to UTM (WGS84). Returns zone, hemisphere char ('N'/'S'), easting, northing
        private static (int zone, char hemisphere, double easting, double northing) LatLonToUTM(double lat, double lon)
        {
            const double a = 6378137.0; // WGS84 major axis
            const double f = 1.0 / 298.257223563; // WGS84 flattening
            const double k0 = 0.9996;

            double latRad = lat * Math.PI / 180.0;
            double lonRad = lon * Math.PI / 180.0;

            int zone = (int)Math.Floor((lon + 180.0) / 6.0) + 1;
            double lonOrigin = (zone - 1) * 6 - 180 + 3; // +3 puts origin in middle of zone
            double lonOriginRad = lonOrigin * Math.PI / 180.0;

            double e2 = f * (2 - f);
            double ePrime2 = e2 / (1 - e2);
            double N = a / Math.Sqrt(1 - e2 * Math.Sin(latRad) * Math.Sin(latRad));
            double T = Math.Tan(latRad) * Math.Tan(latRad);
            double C = ePrime2 * Math.Cos(latRad) * Math.Cos(latRad);
            double A = Math.Cos(latRad) * (lonRad - lonOriginRad);

            double M = a * ((1 - e2 / 4 - 3 * e2 * e2 / 64 - 5 * e2 * e2 * e2 / 256) * latRad
                            - (3 * e2 / 8 + 3 * e2 * e2 / 32 + 45 * e2 * e2 * e2 / 1024) * Math.Sin(2 * latRad)
                            + (15 * e2 * e2 / 256 + 45 * e2 * e2 * e2 / 1024) * Math.Sin(4 * latRad)
                            - (35 * e2 * e2 * e2 / 3072) * Math.Sin(6 * latRad));

            double easting = k0 * N * (A + (1 - T + C) * Math.Pow(A, 3) / 6 + (5 - 18 * T + T * T + 72 * C - 58 * ePrime2) * Math.Pow(A, 5) / 120) + 500000.0;

            double northing = k0 * (M + N * Math.Tan(latRad) * (A * A / 2 + (5 - T + 9 * C + 4 * C * C) * Math.Pow(A, 4) / 24
                                + (61 - 58 * T + T * T + 600 * C - 330 * ePrime2) * Math.Pow(A, 6) / 720));

            char hemisphere = lat >= 0 ? 'N' : 'S';
            if (lat < 0) northing += 10000000.0; // add offset for southern hemisphere

            return (zone, hemisphere, easting, northing);
        }

        private void MapControl_MouseMove(object sender, MouseEventArgs e)
        {
            PointLatLng point = mapControl.FromLocalToLatLng(e.X, e.Y);
            this.Text = $"GMap.NET - Координати: {point.Lat:F6}, {point.Lng:F6}";
        }
    }
}
