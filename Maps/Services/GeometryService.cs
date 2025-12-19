using System;
using System.Drawing;

namespace Maps.Services
{
    public class GeometryService
    {
        public static double HaversineDistanceMeters(double lat1, double lon1, double lat2, double lon2)
        {
            const double R = 6371000.0;
            double dLat = (lat2 - lat1) * Math.PI / 180.0;
            double dLon = (lon2 - lon1) * Math.PI / 180.0;
            double a = Math.Sin(dLat / 2) * Math.Sin(dLat / 2) +
                       Math.Cos(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) *
                       Math.Sin(dLon / 2) * Math.Sin(dLon / 2);
            return R * 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        }

        public static double BearingDegrees(double lat1, double lon1, double lat2, double lon2)
        {
            double dLon = (lon2 - lon1) * Math.PI / 180.0;
            double y = Math.Sin(dLon) * Math.Cos(lat2 * Math.PI / 180);
            double x = Math.Cos(lat1 * Math.PI / 180) * Math.Sin(lat2 * Math.PI / 180) -
                       Math.Sin(lat1 * Math.PI / 180) * Math.Cos(lat2 * Math.PI / 180) * Math.Cos(dLon);
            double brng = Math.Atan2(y, x) * 180 / Math.PI;
            return (brng + 360) % 360;
        }

        public static float CalculateAzimuth(PointF fromPoint, PointF toPoint)
        {
            float dx = toPoint.X - fromPoint.X;
            float dy = toPoint.Y - fromPoint.Y;
            float angleRad = (float)Math.Atan2(dx, -dy);
            float azimuth = (float)(angleRad * 180 / Math.PI);
            if (azimuth < 0) azimuth += 360;
            return azimuth;
        }

        public static bool IsClickAction(PointF start, PointF end, float threshold = 10f)
        {
            float dx = end.X - start.X;
            float dy = end.Y - start.Y;
            float distance = MathF.Sqrt(dx * dx + dy * dy);
            return distance < threshold;
        }

        public static float CalculateDistance(PointF p1, PointF p2)
        {
            float dx = p2.X - p1.X;
            float dy = p2.Y - p1.Y;
            return MathF.Sqrt(dx * dx + dy * dy);
        }
    }
}
