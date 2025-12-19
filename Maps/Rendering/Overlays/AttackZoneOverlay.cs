using System;
using System.Drawing;
using Maps.Models;

namespace Maps.Rendering.Overlays
{
    public class AttackZoneOverlay : IMapOverlay
    {
        private readonly Func<AttackZone> _getZone;
        
        public bool IsVisible { get; set; } = true;
        public int ZIndex => 100;

        public AttackZoneOverlay(Func<AttackZone> getZone)
        {
            _getZone = getZone ?? throw new ArgumentNullException(nameof(getZone));
        }

        public void Draw(Graphics g, RectangleF viewArea, Func<PointF, PointF>? coordTransform = null)
        {
            if (!IsVisible) return;
            
            var zone = _getZone();
            if (zone.AttackPoint == PointF.Empty) return;

            var center = coordTransform?.Invoke(zone.AttackPoint) ?? zone.AttackPoint;

            float drawAngle = zone.Angle - 90f;
            float drawAngleRad = MathF.PI * (zone.Angle - 90) / 180f;

            using var attackBrush = new SolidBrush(Color.FromArgb(70, Color.Red));
            using var attackPen = new Pen(Color.Red, 2);

            float radius = zone.SectorRadius;

            try
            {
                g.FillPie(attackBrush, center.X - radius, center.Y - radius, radius * 2, radius * 2,
                    drawAngle - (zone.SectorWidth / 2), zone.SectorWidth);
            }
            catch { }

            float endX = center.X + MathF.Cos(drawAngleRad) * zone.RayLength;
            float endY = center.Y + MathF.Sin(drawAngleRad) * zone.RayLength;

            g.DrawLine(attackPen, center.X, center.Y, endX, endY);

            using var centerBrush = new SolidBrush(Color.Red);
            float r = 6f;
            g.FillEllipse(centerBrush, center.X - r / 2, center.Y - r / 2, r, r);
        }
    }
}
