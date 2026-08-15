using System;
using System.Diagnostics;
using System.Globalization;
using System.Windows;
using System.Windows.Media;

using GMap.NET;
using GMap.NET.WindowsPresentation;

namespace MapsWPF
{
    /// <summary>
    ///     The custom map of GMapControl
    /// </summary>
    public class Map : GMapControl
    {
        public long ElapsedMilliseconds;

        // Attack zone properties
        // Changed to PointLatLng to anchor to map
        public PointLatLng AttackPoint { get; set; } = PointLatLng.Empty;
        public bool IsAttackPointSet { get; set; } = false;

        public float AttackAngle { get; set; } = 0f;

        // Computed display angle for servo overlay (angle to show next to "СЕРВА")
        public float ServoAngleDisplay { get; set; } = float.NaN;

        // Dimensions in METERS now, not pixels
        public double AttackRayLengthMeters { get; set; } = 5000;
        public double AttackSectorRadiusMeters { get; set; } = 5000;

        public float AttackSectorWidth { get; set; } = 30f;
        public double TargetDistance { get; set; } = -1; // Distance to target in meters

#if DEBUG

        readonly Typeface _tf = new("GenericSansSerif");
        readonly FlowDirection fd = new FlowDirection();
        public static readonly Stopwatch _stopwatch = new();

        // Cache для геометрії сектору (оптимізація!)
        private PathGeometry _cachedSectorGeometry;
        private float _lastCachedAngle = float.MinValue;
        private float _lastCachedWidth = float.MinValue;
        private double _lastCachedRadiusMeters = double.MinValue;
        private PointLatLng _lastCachedPoint = PointLatLng.Empty;
        private double _lastCachedZoom = -1;
        private Point _lastCachedCenter = new Point(double.MinValue, double.MinValue);

        // Статичні ресурси (створюються один раз)
        private static readonly SolidColorBrush _sectorBrush;
        private static readonly Pen _sectorPen = null;
        private static readonly Pen _rayPen;

        private static readonly Typeface textTypeFace = new(fontFamily: new System.Windows.Media.FontFamily("FontStretches.Normal"),
                                                            weight: FontWeights.Normal,
                                                            style: FontStyles.Normal,
                                                            stretch: FontStretches.Medium);

        private static readonly Typeface textTypeFaceServo = new(fontFamily: new System.Windows.Media.FontFamily("FontStretches.Normal"),
                                                    weight: FontWeights.Bold,
                                                    style: FontStyles.Normal,
                                                    stretch: FontStretches.Medium);

        static Map()
        {
            // Створюємо статичні ресурси один раз для всіх екземплярів
            _sectorBrush = new SolidColorBrush(Color.FromArgb(75, 255, 0, 0));
            _sectorBrush.Freeze();
            //_sectorPen = new Pen(Brushes.Red, 1);
            //_sectorPen.Freeze();
            _rayPen = new Pen(Brushes.Red, 2);
            _rayPen.Freeze();
            // Overlay resources for coordinates label (match WinForms labelCoordinates)
            // Use a stronger alpha so the background is visibly semi-transparent over the map
            _overlayBackground = new SolidColorBrush(Color.FromArgb(255, 30, 30, 30));
            _overlayBackground.Freeze();

            _overlayBorder = new Pen(new SolidColorBrush(Color.FromArgb(255, 120, 120, 120)), 1);

            _overlayBorder.Freeze();

            _overlayTextBrush = new SolidColorBrush(Colors.White);
            _overlayTextBrush.Freeze();

            _workAreaEditFillBrush = new SolidColorBrush(Color.FromArgb(45, 0, 191, 255));
            _workAreaEditFillBrush.Freeze();

            var workAreaStrokeBrush = new SolidColorBrush(Color.FromArgb(255, 0, 191, 255));
            workAreaStrokeBrush.Freeze();

            _workAreaEditPen = new Pen(workAreaStrokeBrush, 2);
            _workAreaEditPen.Freeze();

            var workAreaBufferBrush = new SolidColorBrush(
                Color.FromArgb(255, 255, 140, 0));
            workAreaBufferBrush.Freeze();

            _workAreaBufferPen = new Pen(workAreaBufferBrush, 2.5)
            {
                DashStyle = DashStyles.Dash
            };
            _workAreaBufferPen.Freeze();
                       
        }

        // Overlay drawing resources
        private static readonly SolidColorBrush _overlayBackground;
        private static readonly Pen _overlayBorder;
        private static readonly SolidColorBrush _overlayTextBrush;

