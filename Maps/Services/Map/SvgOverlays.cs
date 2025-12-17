using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Maps.Models;

namespace Maps.Services.Map
{
    // Overlay для зони атаки
    public class SvgAttackZoneOverlay : IMapOverlay
    {
        private readonly Func<AttackZone> _getZone;

        public SvgAttackZoneOverlay(Func<AttackZone> getZone)
        {
            _getZone = getZone;
        }

        public void Draw(Graphics g)
        {
            var zone = _getZone();
            if (zone == null || zone.AttackPoint == PointF.Empty) return;

            // Додатковий лог для дебагу — бачимо, який кут використовуємо при рендері
            System.Diagnostics.Debug.WriteLine($"SvgAttackZoneOverlay.Draw: angle={zone.Angle}, point={zone.AttackPoint}, ray={zone.RayLength}");

            float drawAngle = zone.Angle - 90f;
            float rad = (float)(drawAngle * Math.PI / 180.0);
            float endX = zone.AttackPoint.X + MathF.Cos(rad) * zone.RayLength;
            float endY = zone.AttackPoint.Y + MathF.Sin(rad) * zone.RayLength;

            using var attackPen = new Pen(Color.Red, 1);
            using var attackBrush = new SolidBrush(Color.FromArgb(70, Color.Red));

            float radius = zone.SectorRadius;
            g.FillPie(attackBrush, zone.AttackPoint.X - radius, zone.AttackPoint.Y - radius, radius * 2, radius * 2,
                      drawAngle - (zone.SectorWidth / 2), zone.SectorWidth);

            g.DrawLine(attackPen, zone.AttackPoint.X, zone.AttackPoint.Y, endX, endY);
        }
    }

    // Overlay для точки кліку/маркеру
    public class SvgClickMarkerOverlay : IMapOverlay
    {
        private readonly Func<PointF?> _getClickedPoint;

        public SvgClickMarkerOverlay(Func<PointF?> getClickedPoint)
        {
            _getClickedPoint = getClickedPoint;
        }

        public void Draw(Graphics g)
        {
            var pt = _getClickedPoint();
            if (!pt.HasValue) return;

            float radius = 18f;
            using Brush b = new SolidBrush(Color.Red);
            float scaledRadius = radius; // drawing happens in image space (already transformed by provider)
            g.FillEllipse(b, pt.Value.X - scaledRadius / 2, pt.Value.Y - scaledRadius / 2, scaledRadius, scaledRadius);
        }
    }

    // Overlay для підписів населених пунктів
    public class SvgLocalitiesOverlay : IMapOverlay
    {
        private readonly Func<Dictionary<string, List<PointF>>> _getLocalities;
        private readonly CoordinateConverter? _convProvider;
        private readonly Func<Size> _getCurrentSize;
        private readonly Func<Size> _getOriginalSize;
        private readonly Func<float> _getScale;

        public SvgLocalitiesOverlay(Func<Dictionary<string, List<PointF>>> getLocalities,
            CoordinateConverter? convProvider,
            Func<Size> getOriginalSize,
            Func<Size> getCurrentSize,
            Func<float> getScale)
        {
            _getLocalities = getLocalities;
            _convProvider = convProvider;
            _getOriginalSize = getOriginalSize;
            _getCurrentSize = getCurrentSize;
            _getScale = getScale;
        }

        public void Draw(Graphics g)
        {
            var localities = _getLocalities();
            if (localities == null || _convProvider == null) return;

            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.AntiAlias;

            using var font = new Font("Arial", 9f / Math.Max(0.0001f, _getScale()));
            using var brush = new SolidBrush(Color.White);
            using var outline = new Pen(Color.Black, 0.5f / Math.Max(0.0001f, _getScale()));
            using var format = new StringFormat { Alignment = StringAlignment.Center, LineAlignment = StringAlignment.Center };

            Size orig = _getOriginalSize();
            Size cur = _getCurrentSize();

            foreach (var kvp in localities)
            {
                var points = kvp.Value;
                if (points == null || points.Count == 0) continue;

                var valid = new List<PointF>();

                foreach (var utm in points)
                {
                    var pixel = _convProvider.UTMToPixel(utm);
                    if (float.IsNaN(pixel.X) || float.IsNaN(pixel.Y)) continue;

                    // If bitmap resized relative to original, scale accordingly (approximate)
                    if (orig.Width > 0 && (orig.Width != cur.Width || orig.Height != cur.Height))
                    {
                        float scaleX = cur.Width / (float)orig.Width;
                        float scaleY = cur.Height / (float)orig.Height;
                        pixel = new PointF(pixel.X * scaleX, pixel.Y * scaleY);
                    }

                    if (pixel.X >= 0 && pixel.Y >= 0 && pixel.X <= cur.Width && pixel.Y <= cur.Height)
                        valid.Add(pixel);
                }

                if (valid.Count == 0) continue;

                float avgX = valid.Average(p => p.X);
                float avgY = valid.Average(p => p.Y);
                var pos = new PointF(avgX, avgY);

                g.DrawRectangle(outline, pos.X - 1, pos.Y - 1, 2, 2);
                g.DrawString(kvp.Key, font, brush, pos, format);
            }
        }
    }
}
