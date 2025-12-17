using System;
using System.Drawing;
using Maps.Models;

namespace Maps.Services.Map
{
    /// <summary>
    /// Overlay used by GMap provider to draw attack sector and ray in client (pixel) coordinates.
    /// </summary>
    public class GMapAttackZoneOverlay : IMapOverlay
    {
        private readonly Func<AttackZone> _getZone;

        public GMapAttackZoneOverlay(Func<AttackZone> getZone)
        {
            _getZone = getZone;
        }

        public void Draw(Graphics g)
        {
            var zone = _getZone();
            if (zone == null || zone.AttackPoint == PointF.Empty) return;

            try
            {
                float drawAngle = zone.Angle - 90f;
                float drawAngleRad = (float)(drawAngle * Math.PI / 180.0);

                using var attackBrush = new SolidBrush(Color.FromArgb(70, Color.Red));
                using var attackPen = new Pen(Color.Red, 2);

                float radius = zone.SectorRadius;
                g.FillPie(attackBrush, zone.AttackPoint.X - radius, zone.AttackPoint.Y - radius,
                          radius * 2, radius * 2, drawAngle - (zone.SectorWidth / 2), zone.SectorWidth);

                float endX = zone.AttackPoint.X + MathF.Cos(drawAngleRad) * zone.RayLength;
                float endY = zone.AttackPoint.Y + MathF.Sin(drawAngleRad) * zone.RayLength;
                g.DrawLine(attackPen, zone.AttackPoint.X, zone.AttackPoint.Y, endX, endY);

                // Small center marker
                using var centerBrush = new SolidBrush(Color.Red);
                float r = 6f;
                g.FillEllipse(centerBrush, zone.AttackPoint.X - r / 2, zone.AttackPoint.Y - r / 2, r, r);
            }
            catch
            {
                // Suppress errors in overlay drawing
            }
        }
    }
}