        private static readonly SolidColorBrush _workAreaEditFillBrush;
        private static readonly Pen _workAreaEditPen;
        private static readonly Pen _workAreaBufferPen;

        /// <summary>
        /// Optional provider that given a lat/lng returns additional coordinate strings (UTM, MGRS, etc.).
        /// MainWindow can set this to render UTM/MGRS the same way as the WinForms app.
        /// </summary>
        public System.Func<GMap.NET.PointLatLng, (string UTM, string MGRS)> CoordinateFormatter { get; set; }

        /// <summary>
        /// Show coordinates overlay
        /// </summary>
        public bool ShowCoordinates { get; set; } = true;

        public bool IsWorkAreaEditVisible { get; set; }

        public bool IsWorkAreaEditFillVisible { get; set; } = true;

        public RectLatLng? WorkAreaEditBounds { get; set; }

        public bool IsWorkAreaBufferVisible { get; set; }

        public RectLatLng? WorkAreaBufferBounds { get; set; }

        public bool IsWorkAreaLimitDebugVisible { get; set; }

        public RectLatLng? WorkAreaLimitDebugBounds { get; set; }

        /// <summary>
        ///     any custom drawing here
        /// </summary>
        /// <param name="drawingContext"></param>
        protected override void OnRender(DrawingContext drawingContext)
        {
            _stopwatch.Reset();
            _stopwatch.Start();

            base.OnRender(drawingContext);

            DrawWorkAreaLimitDebugBounds(drawingContext);
            DrawWorkAreaBufferBounds(drawingContext);
            DrawWorkAreaEditBounds(drawingContext);

            // Draw attack zone if attack point is set
            if (IsAttackPointSet)
            {
                DrawAttackZone(drawingContext);
            }

            // Draw coordinates overlay (top-right corner) using WinForms style (black text on semi-transparent background)
            if (ShowCoordinates)
            {
                DrawCoordinatesOverlay(drawingContext);
            }

            _stopwatch.Stop();

            // Draw elapsed time in milliseconds (debug)
            var text = new FormattedText(
                _stopwatch.ElapsedMilliseconds +
                "ms",
                CultureInfo.InvariantCulture,
                fd,
                _tf,
                12,
                Brushes.Red,
                VisualTreeHelper.GetDpi(this).PixelsPerDip);

            // Правий нижній кут карти з відступом 10 пікселів
            drawingContext.DrawText(text, new Point(ActualWidth - text.Width - 10, ActualHeight - text.Height - 20));
        }

        private void DrawWorkAreaEditBounds(DrawingContext dc)
        {
            if (!IsWorkAreaEditVisible || !WorkAreaEditBounds.HasValue)
            {
                return;
            }

            var bounds = WorkAreaEditBounds.Value;

            var topLeft = new PointLatLng(bounds.Top, bounds.Left);
            var bottomRight = new PointLatLng(bounds.Bottom, bounds.Right);

            var topLeftLocal = FromLatLngToLocal(topLeft);
            var bottomRightLocal = FromLatLngToLocal(bottomRight);

            var left = Math.Min(topLeftLocal.X, bottomRightLocal.X);
            var top = Math.Min(topLeftLocal.Y, bottomRightLocal.Y);
            var right = Math.Max(topLeftLocal.X, bottomRightLocal.X);
            var bottom = Math.Max(topLeftLocal.Y, bottomRightLocal.Y);

            var width = right - left;
            var height = bottom - top;

            if (width < 1 || height < 1)
            {
                return;
            }

            var rect = new Rect(
                left,
                top,
                width,
                height
            );

            var fill = IsWorkAreaEditFillVisible
                ? _workAreaEditFillBrush
                : null;

            dc.DrawRectangle(
                fill,
                _workAreaEditPen,
                rect
            );
        }

        private void DrawWorkAreaLimitDebugBounds(DrawingContext dc)
        {
            if (!IsWorkAreaLimitDebugVisible || !WorkAreaLimitDebugBounds.HasValue)
            {
                return;
            }

            var bounds = WorkAreaLimitDebugBounds.Value;

            var topLeft = new PointLatLng(bounds.Top, bounds.Left);
            var bottomRight = new PointLatLng(bounds.Bottom, bounds.Right);

            var topLeftLocal = FromLatLngToLocal(topLeft);
            var bottomRightLocal = FromLatLngToLocal(bottomRight);

            var left = Math.Min(topLeftLocal.X, bottomRightLocal.X);
            var top = Math.Min(topLeftLocal.Y, bottomRightLocal.Y);
            var right = Math.Max(topLeftLocal.X, bottomRightLocal.X);
            var bottom = Math.Max(topLeftLocal.Y, bottomRightLocal.Y);

            var width = right - left;
            var height = bottom - top;

            if (width < 1 || height < 1)
            {
                return;
            }

            var rect = new Rect(
                left,
                top,
                width,
                height
            );

            dc.DrawRectangle(
                null,
                _workAreaEditPen,
                rect
            );
        }

