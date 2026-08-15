using System;
using System.Collections.Generic;
using System.Drawing;
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

        public static IReadOnlyList<string> ReadPolygonLines(
            JToken? token,
            TryConvertLatLngToUtmDelegate tryConvert)
        {
            var result = new List<string>();

            if (token == null || tryConvert == null)
            {
                return result;
            }

            ReadToken(token, tryConvert, result);
            return result;
        }

        private static void ReadToken(
            JToken token,
            TryConvertLatLngToUtmDelegate tryConvert,
            List<string> result)
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
            List<string> result)
        {
            // The cache format currently stores outer rings only. Interior
            // rings (holes) are deliberately ignored instead of being drawn
            // as additional settlement polygons.
            var outerRing = polygonCoordinates?.First as JArray;

            if (outerRing == null)
            {
                return;
            }

            var points = new List<PointF>();

            foreach (var coordinateToken in outerRing)
            {
                if (coordinateToken is not JArray coordinate ||
                    coordinate.Count < 2 ||
                    !TryReadDouble(coordinate[0], out var lng) ||
                    !TryReadDouble(coordinate[1], out var lat) ||
                    !tryConvert(lat, lng, out var utm, out _, out _))
                {
                    continue;
                }

                points.Add(utm);
            }

            if (points.Count > 3 && AreSame(points[0], points[^1]))
            {
                points.RemoveAt(points.Count - 1);
            }

            if (points.Count < 3)
            {
                return;
            }

            var polygonLine = SettlementGeometryService.ToPolygonLine(points);

            if (!string.IsNullOrWhiteSpace(polygonLine))
            {
                result.Add(polygonLine);
            }
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
    }
}
