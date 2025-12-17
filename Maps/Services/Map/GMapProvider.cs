using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using GMap.NET;
using GMap.NET.MapProviders;
using GMap.NET.WindowsForms;

namespace Maps.Services.Map
{
    /// <summary>
    /// Простий провайдер для GMap.NET, що реалізує IMapProvider.
    /// Відповідальний за підключення контролу, трансформації координат
    /// і рендеринг оверлеїв через Paint.
    /// </summary>
    public class GMapProvider : IMapProvider
    {
        private GMapControl? _mapControl;
        private readonly List<IMapOverlay> _overlays = new List<IMapOverlay>();

        public event Action<PointF>? OnClick;
        public event Action<PointF>? OnPositionChanged;

        /// <summary>
        /// Доступ до внутрішнього GMapControl для зворотної сумісності з існуючим кодом.
        /// </summary>
        public GMapControl? Control => _mapControl;

        public void Initialize(Control container)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));

            if (_mapControl != null)
            {
                // Відпідписуємося від подій для безпечної ре-ініціалізації
                _mapControl.MouseClick -= MapControl_MouseClick;
                _mapControl.OnPositionChanged -= MapControl_OnPositionChanged;
                _mapControl.Paint -= MapControl_Paint;
                container.Controls.Remove(_mapControl);
                _mapControl.Dispose();
                _mapControl = null;
            }

            _mapControl = new GMapControl
            {
                Dock = DockStyle.Fill,
                MinZoom = 2,
                MaxZoom = 20,
                Zoom = 10,
                CanDragMap = true,
                DragButton = MouseButtons.Left,
                ShowCenter = false,
                MouseWheelZoomEnabled = true,
                MouseWheelZoomType = GMap.NET.MouseWheelZoomType.MousePositionAndCenter,
                IgnoreMarkerOnMouseWheel = true
            };

            // Базова конфігурація
            _mapControl.MapProvider = GMapProviders.OpenStreetMap;
            GMaps.Instance.Mode = AccessMode.ServerAndCache;

            // Початкова позиція (за замовчуванням Київ)
            _mapControl.Position = new PointLatLng(50.4501, 30.5234);

            // Події
            _mapControl.MouseClick += MapControl_MouseClick;
            _mapControl.OnPositionChanged += MapControl_OnPositionChanged;
            _mapControl.Paint += MapControl_Paint;

            container.Controls.Add(_mapControl);
            container.Controls.SetChildIndex(_mapControl, 0);
        }

        public void SetBounds(double north, double south, double east, double west)
        {
            if (_mapControl == null) return;

            // FromLTRB(leftLng, topLat, rightLng, bottomLat)
            _mapControl.BoundsOfMap = RectLatLng.FromLTRB(west, north, east, south);

            // Центр області
            double centerLat = (north + south) / 2.0;
            double centerLng = (west + east) / 2.0;

            _mapControl.Position = new PointLatLng(centerLat, centerLng);

            // Коли контрол має потрібний розмір, можна перерахувати zoom-обмеження зовні
            _mapControl.Invalidate();
        }

        public PointF ScreenToGeo(Point screen)
        {
            if (_mapControl == null) throw new InvalidOperationException("Map not initialized");
            var latLng = _mapControl.FromLocalToLatLng(screen.X, screen.Y);
            return new PointF((float)latLng.Lat, (float)latLng.Lng);
        }

        public Point GeoToScreen(PointF geo)
        {
            if (_mapControl == null) throw new InvalidOperationException("Map not initialized");
            var gpt = _mapControl.FromLatLngToLocal(new PointLatLng(geo.X, geo.Y));
            // FromLatLngToLocal returns GPoint (double coordinates) — конвертуємо в System.Drawing.Point
            return new Point((int)Math.Round((double)gpt.X), (int)Math.Round((double)gpt.Y));
        }

        public void AddOverlay(IMapOverlay overlay)
        {
            if (overlay == null) return;
            _overlays.Add(overlay);
            _mapControl?.Invalidate();
        }

        public void Refresh()
        {
            _mapControl?.ReloadMap();
            _mapControl?.Invalidate();
        }

        // --- Обробники подій ---
        private void MapControl_MouseClick(object? sender, MouseEventArgs e)
        {
            if (_mapControl == null) return;
            var latLng = _mapControl.FromLocalToLatLng(e.X, e.Y);
            OnClick?.Invoke(new PointF((float)latLng.Lat, (float)latLng.Lng));
        }

        private void MapControl_OnPositionChanged(PointLatLng point)
        {
            OnPositionChanged?.Invoke(new PointF((float)point.Lat, (float)point.Lng));
        }

        private void MapControl_Paint(object? sender, PaintEventArgs e)
        {
            // Рендеримо наші IMapOverlay зверху карти
            try
            {
                foreach (var ov in _overlays)
                {
                    ov.Draw(e.Graphics);
                }
            }
            catch
            {
                // Безпечна заглушка — щоб помилка в оверлеї не ламала рендер карти
            }
        }
    }
}
