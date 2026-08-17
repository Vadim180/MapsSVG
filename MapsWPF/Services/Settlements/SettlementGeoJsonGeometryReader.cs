using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace MapsWPF.Services.Settlements
{
    public static class SettlementGeoJsonGeometryReader
    {
        public delegate bool TryConvertLatLngToUtmDelegate(
            double lat,
            double lng,
            out PointF utm,
            out int zone,
            out string band);

        public static IReadOnlyList<SettlementPolygonGeometry> ReadPolygons(
            JToken? token,
            TryConvertLatLngToUtmDelegate tryConvert)
        {
            var result = new List<SettlementPolygonGeometry>();

            if (token == null || tryConvert == null)
            {
                return result;
            }

            ReadToken(token, tryConvert, result);
            return result;
        }

        // Залишено для сумісності зі старими викликами й тестами.
        public static IReadOnlyList<string> ReadPolygonLines(
            JToken? token,
            TryConvertLatLngToUtmDelegate tryConvert)
        {
            return ReadPolygons(token, tryConvert)
                .Select(x => x.OuterRing)
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();
        }

        private static void ReadToken(
            JToken token,
            TryConvertLatLngToUtmDelegate tryConvert,
            List<SettlementPolygonGeometry> result)
        {
            var type = token["type"]?.Value<string>() ?? string.Empty;

            switch (type)
            {
                case "FeatureCollection":
                    foreach (var feature in token["features"] as JArray ?? new JArray())
                    {
                        ReadToken(feature, tryConvert, result);
                    }
                    break;

                case "Feature":
                    if (token["geometry"] != null)
                    {
                        ReadToken(token["geometry"]!, tryConvert, result);
                    }
                    break;

                case "GeometryCollection":
                    foreach (var geometry in token["geometries"] as JArray ?? new JArray())
                    {
                        ReadToken(geometry, tryConvert, result);
                    }
                    break;

                case "Polygon":
                    ReadPolygonCoordinates(
                        token["coordinates"] as JArray,
                        tryConvert,
                        result);
                    break;

                case "MultiPolygon":
                    foreach (var polygon in token["coordinates"] as JArray ?? new JArray())
                    {
                        ReadPolygonCoordinates(
                            polygon as JArray,
                            tryConvert,
                            result);
                    }
                    break;
            }
        }

        private static void ReadPolygonCoordinates(
            JArray? polygonCoordinates,
            TryConvertLatLngToUtmDelegate tryConvert,
            List<SettlementPolygonGeometry> result)
        {
            if (polygonCoordinates?.First is not JArray outerRingToken)
            {
                return;
            }

            var outerCoordinates = ReadCoordinates(outerRingToken);

            if (outerCoordinates.Count < 3)
            {
                return;
            }

            var first = outerCoordinates[0];

            if (!tryConvert(
                    first.Latitude,
                    first.Longitude,
                    out _,
                    out var zone,
                    out var band) ||
                zone is < 1 or > 60)
            {
                return;
            }

            var outerPoints = ProjectRing(outerCoordinates, zone);

            if (outerPoints.Count < 3)
            {
                return;
            }

            var geometry = new SettlementPolygonGeometry
            {
                OuterRing = SettlementGeometryService.ToPolygonLine(outerPoints),
                UtmZone = zone,
                UtmBand = band ?? string.Empty
            };

            // GeoJSON: перше кільце — зовнішнє, усі наступні — отвори.
            // Кожен отвір проєктується у ту саму UTM-зону, що й зовнішнє
            // кільце, тому контур не розривається на межі UTM-зон.
            foreach (var innerRingToken in polygonCoordinates.Skip(1).OfType<JArray>())
            {
                var innerCoordinates = ReadCoordinates(innerRingToken);
                var innerPoints = ProjectRing(innerCoordinates, zone);

                if (innerPoints.Count < 3)
                {
                    continue;
                }

                var line = SettlementGeometryService.ToPolygonLine(innerPoints);

                if (!string.IsNullOrWhiteSpace(line))
                {
                    geometry.InteriorRings.Add(line);
                }
            }

            if (!string.IsNullOrWhiteSpace(geometry.OuterRing))
            {
                result.Add(geometry);
            }
        }

        private static List<GeoCoordinate> ReadCoordinates(JArray ring)
        {
            var result = new List<GeoCoordinate>();

            foreach (var coordinateToken in ring)
            {
                if (coordinateToken is not JArray coordinate ||
                    coordinate.Count < 2 ||
                    !TryReadDouble(coordinate[0], out var longitude) ||
                    !TryReadDouble(coordinate[1], out var latitude))
                {
                    continue;
                }

                result.Add(new GeoCoordinate(latitude, longitude));
            }

            if (result.Count > 3 && AreSame(result[0], result[^1]))
            {
                result.RemoveAt(result.Count - 1);
            }

            return result;
        }

        private static List<PointF> ProjectRing(
            IEnumerable<GeoCoordinate> coordinates,
            int zone)
        {
            var result = new List<PointF>();

            foreach (var coordinate in coordinates)
            {
                if (!SettlementUtmProjection.TryProject(
                        coordinate.Latitude,
                        coordinate.Longitude,
                        zone,
                        out var projected))
                {
                    continue;
                }

                var point = new PointF(projected.X, projected.Y);

                if (result.Count == 0 || !AreSame(result[^1], point))
                {
                    result.Add(point);
                }
            }

            if (result.Count > 3 && AreSame(result[0], result[^1]))
            {
                result.RemoveAt(result.Count - 1);
            }

            return result;
        }

        private static bool TryReadDouble(JToken? token, out double value)
        {
            value = 0;

            if (token == null)
            {
                return false;
            }

            try
            {
                value = token.Value<double>();
                return !double.IsNaN(value) && !double.IsInfinity(value);
            }
            catch
            {
                return false;
            }
        }

        private static bool AreSame(PointF first, PointF second)
        {
            return Math.Abs(first.X - second.X) < 0.01f &&
                   Math.Abs(first.Y - second.Y) < 0.01f;
        }

        private static bool AreSame(GeoCoordinate first, GeoCoordinate second)
        {
            return Math.Abs(first.Latitude - second.Latitude) < 0.0000001 &&
                   Math.Abs(first.Longitude - second.Longitude) < 0.0000001;
        }

        private readonly struct GeoCoordinate
        {
            public GeoCoordinate(double latitude, double longitude)
            {
                Latitude = latitude;
                Longitude = longitude;
            }

            public double Latitude { get; }

            public double Longitude { get; }
        }
    }
}
