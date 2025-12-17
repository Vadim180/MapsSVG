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
                MinZoom = 10,
                MaxZoom = 25,
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

            // Ensure there are no unexpected native overlays/markers (some environments/plugins may add debug overlays)
            try
            {
                _mapControl.Overlays.Clear();
            }
            catch { }

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

        /// <summary>
        /// Return currently set clicked marker (Lat/Lng) if any.
        /// </summary>
        public PointLatLng? GetClickedMarkerLatLng() => _clickedMarkerLatLng;

        public void ClearClickedMarker()
        {
            _clickedMarkerLatLng = null;
            _mapControl?.Invalidate();
        }

        public void SetLockToAllowedArea(bool enable)
        {
            // No-op for backward compatibility — bounding removed
        }

        /// <summary>
        /// Центрує карту на заданих координатах (WGS84 lat/lng).
        /// </summary>
        public void SetPosition(double latitude, double longitude)
        {
            if (_mapControl == null) return;
            try
            {
                // Ensure we set position on UI thread
                if (_mapControl.InvokeRequired)
                {
                    _mapControl.Invoke(new Action(() =>
                    {
                        Console.WriteLine($"GMapProvider: SetPosition -> {latitude:F6},{longitude:F6}");
                        _mapControl.Position = new PointLatLng(latitude, longitude);
                        try { _mapControl.ReloadMap(); } catch { }
                        try { _mapControl.Invalidate(); } catch { }
                    }));
                }
                else
                {
                    Console.WriteLine($"GMapProvider: SetPosition -> {latitude:F6},{longitude:F6}");
                    _mapControl.Position = new PointLatLng(latitude, longitude);
                    try { _mapControl.ReloadMap(); } catch { }
                    try { _mapControl.Invalidate(); } catch { }
                }
            }
            catch { }
        }

        /// <summary>
        /// Динамічно встановити максимальний рівень зуму (підтримується GMapControl).
        /// </summary>
        private const int MAX_SAFE_ZOOM = 25; // protect against unsupported zoom levels in tile providers

        public void SetMaxZoom(double maxZoom)
        {
            if (_mapControl == null) return;
            try
            {
                int z = (int)Math.Round(maxZoom);
                if (z > MAX_SAFE_ZOOM)
                {
                    Console.WriteLine($"GMapProvider: requested MaxZoom {z} exceeds safe limit {MAX_SAFE_ZOOM}, capping.");
                    z = MAX_SAFE_ZOOM;
                }

                _mapControl.MaxZoom = z;
            }
            catch (Exception ex)
            {
                Console.WriteLine("GMapProvider.SetMaxZoom failed: " + ex.Message);
            }
        }

        /// <summary>
        /// Динамічно встановити мінімальний рівень зуму.
        /// </summary>
        public void SetMinZoom(double minZoom)
        {
            if (_mapControl == null) return;
            try { _mapControl.MinZoom = (int)Math.Round(minZoom); } catch { }
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

        private bool _zoomLimitEnabled = true;

        /// <summary>
        /// Увімкнути/вимкнути застосування верхнього/нижнього ліміту зуму в OnMapZoomChanged.
        /// </summary>
        public void SetZoomLimitEnabled(bool enabled)
        {
            _zoomLimitEnabled = enabled;
        }

        private void MapControl_OnMapZoomChanged()
        {
            if (!_zoomLimitEnabled || _mapControl == null) return;

            try
            {
                // Clamp zoom to map control's MinZoom/MaxZoom
                double current = _mapControl.Zoom;
                double max = _mapControl.MaxZoom;
                double min = _mapControl.MinZoom;

                if (current > max)
                {
                    _mapControl.Zoom = max;
                }
                else if (current < min)
                {
                    _mapControl.Zoom = min;
                }
            }
            catch { }
        }

        private void MapControl_Paint(object? sender, PaintEventArgs e)
        {
            // Рендеримо наші IMapOverlay зверху карти
            try
            {
                // Очистимо будь-які нативні оверлеї/маркери, які явно позначені як debug або мають
                // тип/текст 'Cross' / 'debug' (різні збірки GMap можуть додавати такі артефакти).
                try { CleanupNativeDebugOverlays(); } catch { }

                foreach (var ov in _overlays)
                {
                    ov.Draw(e.Graphics);
                }

                // (No center cover drawing here: we rely on explicit cleanup of native debug overlays/markers.)

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

        private void CleanupNativeDebugOverlays()
        {
            if (_mapControl == null) return;

            // Snapshot to avoid collection modifications while iterating
            bool removed = false;
            var overlays = new List<GMapOverlay>(_mapControl.Overlays);
            foreach (var ov in overlays)
            {
                try
                {
                    if (string.IsNullOrEmpty(ov.Id)) continue;

                    // If overlay id contains debug -> remove entire overlay
                    if (ov.Id.IndexOf("debug", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        Console.WriteLine($"GMapProvider: removing debug overlay '{ov.Id}'");
                        _mapControl.Overlays.Remove(ov);
                        removed = true;
                        continue;
                    }

                    // Remove markers that look like debug/cross markers
                    var markers = new List<GMap.NET.WindowsForms.GMapMarker>(ov.Markers);
                    foreach (var m in markers)
                    {
                        try
                        {
                            var tt = m.ToolTipText;
                            if (!string.IsNullOrEmpty(tt) && tt.IndexOf("debug", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                Console.WriteLine($"GMapProvider: removing debug marker with tooltip '{tt}' in overlay '{ov.Id}'");
                                ov.Markers.Remove(m);
                                removed = true;
                                continue;
                            }

                            var tname = m.GetType().Name;
                            if (tname.IndexOf("Cross", StringComparison.OrdinalIgnoreCase) >= 0 || tname.IndexOf("Debug", StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                ov.Markers.Remove(m);
                                continue;
                            }
                        }
                        catch { }
                    }

                    // If overlay ended up empty and it's not our 'markers' overlay, remove it
                    if (ov.Markers.Count == 0 && ov.Routes.Count == 0 && ov.Polygons.Count == 0 && !string.Equals(ov.Id, "markers", StringComparison.OrdinalIgnoreCase))
                    {
                        _mapControl.Overlays.Remove(ov);
                    }
                }
                catch { }
            }

            if (removed) Console.WriteLine("GMapProvider: removed debug overlays/markers during paint");
        }
    }
}
