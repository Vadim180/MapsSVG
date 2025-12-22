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
        public System.Drawing.PointF AttackPoint { get; set; } = System.Drawing.PointF.Empty;
        public float AttackAngle { get; set; } = 0f;
        public float AttackRayLength { get; set; } = 500;
        public float AttackSectorRadius { get; set; } = 500;
        public float AttackSectorWidth { get; set; } = 30f;

#if DEBUG

        readonly Typeface _tf = new("GenericSansSerif");
        readonly FlowDirection fd = new FlowDirection();
        public static readonly Stopwatch _stopwatch = new();

        // Cache для геометрії сектору (оптимізація!)
        private PathGeometry _cachedSectorGeometry;
        private float _lastCachedAngle = float.MinValue;
        private float _lastCachedWidth = float.MinValue;
        private float _lastCachedRadius = float.MinValue;
        private System.Drawing.PointF _lastCachedPoint = System.Drawing.PointF.Empty;

        // Статичні ресурси (створюються один раз)
        private static readonly SolidColorBrush _sectorBrush;
        private static readonly Pen _sectorPen = null;
        private static readonly Pen _rayPen;

        private static readonly Typeface textTypeFace = new(fontFamily: new System.Windows.Media.FontFamily("FontStretches.Normal"),
                                                            weight: FontWeights.Normal,
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
        }

        // Overlay drawing resources
        private static readonly SolidColorBrush _overlayBackground;
        private static readonly Pen _overlayBorder;
        private static readonly SolidColorBrush _overlayTextBrush;
        
        /// <summary>
        /// Optional provider that given a lat/lng returns additional coordinate strings (UTM, MGRS, etc.).
        /// MainWindow can set this to render UTM/MGRS the same way as the WinForms app.
        /// </summary>
        public System.Func<GMap.NET.PointLatLng, (string UTM, string MGRS)> CoordinateFormatter { get; set; }

        /// <summary>
        ///     any custom drawing here
        /// </summary>
        /// <param name="drawingContext"></param>
        protected override void OnRender(DrawingContext drawingContext)
        {
            _stopwatch.Reset();
            _stopwatch.Start();

            base.OnRender(drawingContext);

            // Draw attack zone if attack point is set
            if (AttackPoint != System.Drawing.PointF.Empty)
            {
                DrawAttackZone(drawingContext);
            }

            // Draw coordinates overlay (top-right corner) using WinForms style (black text on semi-transparent background)
            DrawCoordinatesOverlay(drawingContext);

            _stopwatch.Stop();

            var text = new FormattedText(
                _stopwatch.ElapsedMilliseconds +
                "ms",
                CultureInfo.InvariantCulture,
                fd,
                _tf,
                12,
                Brushes.Red);

            // Правий нижній кут карти з відступом 10 пікселів
            drawingContext.DrawText(text, new Point(ActualWidth - text.Width - 10, ActualHeight - text.Height - 10));
        }

        private void DrawAttackZone(DrawingContext dc)
        {
            var center = new Point(AttackPoint.X, AttackPoint.Y);

            // Перевіряємо чи змінилися параметри (якщо ні - використовуємо кеш!)
            bool needsUpdate = _cachedSectorGeometry == null ||
                              _lastCachedAngle != AttackAngle ||
                              _lastCachedWidth != AttackSectorWidth ||
                              _lastCachedRadius != AttackSectorRadius ||
                              _lastCachedPoint != AttackPoint;

            if (needsUpdate)
            {
                // Тільки якщо змінилося - перестворюємо геометрію
                float drawAngle = AttackAngle - 90f;
                var radius = AttackSectorRadius;

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
                _lastCachedRadius = AttackSectorRadius;
                _lastCachedPoint = AttackPoint;
            }

            // Малюємо сектор з кешу (швидко!)
            dc.DrawGeometry(_sectorBrush, _sectorPen, _cachedSectorGeometry);

            // Малюємо промінь (центральна лінія)
            float drawAngleForRay = AttackAngle - 90f;
            float rad = (float)(drawAngleForRay * System.Math.PI / 180.0);
            float endX = AttackPoint.X + (float)System.Math.Cos(rad) * AttackRayLength;
            float endY = AttackPoint.Y + (float)System.Math.Sin(rad) * AttackRayLength;
            dc.DrawLine(_rayPen, center, new Point(endX, endY));

            // Малюємо центральний маркер (червоне коло)
            dc.DrawEllipse(Brushes.Red, null, center, 3, 3);
        }

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
                if (CoordinateFormatter != null)
                {
                    try
                    {
                        var extra = CoordinateFormatter(new PointLatLng(lat, lng));
                        utm = extra.UTM;
                        mgrs = extra.MGRS;
                    }
                    catch { }
                }

                // Build rows (label / value) so we can align values in a column
                var rows = new System.Collections.Generic.List<(string Label, string Value)>();
                rows.Add(("Lat/Lng:", $"{lat:F6}, {lng:F6}"));
                if (!string.IsNullOrEmpty(utm)) rows.Add(("UTM:", utm));
                if (!string.IsNullOrEmpty(mgrs)) rows.Add(("MGRS:", mgrs));
                rows.Add(("Кут:", AttackAngle.ToString("F1", CultureInfo.InvariantCulture) + "°"));

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
                    var lft = new FormattedText(row.Label, CultureInfo.InvariantCulture, fd, textTypeFace, 14, _overlayTextBrush);
                    var vft = new FormattedText(row.Value, CultureInfo.InvariantCulture, fd, textTypeFace, 14, _overlayTextBrush);
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
            }
            catch { }
        }
#endif
    }
}
