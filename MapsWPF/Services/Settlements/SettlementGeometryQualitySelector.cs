using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MapsWPF.Services.Settlements
{
    public static class SettlementGeometryQualitySelector
    {
        public static void Recalculate(SettlementGeometryItem? settlement)
        {
            if (settlement == null)
            {
                return;
            }

            settlement.CenterCandidates ??= new List<SettlementCenterCandidate>();
            settlement.PolygonCandidates ??= new List<SettlementPolygonCandidate>();

            SelectPreferredCenter(settlement, useQualityScore: false);
            RecalculatePolygonScores(settlement);
            RecalculateCenterScores(settlement);
            SelectPreferredCenter(settlement, useQualityScore: true);
        }

        public static string GetPreferredPolygonProvider(
            SettlementGeometryItem? settlement)
        {
            if (settlement?.PolygonCandidates == null)
            {
                return string.Empty;
            }

            return settlement.PolygonCandidates
                .Where(x => !string.IsNullOrWhiteSpace(x.Polygon))
                .OrderByDescending(GetEffectivePolygonScore)
                .ThenByDescending(x => x.UpdatedAt)
                .Select(x => x.Provider)
                .FirstOrDefault() ?? string.Empty;
        }

        public static int GetEffectivePolygonScore(
            SettlementPolygonCandidate candidate)
        {
            return candidate.QualityScore != 0
                ? candidate.QualityScore
                : candidate.Priority;
        }

        public static double GetAreaSquareKilometers(
            IEnumerable<string> polygonLines)
        {
            return polygonLines
                .Select(SettlementGeometryService.ParsePolygonLine)
                .Where(x => x.Count >= 3)
                .Sum(GetAreaSquareMeters) /
                1_000_000.0;
        }

        private static void RecalculatePolygonScores(
            SettlementGeometryItem settlement)
        {
            var center = settlement.FallbackPoint == null
                ? (PointF?)null
                : new PointF(
                    settlement.FallbackPoint.X,
                    settlement.FallbackPoint.Y);

            var groups = settlement.PolygonCandidates
                .Where(x => !string.IsNullOrWhiteSpace(x.Polygon))
                .GroupBy(
                    x => $"{x.Provider}\n{x.ExternalId}",
                    StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                var parsed = group
                    .Select(x => new
                    {
                        Candidate = x,
                        Points = SettlementGeometryService.ParsePolygonLine(x.Polygon)
                    })
                    .Where(x => x.Points.Count >= 3)
                    .ToList();

                var vertexCount = parsed.Sum(x => x.Points.Count);
                var areaSquareMeters = parsed.Sum(x => GetAreaSquareMeters(x.Points));
                var areaSquareKilometers = areaSquareMeters / 1_000_000.0;
                var containsCenter = center.HasValue &&
                    parsed.Any(x => IsPointInsidePolygon(center.Value, x.Points));

                var basePriority = group.Max(x => x.Priority);
                var score = basePriority;

                if (containsCenter)
                {
                    score += 35;
                }
                else if (center.HasValue)
                {
                    score -= 25;
                }

                score += Math.Min(
                    20,
                    (int)Math.Round(Math.Log2(Math.Max(2, vertexCount)) * 2.0));

                var (minimumArea, maximumArea) = GetExpectedAreaRange(settlement.Place);

                if (areaSquareKilometers < minimumArea)
                {
                    score -= 35;
                }
                else if (areaSquareKilometers > maximumArea)
                {
                    score -= 60;
                }
                else
                {
                    score += 10;
                }

                var compactness = GetCompactness(parsed.Select(x => x.Points));

                if (compactness >= 0.02)
                {
                    score += 5;
                }
                else
                {
                    score -= 10;
                }

                foreach (var item in parsed)
                {
                    item.Candidate.QualityScore = score;
                    item.Candidate.AreaSquareKilometers = areaSquareKilometers;
                    item.Candidate.VertexCount = vertexCount;
                    item.Candidate.ContainsCenter = containsCenter;
                }
            }
        }

        private static void RecalculateCenterScores(
            SettlementGeometryItem settlement)
        {
            var polygonLines = GetBestPolygonGroup(settlement);

            foreach (var candidate in settlement.CenterCandidates.Where(x => x.Point != null))
            {
                var point = new PointF(candidate.Point.X, candidate.Point.Y);
                var score = candidate.Priority;

                foreach (var other in settlement.CenterCandidates.Where(x =>
                             !ReferenceEquals(x, candidate) && x.Point != null))
                {
                    var distance = GetDistanceMeters(candidate.Point, other.Point);

                    if (distance <= 3000.0)
                    {
                        score += 8;
                    }
                    else if (distance > 25000.0)
                    {
                        score -= 12;
                    }
                }

                if (polygonLines.Any(line =>
                        IsPointInsidePolygon(
                            point,
                            SettlementGeometryService.ParsePolygonLine(line))))
                {
                    score += 30;
                }

                candidate.QualityScore = score;
            }
        }

        private static IReadOnlyList<string> GetBestPolygonGroup(
            SettlementGeometryItem settlement)
        {
            var valid = settlement.PolygonCandidates
                .Where(x => !string.IsNullOrWhiteSpace(x.Polygon))
                .ToList();

            if (valid.Count == 0)
            {
                return Array.Empty<string>();
            }

            var score = valid.Max(GetEffectivePolygonScore);

            return valid
                .Where(x => GetEffectivePolygonScore(x) == score)
                .Select(x => x.Polygon)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void SelectPreferredCenter(
            SettlementGeometryItem settlement,
            bool useQualityScore)
        {
            var preferred = settlement.CenterCandidates
                .Where(x => x.Point != null)
                .OrderByDescending(x =>
                    useQualityScore && x.QualityScore != 0
                        ? x.QualityScore
                        : x.Priority)
                .ThenByDescending(x => x.UpdatedAt)
                .FirstOrDefault();

            if (preferred != null)
            {
                settlement.FallbackPoint = new SettlementPoint(
                    preferred.Point.X,
                    preferred.Point.Y);
            }
        }

        private static (double Minimum, double Maximum) GetExpectedAreaRange(
            string? place)
        {
            return place?.Trim().ToLowerInvariant() switch
            {
                "city" => (0.5, 10000.0),
                "town" => (0.1, 3000.0),
                "village" => (0.02, 1000.0),
                "hamlet" => (0.005, 300.0),
                "isolated_dwelling" => (0.001, 100.0),
                _ => (0.005, 10000.0)
            };
        }

        private static double GetCompactness(
            IEnumerable<List<PointF>> polygons)
        {
            var points = polygons.SelectMany(x => x).ToList();

            if (points.Count < 3)
            {
                return 0;
            }

            var width = (double)points.Max(x => x.X) - points.Min(x => x.X);
            var height = (double)points.Max(x => x.Y) - points.Min(x => x.Y);
            var boundingArea = Math.Abs(width * height);

            if (boundingArea < 0.0001)
            {
                return 0;
            }

            var polygonArea = polygons.Sum(GetAreaSquareMeters);
            return polygonArea / boundingArea;
        }

        private static double GetAreaSquareMeters(IReadOnlyList<PointF> polygon)
        {
            if (polygon.Count < 3)
            {
                return 0;
            }

            double area = 0;
            var originX = (double)polygon[0].X;
            var originY = (double)polygon[0].Y;

            for (var i = 0; i < polygon.Count; i++)
            {
                var first = polygon[i];
                var second = polygon[(i + 1) % polygon.Count];
                var firstX = (double)first.X - originX;
                var firstY = (double)first.Y - originY;
                var secondX = (double)second.X - originX;
                var secondY = (double)second.Y - originY;
                area += firstX * secondY - secondX * firstY;
            }

            return Math.Abs(area / 2.0);
        }

        private static bool IsPointInsidePolygon(
            PointF point,
            IReadOnlyList<PointF> polygon)
        {
            if (polygon.Count < 3)
            {
                return false;
            }

            var inside = false;

            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var current = polygon[i];
                var previous = polygon[j];
                var intersects =
                    (current.Y > point.Y) != (previous.Y > point.Y) &&
                    point.X <
                    (previous.X - current.X) *
                    (point.Y - current.Y) /
                    ((previous.Y - current.Y) + 0.000001f) +
                    current.X;

                if (intersects)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static double GetDistanceMeters(
            SettlementPoint first,
            SettlementPoint second)
        {
            var dx = first.X - second.X;
            var dy = first.Y - second.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
