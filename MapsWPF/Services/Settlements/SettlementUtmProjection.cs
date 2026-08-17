using System;
using System.Drawing;
using CoordinateSharp;

namespace MapsWPF.Services.Settlements
{
    public static class SettlementUtmProjection
    {
        private const double EarthRadiusMeters = 6371000.0;
        private const double Wgs84SemiMajorAxis = 6378137.0;
        private const double Wgs84InverseFlattening = 298.257223563;
        private const double ScaleFactor = 0.9996;

        public static bool TryProject(
            double latitude,
            double longitude,
            int? requestedZone,
            out SettlementPoint point)
        {
            point = new SettlementPoint();

            if (double.IsNaN(latitude) ||
                double.IsInfinity(latitude) ||
                double.IsNaN(longitude) ||
                double.IsInfinity(longitude) ||
                latitude < -80.0 ||
                latitude > 84.0 ||
                longitude < -180.0 ||
                longitude > 180.0)
            {
                return false;
            }

            try
            {
                var zone = requestedZone.GetValueOrDefault(
                    (int)Math.Floor((longitude + 180.0) / 6.0) + 1);
                zone = Math.Clamp(zone, 1, 60);

                var flattening = 1.0 / Wgs84InverseFlattening;
                var eccentricitySquared = flattening * (2.0 - flattening);
                var secondEccentricitySquared =
                    eccentricitySquared / (1.0 - eccentricitySquared);

                var latitudeRadians = DegreesToRadians(latitude);
                var longitudeRadians = DegreesToRadians(longitude);
                var centralMeridian = DegreesToRadians(
                    (zone - 1) * 6.0 - 180.0 + 3.0);

                var sinLatitude = Math.Sin(latitudeRadians);
                var cosLatitude = Math.Cos(latitudeRadians);
                var tanLatitude = Math.Tan(latitudeRadians);

                var radius = Wgs84SemiMajorAxis /
                    Math.Sqrt(
                        1.0 - eccentricitySquared *
                        sinLatitude * sinLatitude);
                var t = tanLatitude * tanLatitude;
                var c = secondEccentricitySquared *
                    cosLatitude * cosLatitude;
                var a = cosLatitude *
                    (longitudeRadians - centralMeridian);

                var eccentricityFourth =
                    eccentricitySquared * eccentricitySquared;
                var eccentricitySixth =
                    eccentricityFourth * eccentricitySquared;
                var meridionalArc = Wgs84SemiMajorAxis *
                    ((1.0 - eccentricitySquared / 4.0 -
                      3.0 * eccentricityFourth / 64.0 -
                      5.0 * eccentricitySixth / 256.0) * latitudeRadians -
                     (3.0 * eccentricitySquared / 8.0 +
                      3.0 * eccentricityFourth / 32.0 +
                      45.0 * eccentricitySixth / 1024.0) *
                     Math.Sin(2.0 * latitudeRadians) +
                     (15.0 * eccentricityFourth / 256.0 +
                      45.0 * eccentricitySixth / 1024.0) *
                     Math.Sin(4.0 * latitudeRadians) -
                     35.0 * eccentricitySixth / 3072.0 *
                     Math.Sin(6.0 * latitudeRadians));

                var easting = ScaleFactor * radius *
                    (a +
                     (1.0 - t + c) * Math.Pow(a, 3) / 6.0 +
                     (5.0 - 18.0 * t + t * t + 72.0 * c -
                      58.0 * secondEccentricitySquared) *
                     Math.Pow(a, 5) / 120.0) +
                    500000.0;

                var northing = ScaleFactor *
                    (meridionalArc +
                     radius * tanLatitude *
                     (a * a / 2.0 +
                      (5.0 - t + 9.0 * c + 4.0 * c * c) *
                      Math.Pow(a, 4) / 24.0 +
                      (61.0 - 58.0 * t + t * t + 600.0 * c -
                       330.0 * secondEccentricitySquared) *
                      Math.Pow(a, 6) / 720.0));

                if (latitude < 0)
                {
                    northing += 10000000.0;
                }

                point = new SettlementPoint(
                    (float)easting,
                    (float)northing,
                    zone,
                    GetBand(latitude).ToString(),
                    latitude,
                    longitude);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryToLatLng(
            SettlementPoint? point,
            out double latitude,
            out double longitude)
        {
            latitude = double.NaN;
            longitude = double.NaN;

            if (point == null)
            {
                return false;
            }

            if (point.Latitude.HasValue && point.Longitude.HasValue)
            {
                latitude = point.Latitude.Value;
                longitude = point.Longitude.Value;
                return true;
            }

            if (point.UtmZone is < 1 or > 60)
            {
                return false;
            }

            try
            {
                var band = string.IsNullOrWhiteSpace(point.UtmBand)
                    ? "N"
                    : point.UtmBand.Trim().Substring(0, 1).ToUpperInvariant();
                var utm = new UniversalTransverseMercator(
                    band,
                    point.UtmZone,
                    point.X,
                    point.Y);
                var coordinate =
                    UniversalTransverseMercator.ConvertUTMtoLatLong(utm);

                latitude = coordinate.Latitude.DecimalDegree;
                longitude = coordinate.Longitude.DecimalDegree;
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static bool TryReproject(
            SettlementPoint? point,
            int targetZone,
            out PointF projected)
        {
            projected = PointF.Empty;

            if (point == null || targetZone is < 1 or > 60)
            {
                return false;
            }

            if (point.UtmZone == targetZone)
            {
                projected = new PointF(point.X, point.Y);
                return true;
            }

            if (!TryToLatLng(point, out var latitude, out var longitude) ||
                !TryProject(
                    latitude,
                    longitude,
                    targetZone,
                    out var converted))
            {
                return false;
            }

            projected = new PointF(converted.X, converted.Y);
            return true;
        }

        public static double GetDistanceMeters(
            SettlementPoint? first,
            SettlementPoint? second)
        {
            if (first == null || second == null)
            {
                return double.MaxValue;
            }

            if (first.UtmZone > 0 && first.UtmZone == second.UtmZone)
            {
                var dx = (double)first.X - second.X;
                var dy = (double)first.Y - second.Y;
                return Math.Sqrt(dx * dx + dy * dy);
            }

            if (!TryToLatLng(first, out var firstLat, out var firstLng) ||
                !TryToLatLng(second, out var secondLat, out var secondLng))
            {
                return double.MaxValue;
            }

            var firstLatRadians = DegreesToRadians(firstLat);
            var secondLatRadians = DegreesToRadians(secondLat);
            var deltaLat = DegreesToRadians(secondLat - firstLat);
            var deltaLng = DegreesToRadians(secondLng - firstLng);
            var haversine =
                Math.Sin(deltaLat / 2.0) * Math.Sin(deltaLat / 2.0) +
                Math.Cos(firstLatRadians) * Math.Cos(secondLatRadians) *
                Math.Sin(deltaLng / 2.0) * Math.Sin(deltaLng / 2.0);

            return EarthRadiusMeters * 2.0 *
                Math.Atan2(
                    Math.Sqrt(haversine),
                    Math.Sqrt(Math.Max(0.0, 1.0 - haversine)));
        }

        private static double DegreesToRadians(double value)
        {
            return value * Math.PI / 180.0;
        }

        private static char GetBand(double latitude)
        {
            const string bands = "CDEFGHJKLMNPQRSTUVWX";
            var index = (int)Math.Floor((latitude + 80.0) / 8.0);
            return bands[Math.Clamp(index, 0, bands.Length - 1)];
        }
    }
}
