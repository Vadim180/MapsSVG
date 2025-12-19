using System;
using System.Drawing;

namespace Maps.Rendering.Overlays
{
    // Overlay для відображення кліку на карті
    public class MarkerOverlay : IMapOverlay
    {
        private readonly Func<PointF?> _getClickedPoint;

        public MarkerOverlay(Func<PointF?> getClickedPoint)
        {
            _getClickedPoint = getClickedPoint;
        }

        public bool IsVisible { get; set; } = true;
        public int ZIndex => 150;

        public void Draw(Graphics g, RectangleF viewArea, Func<PointF, PointF>? coordTransform = null)
        {
            if (!IsVisible) return;
            
            var pt = _getClickedPoint();
            if (!pt.HasValue) return;

            var screen = coordTransform?.Invoke(pt.Value) ?? pt.Value;
            float radius = 9f;
            using var brush = new SolidBrush(Color.Red);
            g.FillEllipse(brush, screen.X - radius / 2, screen.Y - radius / 2, radius, radius);
        }
    }
}