        private void DrawWorkAreaBufferBounds(DrawingContext dc)
        {
            if (!IsWorkAreaBufferVisible || !WorkAreaBufferBounds.HasValue)
            {
                return;
            }

            var bounds = WorkAreaBufferBounds.Value;

            var topLeft = new PointLatLng(bounds.Top, bounds.Left);
            var bottomRight = new PointLatLng(bounds.Bottom, bounds.Right);

            var topLeftLocal = FromLatLngToLocal(topLeft);
            var bottomRightLocal = FromLatLngToLocal(bottomRight);

            var left = Math.Min(topLeftLocal.X, bottomRightLocal.X);
            var top = Math.Min(topLeftLocal.Y, bottomRightLocal.Y);
            var right = Math.Max(topLeftLocal.X, bottomRightLocal.X);
            var bottom = Math.Max(topLeftLocal.Y, bottomRightLocal.Y);

            var width = right - left;
            var height = bottom - top;

            if (width < 1 || height < 1)
            {
                return;
            }

            dc.DrawRectangle(
                null,
                _workAreaBufferPen,
                new Rect(left, top, width, height));
        }

        private void DrawAttackZone(DrawingContext dc)
        {
            // Convert Geo point to Screen point
            var gCenter = FromLatLngToLocal(AttackPoint);
            var center = new Point(gCenter.X, gCenter.Y);

            // Calculate radius in pixels based on zoom level
            // We take a point at distance X North and measure pixel distance
            // This is an approximation but good enough for visual
            // Better: use MapProvider projection

            var pxRadius = GetPixelDistance(AttackPoint, AttackSectorRadiusMeters);
            var pxRayLength = GetPixelDistance(AttackPoint, AttackRayLengthMeters);

            // Перевіряємо чи змінилися параметри (якщо ні - використовуємо кеш!)
            bool needsUpdate = _cachedSectorGeometry == null ||
                              _lastCachedAngle != AttackAngle ||
                              _lastCachedWidth != AttackSectorWidth ||
                              Math.Abs(_lastCachedRadiusMeters - AttackSectorRadiusMeters) > 0.001 ||
                              _lastCachedPoint != AttackPoint ||
                              Math.Abs(_lastCachedZoom - Zoom) > 0.01 ||
                              _lastCachedCenter != center;

            if (needsUpdate)
            {
                // Тільки якщо змінилося - перестворюємо геометрію
                float drawAngle = AttackAngle - 90f;
                // Use calculated pixel radius
                var radius = pxRadius;

                var startAngle = drawAngle - (AttackSectorWidth / 2);
                var endAngle = drawAngle + (AttackSectorWidth / 2);

                var startRad = startAngle * System.Math.PI / 180.0;
                var endRad = endAngle * System.Math.PI / 180.0;

                var startPoint = new Point(
                    center.X + radius * System.Math.Cos(startRad),
                    center.Y + radius * System.Math.Sin(startRad));

                var endPoint = new Point(
                    center.X + radius * System.Math.Cos(endRad),
                    center.Y + radius * System.Math.Sin(endRad));

                // Створюємо нову геометрію
                var pathGeometry = new PathGeometry();
                var pathFigure = new PathFigure
                {
                    StartPoint = center,
                    IsClosed = true
                };

                pathFigure.Segments.Add(new LineSegment(startPoint, true));
                pathFigure.Segments.Add(new ArcSegment(endPoint, new Size(radius, radius), 0,
                    AttackSectorWidth > 180, SweepDirection.Clockwise, true));
                pathFigure.Segments.Add(new LineSegment(center, true));

                pathGeometry.Figures.Add(pathFigure);
                pathGeometry.Freeze(); // Заморожуємо для швидкості

                // Зберігаємо в кеш
                _cachedSectorGeometry = pathGeometry;
                _lastCachedAngle = AttackAngle;
                _lastCachedWidth = AttackSectorWidth;
                _lastCachedRadiusMeters = AttackSectorRadiusMeters;
                _lastCachedPoint = AttackPoint;
                _lastCachedZoom = Zoom;
                _lastCachedCenter = center;
            }

            // Малюємо сектор з кешу (швидко!)
            dc.DrawGeometry(_sectorBrush, _sectorPen, _cachedSectorGeometry);

            // Малюємо промінь (центральна лінія)
            float drawAngleForRay = AttackAngle - 90f;
            float rad = (float)(drawAngleForRay * System.Math.PI / 180.0);

            // Use calculated pixel len
            float endX = (float)(center.X + System.Math.Cos(rad) * pxRayLength);
            float endY = (float)(center.Y + System.Math.Sin(rad) * pxRayLength);
            dc.DrawLine(_rayPen, center, new Point(endX, endY));

            // Малюємо центральний маркер (червоне коло)
            dc.DrawEllipse(Brushes.Red, null, center, 3, 3);
        }

