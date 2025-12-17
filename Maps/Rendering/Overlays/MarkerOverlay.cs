using System;
using System.Collections.Generic;
using System.Drawing;
using Maps.Models;

namespace Maps.Rendering.Overlays
{
    /// <summary>
    /// Overlay that draws clicked point, calibration markers and scale measurement markers.
    /// Coordinates passed to Render are in map logical coordinates; geoToScreen converts them to screen pixels.
    /// </summary>
    public class MarkerOverlay : IMapOverlay
    {
        private readonly Func<PointF?> _getClickedPoint;
        private readonly Func<PointF[]> _getCalibrationMarkers;
        private readonly Func<int> _getCurrentCornerIndex;
        private readonly Func<bool> _getScaleMarkersVisible;
        private readonly Func<List<ReferencePoint>> _getReferencePoints;

        public MarkerOverlay(
            Func<PointF?> getClickedPoint,
            Func<PointF[]> getCalibrationMarkers,
            Func<int> getCurrentCornerIndex,
            Func<bool> getScaleMarkersVisible,
            Func<List<ReferencePoint>> getReferencePoints)
        {
            _getClickedPoint = getClickedPoint;
            _getCalibrationMarkers = getCalibrationMarkers;
            _getCurrentCornerIndex = getCurrentCornerIndex;
            _getScaleMarkersVisible = getScaleMarkersVisible;
            _getReferencePoints = getReferencePoints;
            Visible = true;
        }

        public bool Visible { get; set; }

        public void Render(Graphics g, Func<PointF, Point> geoToScreen)
        {
            System.Diagnostics.Debug.WriteLine("MarkerOverlay.Render called");
            Console.WriteLine("MarkerOverlay.Render called");
            if (!Visible) return;

            DrawClickedPoint(g, geoToScreen);
            DrawCalibrationMarkers(g, geoToScreen);
            DrawScaleMeasurementMarkers(g, geoToScreen);
        }

        private void DrawClickedPoint(Graphics g, Func<PointF, Point> geoToScreen)
        {
            var pt = _getClickedPoint();
            if (!pt.HasValue) return;

            var screen = geoToScreen(pt.Value);
            float radius = 9f; // screen pixels
            using var b = new SolidBrush(Color.Red);
            g.FillEllipse(b, screen.X - radius / 2, screen.Y - radius / 2, radius, radius);
        }

        private void DrawCalibrationMarkers(Graphics g, Func<PointF, Point> geoToScreen)
        {
            var markers = _getCalibrationMarkers();
            if (markers == null || markers.Length == 0) return;

            int markerCount = Math.Clamp(_getCurrentCornerIndex() + 1, 0, markers.Length);
            using var brush = new SolidBrush(Color.Yellow);
            for (int i = 0; i < markerCount; i++)
            {
                var s = geoToScreen(markers[i]);
                float r = 4f;
                g.FillEllipse(brush, s.X - r, s.Y - r, r * 2, r * 2);
            }
        }

        private void DrawScaleMeasurementMarkers(Graphics g, Func<PointF, Point> geoToScreen)
        {
            if (!_getScaleMarkersVisible()) return;
            var refs = _getReferencePoints();
            if (refs == null || refs.Count < 4) return;

            var topLeft = geoToScreen(refs[0].Pixel);
            var bottomRight = geoToScreen(refs[3].Pixel);

            using var brush = new SolidBrush(Color.Cyan);
            float r = 4f;
            g.FillEllipse(brush, topLeft.X - r, topLeft.Y - r, r * 2, r * 2);
            g.FillEllipse(brush, bottomRight.X - r, bottomRight.Y - r, r * 2, r * 2);
        }
    }
}
