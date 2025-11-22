using System;
using System.Drawing;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;

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
            lblPosition.Text = $"Позиція: {point.Lat:F4}, {point.Lng:F4}";
        }

        private void MapControl_OnMapZoomChanged()
        {
            lblPosition.Text = $"Позиція: {mapControl.Position.Lat:F4}, {mapControl.Position.Lng:F4} | Zoom: {mapControl.Zoom}";
        }

        private void MapControl_MouseMove(object sender, MouseEventArgs e)
        {
            PointLatLng point = mapControl.FromLocalToLatLng(e.X, e.Y);
            this.Text = $"GMap.NET - Координати: {point.Lat:F6}, {point.Lng:F6}";
        }
    }
}