        private double GetPixelDistance(PointLatLng centerInfo, double meters)
        {
            if (MapProvider == null) return 0;

            // Simplest way:
            // 1. Get center PX
            // 2. Calculate point "meters" away in lat/lng
            // 3. Get that point PX
            // 4. Distance

            var p1G = FromLatLngToLocal(centerInfo);
            var p1 = new Point(p1G.X, p1G.Y);

            // Move East
            var offsetPoint = DestPoint(centerInfo, 90, meters / 1000.0); // Meters to KM
            var p2G = FromLatLngToLocal(offsetPoint);
            var p2 = new Point(p2G.X, p2G.Y);

            var dx = p1.X - p2.X;
            var dy = p1.Y - p2.Y;
            return System.Math.Sqrt(dx * dx + dy * dy);
        }

        // Helper for destination point given distance in km
        private PointLatLng DestPoint(PointLatLng start, double bearing, double distKm)
        {
            var R = 6371d; // Earth Radius
            var brng = bearing * System.Math.PI / 180d;
            var lat1 = start.Lat * System.Math.PI / 180d;
            var lon1 = start.Lng * System.Math.PI / 180d;

            var lat2 = System.Math.Asin(System.Math.Sin(lat1) * System.Math.Cos(distKm / R) +
                                  System.Math.Cos(lat1) * System.Math.Sin(distKm / R) * System.Math.Cos(brng));
            var lon2 = lon1 + System.Math.Atan2(System.Math.Sin(brng) * System.Math.Sin(distKm / R) * System.Math.Cos(lat1),
                                          System.Math.Cos(distKm / R) - System.Math.Sin(lat1) * System.Math.Sin(lat2));

            return new PointLatLng(lat2 * 180d / System.Math.PI, lon2 * 180d / System.Math.PI);
        }

        public string AzimuthText { get; set; } = string.Empty;
        public PointLatLng? MousePositionLatLng { get; set; } = null;

