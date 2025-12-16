using System.Drawing;

namespace Maps.Models
{
    public class AttackZone
    {
        public PointF AttackPoint { get; set; } = PointF.Empty;
        public float Angle { get; set; } = 0f; // degrees
        public float RayLength { get; set; } = 2500f;
        public float SectorRadius { get; set; } = 2500f;
        public float SectorWidth { get; set; } = 30f; // degrees

        public (PointF end, float drawAngle) CalculateEndPoint()
        {
            float drawAngle = Angle - 90f;
            float rad = (float)(drawAngle * Math.PI / 180.0);
            float endX = AttackPoint.X + MathF.Cos(rad) * RayLength;
            float endY = AttackPoint.Y + MathF.Sin(rad) * RayLength;
            return (new PointF(endX, endY), drawAngle);
        }
    }
}