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

        // Маркер кліка у координатах Lat/Lng (підтримуємо для GMap)
        private GMap.NET.PointLatLng? _clickedMarkerLatLng = null;

        // (Bound restriction removed)

        public event Action<PointF>? OnClick;
        public event Action<PointF>? OnPositionChanged;

        /// <summary>
        /// Доступ до внутрішнього GMapControl для зворотної сумісності з існуючим кодом.
        /// </summary>
        public GMapControl? Control => _mapControl;

        /// <summary>
        /// Збережено для сумісності, але більше не робить нічого (обмеження видалено).
        /// </summary>
        public bool LockToAllowedArea
        {
            get => false;
            set { /* no-op: bounding removed */ }
        }

        public void Initialize(Control container)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));

            if (_mapControl != null)
            {
                // Відпідписуємося від подій для безпечної ре-ініціалізації
                _mapControl.MouseClick -= MapControl_MouseClick;
                _mapControl.OnPositionChanged -= MapControl_OnPositionChanged;
                _mapControl.OnMapZoomChanged -= MapControl_OnMapZoomChanged;
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
            _mapControl.OnMapZoomChanged += MapControl_OnMapZoomChanged;
            _mapControl.Paint += MapControl_Paint;

            container.Controls.Add(_mapControl);
            container.Controls.SetChildIndex(_mapControl, 0);
        }

        public void SetBounds(double north, double south, double east, double west)
        {
            if (_mapControl == null) return;

            // Просто виставляємо центр області та перемальовуємо — без збереження allowed area
            double centerLat = (north + south) / 2.0;
            double centerLng = (west + east) / 2.0;

            _mapControl.Position = new PointLatLng(centerLat, centerLng);
            _mapControl.Invalidate();
        }

        // EnsureViewAreaInsideAllowed removed — bounding behaviour was disabled per request.

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

        // Position comparison helpers removed with bounding behavior

        public void SetClickedMarker(PointLatLng latLng)
        {
            _clickedMarkerLatLng = latLng;
            _mapControl?.Invalidate();
        }

        public void ClearClickedMarker()
        {
            _clickedMarkerLatLng = null;
            _mapControl?.Invalidate();
        }

        public void SetLockToAllowedArea(bool enable)
        {
            // No-op for backward compatibility — bounding removed
        }

        // --- Обробники подій ---
        private void MapControl_MouseClick(object? sender, MouseEventArgs e)
        {
            if (_mapControl == null) return;

            // Відображаємо маркер лише на одиночний правий клік
            if (e.Button == MouseButtons.Right)
            {
                var latLng = _mapControl.FromLocalToLatLng(e.X, e.Y);

                // Зберігаємо маркер кліка у внутрішній змінній та перемальовуємо
                _clickedMarkerLatLng = new PointLatLng(latLng.Lat, latLng.Lng);
                try { _mapControl.Invalidate(); } catch { }

                OnClick?.Invoke(new PointF((float)latLng.Lat, (float)latLng.Lng));
            }
        }

        private void MapControl_OnPositionChanged(PointLatLng point)
        {
            // Просто передаємо позицію далі — обмеження переміщення видалено
            OnPositionChanged?.Invoke(new PointF((float)point.Lat, (float)point.Lng));
        }

        private void MapControl_OnMapZoomChanged()
        {
            // No-op: bounding removed
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

                // Якщо є маркер кліка у LatLng — намалюємо його в локальних пікселях
                if (_clickedMarkerLatLng.HasValue)
                {
                    var gpt = _mapControl.FromLatLngToLocal(_clickedMarkerLatLng.Value);
                    using var b = new SolidBrush(Color.Red);
                    using var p = new Pen(Color.Black, 1);
                    float x = (float)gpt.X;
                    float y = (float)gpt.Y;
                    float r = 8f;
                    e.Graphics.FillEllipse(b, x - r, y - r, r * 2, r * 2);
                    e.Graphics.DrawEllipse(p, x - r, y - r, r * 2, r * 2);
                }
            }
            catch
            {
                // Безпечна заглушка — щоб помилка в оверлеї не ламала рендер карти
            }
        }
    }
}