        private void DrawCoordinatesOverlay(DrawingContext dc)
        {
            try
            {
                // Use center of map point instead of pixel output
                double lat = this.Position.Lat;
                double lng = this.Position.Lng;
                string lineLatLng = $"Lat/Lng: {lat:F6}, {lng:F6}";
                string utm = null;
                string mgrs = null;
                string mgrsCursor = null;

                if (CoordinateFormatter != null)
                {
                    try
                    {
                        var extra = CoordinateFormatter(new PointLatLng(lat, lng));
                        utm = extra.UTM;
                        mgrs = extra.MGRS;

                        if (MousePositionLatLng.HasValue)
                        {
                            var extraCursor = CoordinateFormatter(MousePositionLatLng.Value);
                            mgrsCursor = extraCursor.MGRS;
                        }
                    }
                    catch { }
                }

                // Build rows (label / value) so we can align values in a column
                var rows = new System.Collections.Generic.List<(string Label, string Value)>();
                rows.Add(("Lat/Lng:", $"{lat:F6}, {lng:F6}"));
                // Show MGRS above UTM for better readability in overlay
                if (!string.IsNullOrEmpty(mgrs)) rows.Add(("MGRS:", mgrs));
                if (!string.IsNullOrEmpty(mgrsCursor)) rows.Add(("MGRS (Cursor):", mgrsCursor));
                if (!string.IsNullOrEmpty(utm)) rows.Add(("UTM:", utm));
                // Put distance (in km) on its own row, then azimuth and angle on separate rows
                string az = !string.IsNullOrEmpty(AzimuthText) ? AzimuthText : "-";
                string angle = AttackAngle.ToString("F1", CultureInfo.InvariantCulture) + "°";
                string dist = TargetDistance >= 0 ? $"{(TargetDistance / 1000.0):F1} km" : "-";

                rows.Add(("Відстань:", dist));
                rows.Add(("Азимут:", az));
                rows.Add(("Кут:", angle));

                double padding = 10.0;
                double labelSpacing = 8.0;
                double margin = 10.0;

                // Prepare FormattedText for each label/value and measure widths/heights
                var labelFts = new System.Collections.Generic.List<FormattedText>();
                var valueFts = new System.Collections.Generic.List<FormattedText>();
                var rowHeights = new System.Collections.Generic.List<double>();

                double maxLabelWidth = 0;
                double maxValueWidth = 0;

                foreach (var row in rows)
                {
                    var pixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                    var lft = new FormattedText(row.Label, CultureInfo.InvariantCulture, fd, textTypeFace, 14, _overlayTextBrush, pixelsPerDip);
                    var vft = new FormattedText(row.Value, CultureInfo.InvariantCulture, fd, textTypeFace, 14, _overlayTextBrush, pixelsPerDip);
                    labelFts.Add(lft);
                    valueFts.Add(vft);
                    double rh = System.Math.Max(lft.Height, vft.Height);
                    rowHeights.Add(rh);
                    if (lft.Width > maxLabelWidth) maxLabelWidth = lft.Width;
                    if (vft.Width > maxValueWidth) maxValueWidth = vft.Width;
                }

                double totalRowsHeight = 0;
                foreach (var h in rowHeights) totalRowsHeight += h;

                double rectWidth = padding * 2 + maxLabelWidth + labelSpacing + maxValueWidth;
                double rectHeight = padding * 2 + totalRowsHeight;

                double rectX = ActualWidth - rectWidth - margin;
                double rectY = margin;

                // Draw background and border for the overlay
                dc.DrawRectangle(_overlayBackground, _overlayBorder, new Rect(rectX, rectY, rectWidth, rectHeight));

                // Draw each label and its value aligned in columns
                double y = rectY + padding;
                double xLabel = rectX + padding;
                double xValue = rectX + padding + maxLabelWidth + labelSpacing;

                for (int i = 0; i < rows.Count; i++)
                {
                    dc.DrawText(labelFts[i], new Point(xLabel, y));
                    dc.DrawText(valueFts[i], new Point(xValue, y));
                    y += rowHeights[i];
                }

                // Draw a small secondary overlay below the main one, aligned to the right,
                // roughly half the width and half the height of the main overlay.
                double spacing = 5.0;
                double smallRectWidth = Math.Max(60.0, rectWidth / 2.2);
                double smallRectHeight = Math.Max(30.0, rectHeight / 3.5);
                double smallRectX = ActualWidth - smallRectWidth - margin;
                double smallRectY = rectY + rectHeight + spacing;

                // Ensure small overlay fits inside the control
                if (smallRectY + smallRectHeight + margin > ActualHeight)
                {
                    smallRectY = Math.Max(margin, ActualHeight - smallRectHeight - margin);
                }

                dc.DrawRectangle(_overlayBackground, _overlayBorder, new Rect(smallRectX, smallRectY, smallRectWidth, smallRectHeight));

                var dpiPixelsPerDip = VisualTreeHelper.GetDpi(this).PixelsPerDip;

                // Draw label "СЕРВА" fixed at a consistent X inside the small overlay,
                // and draw numeric angle to the right so changing digit width doesn't shift the label.
                var labelText = new FormattedText("СЕРВА", CultureInfo.InvariantCulture, fd, textTypeFaceServo, 18, _overlayTextBrush, dpiPixelsPerDip);
                double paddingServo = 18.0;
                double spacingServo = 8.0;

                double labelX = smallRectX + paddingServo;
                double labelY = smallRectY + (smallRectHeight - labelText.Height) / 2.0;
                dc.DrawText(labelText, new Point(labelX, labelY));

                if (!float.IsNaN(ServoAngleDisplay))
                {
                    string angleStr = $"{ServoAngleDisplay:F1}°";
                    var angleText = new FormattedText(angleStr, CultureInfo.InvariantCulture, fd, textTypeFaceServo, 18, _overlayTextBrush, dpiPixelsPerDip);
                    double angleX = labelX + labelText.Width + spacingServo;

                    // Prevent angle from overflowing the small overlay's right edge; if it would, shift it left but keep label fixed
                    double maxRight = smallRectX + smallRectWidth - paddingServo;
                    if (angleX + angleText.Width > maxRight)
                    {
                        angleX = Math.Max(labelX + labelText.Width + spacingServo, maxRight - angleText.Width);
                    }

                    double angleY = smallRectY + (smallRectHeight - angleText.Height) / 2.0;
                    dc.DrawText(angleText, new Point(angleX, angleY));
                }
            }
            catch { }
        }
#endif
    }
}
