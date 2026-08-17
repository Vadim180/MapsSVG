using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MapsWPF.Services.Settlements
{
    public static class SettlementGeometryQualitySelector
    {
        private const int MinimumContainedOtherSettlementsToReject = 2;
        private const double GeometryEpsilon = 0.01;

        public static void Recalculate(SettlementGeometryItem? settlement)
        {
            Recalculate(settlement, null);
        }

        public static void Recalculate(
            SettlementGeometryItem? settlement,
            IEnumerable<SettlementGeometryItem>? allSettlements)
        {
            if (settlement == null)
            {
                return;
            }

            settlement.CenterCandidates ??= new List<SettlementCenterCandidate>();
            settlement.PolygonCandidates ??= new List<SettlementPolygonCandidate>();

            SelectPreferredCenter(settlement, useQualityScore: false);
            RecalculatePolygonScores(
                settlement,
                allSettlements ?? Array.Empty<SettlementGeometryItem>());
            RecalculateCenterScores(settlement);
            SelectPreferredCenter(settlement, useQualityScore: true);
        }

        public static void RecalculateAll(
            IEnumerable<SettlementGeometryItem>? settlements)
        {
            var list = settlements?
                .Where(x => x != null)
                .ToList() ?? new List<SettlementGeometryItem>();

            foreach (var settlement in list)
            {
                Recalculate(settlement, list);
            }
        }

        public static string GetPreferredPolygonProvider(
            SettlementGeometryItem? settlement)
        {
            return GetPreferredPolygonCandidates(settlement)
                .Select(x => x.Provider)
                .FirstOrDefault() ?? string.Empty;
        }

        public static IReadOnlyList<SettlementPolygonCandidate>
            GetPreferredPolygonCandidates(SettlementGeometryItem? settlement)
        {
            if (settlement?.PolygonCandidates == null)
            {
                return Array.Empty<SettlementPolygonCandidate>();
            }

            var group = settlement.PolygonCandidates
                .Where(IsUsablePolygonCandidate)
                .GroupBy(
                    x => $"{x.Provider}\n{x.ExternalId}",
                    StringComparer.OrdinalIgnoreCase)
                .Select(items => new
                {
                    Items = items.ToList(),
                    Score = items.Max(GetEffectivePolygonScore),
                    UpdatedAt = items.Max(x => x.UpdatedAt),
                    Priority = items.Max(x => x.Priority),
                    Key = items.Key
                })
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.UpdatedAt)
                .ThenByDescending(x => x.Priority)
                .ThenBy(x => x.Key, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault();

            return group == null
                ? Array.Empty<SettlementPolygonCandidate>()
                : group.Items;
        }

        public static bool IsUsablePolygonCandidate(
            SettlementPolygonCandidate? candidate)
        {
            return candidate != null &&
                   !string.IsNullOrWhiteSpace(candidate.Polygon) &&
                   !candidate.RejectedBySettlementContainmentGuard &&
                   !candidate.RejectedByGeometryGuard;
        }

        public static string GetPreferredRejectionReason(
            SettlementGeometryItem? settlement)
        {
            return settlement?.PolygonCandidates?
                       .Where(x =>
                           (x.RejectedBySettlementContainmentGuard ||
                            x.RejectedByGeometryGuard) &&
                           !string.IsNullOrWhiteSpace(x.RejectionReason))
                       .OrderByDescending(x => x.Priority)
                       .ThenByDescending(x => x.UpdatedAt)
                       .Select(x => x.RejectionReason)
                       .FirstOrDefault() ?? string.Empty;
        }

        public static int GetEffectivePolygonScore(
            SettlementPolygonCandidate candidate)
        {
            if (candidate.RejectedBySettlementContainmentGuard ||
                candidate.RejectedByGeometryGuard)
            {
                return int.MinValue;
            }

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

        public static bool IsPointInsideCandidate(
            SettlementPoint point,
            SettlementPolygonCandidate candidate)
        {
            var parsed = ParseCandidate(candidate);

            return parsed != null &&
                   TryProjectPoint(point, candidate.UtmZone, out var projected) &&
                   IsPointInsideGeometry(projected, parsed);
        }

        private static void RecalculatePolygonScores(
            SettlementGeometryItem settlement,
            IEnumerable<SettlementGeometryItem> allSettlements)
        {
            foreach (var candidate in settlement.PolygonCandidates)
            {
                candidate.QualityScore = 0;
                candidate.AreaSquareKilometers = 0;
                candidate.VertexCount = 0;
                candidate.ContainsCenter = false;
                candidate.CenterDistanceMeters = null;
                candidate.AcceptedByExplicitSettlementLink = false;
                candidate.ContainedSettlementCount = 0;
                candidate.RejectedBySettlementContainmentGuard = false;
                candidate.RejectedByGeometryGuard = false;
                candidate.RejectionReason = string.Empty;
                candidate.InteriorRings ??= new List<string>();
            }

            var groups = settlement.PolygonCandidates
                .Where(x => !string.IsNullOrWhiteSpace(x.Polygon))
                .GroupBy(
                    x => $"{x.Provider}\n{x.ExternalId}",
                    StringComparer.OrdinalIgnoreCase);

            foreach (var group in groups)
            {
                var groupCandidates = group.ToList();
                var parsed = groupCandidates
                    .Select(ParseCandidate)
                    .Where(x => x != null)
                    .Cast<ParsedCandidate>()
                    .ToList();

                if (parsed.Count != groupCandidates.Count)
                {
                    RejectGeometryGroup(
                        groupCandidates,
                        "Контур містить пошкоджене або незамкнене кільце");
                    continue;
                }

                var geometryError = parsed
                    .Select(GetGeometryValidationError)
                    .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

                if (!string.IsNullOrWhiteSpace(geometryError))
                {
                    RejectGeometryGroup(groupCandidates, geometryError);
                    continue;
                }

                var vertexCount = parsed.Sum(x =>
                    x.Outer.Count + x.Holes.Sum(hole => hole.Count));
                var areaSquareMeters = parsed.Sum(GetNetAreaSquareMeters);
                var areaSquareKilometers = areaSquareMeters / 1_000_000.0;
                var knownCenters = GetSettlementCenterPoints(settlement);
                var containsCenter = knownCenters.Any(centerPoint =>
                    parsed.Any(x =>
                        TryProjectPoint(
                            centerPoint,
                            x.Candidate.UtmZone,
                            out var center) &&
                        IsPointInsideGeometry(center, x)));
                var centerDistanceMeters = GetMinimumCenterDistanceMeters(
                    knownCenters,
                    parsed);
                var acceptedByExplicitSettlementLink =
                    !containsCenter &&
                    groupCandidates.Any(x => x.HasExplicitSettlementLink) &&
                    centerDistanceMeters.HasValue &&
                    centerDistanceMeters.Value <=
                    GetExplicitLinkCenterToleranceMeters(settlement.Place);
                var containedSettlementCount = CountOtherSettlementsInside(
                    settlement,
                    parsed,
                    allSettlements);
                var rejectedByContainmentGuard =
                    containedSettlementCount >=
                    MinimumContainedOtherSettlementsToReject;
                var geometryRejectionReasons = new List<string>();

                if (knownCenters.Count > 0 &&
                    !containsCenter &&
                    !acceptedByExplicitSettlementLink)
                {
                    geometryRejectionReasons.Add(
                        "Контур не містить центр власного НП");
                }

                var basePriority = group.Max(x => x.Priority);
                var score = basePriority;

                if (containsCenter)
                {
                    score += 35;
                }
                else if (acceptedByExplicitSettlementLink)
                {
                    score += 20;
                }
                else if (settlement.FallbackPoint != null)
                {
                    score -= 25;
                }

                score += Math.Min(
                    20,
                    (int)Math.Round(
                        Math.Log2(Math.Max(2, vertexCount)) * 2.0));

                var (minimumArea, maximumArea) =
                    GetExpectedAreaRange(settlement.Place);

                if (areaSquareKilometers < minimumArea)
                {
                    score -= 35;
                    geometryRejectionReasons.Add(
                        $"Площа контуру {areaSquareKilometers:0.###} км² " +
                        $"менша допустимої {minimumArea:0.###} км²");
                }
                else if (areaSquareKilometers > maximumArea)
                {
                    score -= 60;
                    geometryRejectionReasons.Add(
                        $"Площа контуру {areaSquareKilometers:0.###} км² " +
                        $"перевищує допустимі {maximumArea:0.###} км²");
                }
                else
                {
                    score += 10;
                }

                var compactness = parsed
                    .Select(GetCompactness)
                    .DefaultIfEmpty(0)
                    .Average();

                score += compactness >= 0.02 ? 5 : -10;

                if (rejectedByContainmentGuard)
                {
                    geometryRejectionReasons.Add(
                        $"Контур охоплює {containedSettlementCount} інших НП");
                }

                var rejectedByGeometryGuard = geometryRejectionReasons.Count >
                                               (rejectedByContainmentGuard ? 1 : 0);
                var rejected = rejectedByContainmentGuard ||
                               rejectedByGeometryGuard;
                var rejectionReason = string.Join(
                    "; ",
                    geometryRejectionReasons.Distinct(
                        StringComparer.OrdinalIgnoreCase));

                foreach (var item in groupCandidates)
                {
                    item.QualityScore = rejected
                        ? int.MinValue
                        : score;
                    item.AreaSquareKilometers = areaSquareKilometers;
                    item.VertexCount = vertexCount;
                    item.ContainsCenter = containsCenter;
                    item.CenterDistanceMeters = centerDistanceMeters;
                    item.AcceptedByExplicitSettlementLink =
                        acceptedByExplicitSettlementLink;
                    item.ContainedSettlementCount =
                        containedSettlementCount;
                    item.RejectedBySettlementContainmentGuard =
                        rejectedByContainmentGuard;
                    item.RejectedByGeometryGuard = rejectedByGeometryGuard;
                    item.RejectionReason = rejectionReason;
                }
            }
        }

        private static void RejectGeometryGroup(
            IEnumerable<SettlementPolygonCandidate> candidates,
            string reason)
        {
            foreach (var candidate in candidates)
            {
                candidate.QualityScore = int.MinValue;
                candidate.RejectedByGeometryGuard = true;
                candidate.RejectionReason = reason;
            }
        }

        private static void RecalculateCenterScores(
            SettlementGeometryItem settlement)
        {
            var polygons = GetPreferredPolygonCandidates(settlement)
                .Select(ParseCandidate)
                .Where(x => x != null)
                .Cast<ParsedCandidate>()
                .ToList();

            foreach (var candidate in settlement.CenterCandidates
                         .Where(x => x.Point != null))
            {
                var score = candidate.Priority;

                foreach (var other in settlement.CenterCandidates.Where(x =>
                             !ReferenceEquals(x, candidate) && x.Point != null))
                {
                    var distance = SettlementUtmProjection.GetDistanceMeters(
                        candidate.Point,
                        other.Point);

                    if (distance <= 3000.0)
                    {
                        score += 8;
                    }
                    else if (distance > 25000.0 &&
                             distance < double.MaxValue)
                    {
                        score -= 12;
                    }
                }

                if (polygons.Any(x =>
                        TryProjectPoint(
                            candidate.Point,
                            x.Candidate.UtmZone,
                            out var projected) &&
                        IsPointInsideGeometry(projected, x)))
                {
                    score += 30;
                }

                if (string.Equals(
                        settlement.OsmType,
                        "node",
                        StringComparison.OrdinalIgnoreCase) &&
                    settlement.OsmId > 0 &&
                    string.Equals(
                        candidate.Provider,
                        SettlementDataSources.OpenStreetMapOverpass,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        candidate.ExternalId,
                        settlement.OsmId.ToString(
                            System.Globalization.CultureInfo.InvariantCulture),
                        StringComparison.OrdinalIgnoreCase))
                {
                    // Реальний place=node точніший за bbox-center relation.
                    score += 20;
                }

                candidate.QualityScore = score;
            }
        }

        private static int CountOtherSettlementsInside(
            SettlementGeometryItem settlement,
            IReadOnlyList<ParsedCandidate> polygons,
            IEnumerable<SettlementGeometryItem> allSettlements)
        {
            var contained = new List<SettlementGeometryItem>();

            foreach (var other in allSettlements)
            {
                if (ReferenceEquals(other, settlement))
                {
                    continue;
                }

                var points = GetSettlementCenterPoints(other);

                if (points.Count == 0 ||
                    !points.Any(point =>
                        polygons.Any(x =>
                            TryProjectPoint(
                                point,
                                x.Candidate.UtmZone,
                                out var projected) &&
                            IsPointInsideGeometry(projected, x))))
                {
                    continue;
                }

                if (contained.Any(existing =>
                        IsSameSettlementInstance(existing, other)))
                {
                    continue;
                }

                contained.Add(other);
            }

            return contained.Count;
        }

        private static IReadOnlyList<SettlementPoint> GetSettlementCenterPoints(
            SettlementGeometryItem settlement)
        {
            var points = new List<SettlementPoint>();

            if (settlement.FallbackPoint != null)
            {
                points.Add(settlement.FallbackPoint);
            }

            foreach (var candidate in settlement.CenterCandidates ??
                     new List<SettlementCenterCandidate>())
            {
                if (candidate.Point == null ||
                    points.Any(existing =>
                        SettlementUtmProjection.GetDistanceMeters(
                            existing,
                            candidate.Point) <= 1.0))
                {
                    continue;
                }

                points.Add(candidate.Point);
            }

            return points;
        }

        private static SettlementPoint? GetSettlementCenterPoint(
            SettlementGeometryItem settlement)
        {
            if (settlement.FallbackPoint != null)
            {
                return settlement.FallbackPoint;
            }

            return settlement.CenterCandidates?
                .Where(x => x.Point != null)
                .OrderByDescending(x =>
                    x.QualityScore != 0 ? x.QualityScore : x.Priority)
                .ThenByDescending(x => x.UpdatedAt)
                .Select(x => x.Point)
                .FirstOrDefault();
        }

        private static bool IsSameSettlementInstance(
            SettlementGeometryItem first,
            SettlementGeometryItem second)
        {
            if (first.OsmId > 0 &&
                second.OsmId > 0 &&
                first.OsmId == second.OsmId &&
                string.Equals(
                    first.OsmType,
                    second.OsmType,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var sharedReference =
                (first.ProviderReferences ?? new List<SettlementProviderReference>())
                .Any(reference =>
                    (second.ProviderReferences ??
                     new List<SettlementProviderReference>())
                    .Any(otherReference =>
                        string.Equals(
                            reference.Provider,
                            otherReference.Provider,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            reference.ExternalId,
                            otherReference.ExternalId,
                            StringComparison.OrdinalIgnoreCase) &&
                        (!string.Equals(
                             reference.Provider,
                             SettlementDataSources.OpenStreetMapOverpass,
                             StringComparison.OrdinalIgnoreCase) ||
                         string.Equals(
                             reference.FeatureType,
                             otherReference.FeatureType,
                             StringComparison.OrdinalIgnoreCase))));

            if (sharedReference)
            {
                return true;
            }

            var firstPoint = GetSettlementCenterPoint(first);
            var secondPoint = GetSettlementCenterPoint(second);
            var distance = SettlementUtmProjection.GetDistanceMeters(
                firstPoint,
                secondPoint);

            // Однакова назва навіть поруч не доводить, що це той самий НП.
            // Просторове дедублювання лишаємо тільки для майже ідентичних
            // точок різних провайдерів; на більшій відстані це окремі НП.
            if (distance > 100.0)
            {
                return false;
            }

            var firstNames = GetNormalizedNames(first);
            var secondNames = GetNormalizedNames(second);

            return firstNames.Overlaps(secondNames) &&
                   AreCompatiblePlaces(first.Place, second.Place);
        }

        private static HashSet<string> GetNormalizedNames(
            SettlementGeometryItem settlement)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddNormalizedName(result, settlement.Name);

            foreach (var alias in settlement.NameAliases ?? new List<string>())
            {
                AddNormalizedName(result, alias);
            }

            return result;
        }

        private static void AddNormalizedName(
            HashSet<string> target,
            string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return;
            }

            var normalized = new string(
                value.Trim()
                    .ToLowerInvariant()
                    .Where(char.IsLetterOrDigit)
                    .ToArray());

            if (!string.IsNullOrWhiteSpace(normalized))
            {
                target.Add(normalized);
            }
        }

        private static bool AreCompatiblePlaces(string? first, string? second)
        {
            return string.IsNullOrWhiteSpace(first) ||
                   string.IsNullOrWhiteSpace(second) ||
                   string.Equals(
                       first,
                       second,
                       StringComparison.OrdinalIgnoreCase);
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
                    preferred.Point.Y,
                    preferred.Point.UtmZone,
                    preferred.Point.UtmBand,
                    preferred.Point.Latitude,
                    preferred.Point.Longitude);
            }
        }

        private static string GetGeometryValidationError(
            ParsedCandidate geometry)
        {
            var outer = NormalizeRing(geometry.Outer);

            if (!IsValidRing(outer))
            {
                return "Зовнішнє кільце контуру пошкоджене";
            }

            if (HasSelfIntersections(outer))
            {
                return "Зовнішнє кільце контуру має самоперетини";
            }

            var normalizedHoles = geometry.Holes
                .Select(NormalizeRing)
                .ToList();

            foreach (var hole in normalizedHoles)
            {
                if (!IsValidRing(hole))
                {
                    return "Внутрішнє кільце контуру пошкоджене";
                }

                if (HasSelfIntersections(hole))
                {
                    return "Внутрішнє кільце контуру має самоперетини";
                }

                if (!IsPointInsidePolygon(hole[0], outer))
                {
                    return "Внутрішнє кільце розташоване поза контуром";
                }

                if (RingsIntersect(outer, hole))
                {
                    return "Внутрішнє кільце перетинає зовнішню межу";
                }
            }

            for (var first = 0; first < normalizedHoles.Count; first++)
            {
                for (var second = first + 1;
                     second < normalizedHoles.Count;
                     second++)
                {
                    if (RingsIntersect(
                            normalizedHoles[first],
                            normalizedHoles[second]) ||
                        IsPointInsidePolygon(
                            normalizedHoles[first][0],
                            normalizedHoles[second]) ||
                        IsPointInsidePolygon(
                            normalizedHoles[second][0],
                            normalizedHoles[first]))
                    {
                        return "Внутрішні кільця контуру перетинаються";
                    }
                }
            }

            return string.Empty;
        }

        private static List<PointF> NormalizeRing(
            IReadOnlyList<PointF> source)
        {
            var result = new List<PointF>(source.Count);

            foreach (var point in source)
            {
                if (result.Count == 0 ||
                    !AreSamePoint(result[result.Count - 1], point))
                {
                    result.Add(point);
                }
            }

            if (result.Count > 1 &&
                AreSamePoint(result[0], result[result.Count - 1]))
            {
                result.RemoveAt(result.Count - 1);
            }

            return result;
        }

        private static bool IsValidRing(IReadOnlyList<PointF> ring)
        {
            return ring.Count >= 3 &&
                   ring.All(point =>
                       float.IsFinite(point.X) && float.IsFinite(point.Y)) &&
                   GetAreaSquareMeters(ring) >= 1.0;
        }

        private static bool HasSelfIntersections(IReadOnlyList<PointF> ring)
        {
            if (ring.Count < 4)
            {
                return false;
            }

            return HasSegmentIntersection(ring, null);
        }

        private static bool RingsIntersect(
            IReadOnlyList<PointF> first,
            IReadOnlyList<PointF> second)
        {
            return HasSegmentIntersection(first, second);
        }

        private static bool HasSegmentIntersection(
            IReadOnlyList<PointF> first,
            IReadOnlyList<PointF>? second)
        {
            var isSelfCheck = second == null;
            var segments = CreateSegments(first, ringIndex: 0);

            if (!isSelfCheck)
            {
                segments.AddRange(CreateSegments(second!, ringIndex: 1));
            }

            var ordered = segments
                .OrderBy(x => x.MinX)
                .ThenBy(x => x.MinY)
                .ToList();
            var active = new List<SegmentInfo>();

            foreach (var current in ordered)
            {
                active.RemoveAll(candidate =>
                    candidate.MaxX + GeometryEpsilon < current.MinX);

                foreach (var candidate in active)
                {
                    if (isSelfCheck)
                    {
                        if (AreAdjacentSegments(candidate, current))
                        {
                            continue;
                        }
                    }
                    else if (candidate.RingIndex == current.RingIndex)
                    {
                        continue;
                    }

                    if (candidate.MaxY + GeometryEpsilon < current.MinY ||
                        current.MaxY + GeometryEpsilon < candidate.MinY)
                    {
                        continue;
                    }

                    if (SegmentsIntersect(
                            candidate.Start,
                            candidate.End,
                            current.Start,
                            current.End))
                    {
                        return true;
                    }
                }

                active.Add(current);
            }

            return false;
        }

        private static List<SegmentInfo> CreateSegments(
            IReadOnlyList<PointF> ring,
            int ringIndex)
        {
            var result = new List<SegmentInfo>(ring.Count);

            for (var index = 0; index < ring.Count; index++)
            {
                result.Add(new SegmentInfo(
                    ring[index],
                    ring[(index + 1) % ring.Count],
                    ringIndex,
                    index,
                    ring.Count));
            }

            return result;
        }

        private static bool AreAdjacentSegments(
            SegmentInfo first,
            SegmentInfo second)
        {
            if (first.RingIndex != second.RingIndex)
            {
                return false;
            }

            var difference = Math.Abs(
                first.SegmentIndex - second.SegmentIndex);

            return difference == 1 ||
                   difference == first.RingVertexCount - 1;
        }

        private static bool SegmentsIntersect(
            PointF firstStart,
            PointF firstEnd,
            PointF secondStart,
            PointF secondEnd)
        {
            if (Math.Max(firstStart.X, firstEnd.X) + GeometryEpsilon <
                    Math.Min(secondStart.X, secondEnd.X) ||
                Math.Max(secondStart.X, secondEnd.X) + GeometryEpsilon <
                    Math.Min(firstStart.X, firstEnd.X) ||
                Math.Max(firstStart.Y, firstEnd.Y) + GeometryEpsilon <
                    Math.Min(secondStart.Y, secondEnd.Y) ||
                Math.Max(secondStart.Y, secondEnd.Y) + GeometryEpsilon <
                    Math.Min(firstStart.Y, firstEnd.Y))
            {
                return false;
            }

            var firstOrientation = Cross(firstStart, firstEnd, secondStart);
            var secondOrientation = Cross(firstStart, firstEnd, secondEnd);
            var thirdOrientation = Cross(secondStart, secondEnd, firstStart);
            var fourthOrientation = Cross(secondStart, secondEnd, firstEnd);

            if (((firstOrientation > GeometryEpsilon &&
                  secondOrientation < -GeometryEpsilon) ||
                 (firstOrientation < -GeometryEpsilon &&
                  secondOrientation > GeometryEpsilon)) &&
                ((thirdOrientation > GeometryEpsilon &&
                  fourthOrientation < -GeometryEpsilon) ||
                 (thirdOrientation < -GeometryEpsilon &&
                  fourthOrientation > GeometryEpsilon)))
            {
                return true;
            }

            return (Math.Abs(firstOrientation) <= GeometryEpsilon &&
                    IsPointOnSegment(secondStart, firstStart, firstEnd)) ||
                   (Math.Abs(secondOrientation) <= GeometryEpsilon &&
                    IsPointOnSegment(secondEnd, firstStart, firstEnd)) ||
                   (Math.Abs(thirdOrientation) <= GeometryEpsilon &&
                    IsPointOnSegment(firstStart, secondStart, secondEnd)) ||
                   (Math.Abs(fourthOrientation) <= GeometryEpsilon &&
                    IsPointOnSegment(firstEnd, secondStart, secondEnd));
        }

        private static double Cross(PointF first, PointF second, PointF third)
        {
            return ((double)second.X - first.X) * (third.Y - first.Y) -
                   ((double)second.Y - first.Y) * (third.X - first.X);
        }

        private static bool IsPointOnSegment(
            PointF point,
            PointF start,
            PointF end)
        {
            return point.X >= Math.Min(start.X, end.X) - GeometryEpsilon &&
                   point.X <= Math.Max(start.X, end.X) + GeometryEpsilon &&
                   point.Y >= Math.Min(start.Y, end.Y) - GeometryEpsilon &&
                   point.Y <= Math.Max(start.Y, end.Y) + GeometryEpsilon;
        }

        private static bool AreSamePoint(PointF first, PointF second)
        {
            return Math.Abs(first.X - second.X) <= GeometryEpsilon &&
                   Math.Abs(first.Y - second.Y) <= GeometryEpsilon;
        }

        private static ParsedCandidate? ParseCandidate(
            SettlementPolygonCandidate candidate)
        {
            if (!TryParseRing(candidate.Polygon, out var outer))
            {
                return null;
            }

            var holes = new List<List<PointF>>();

            foreach (var holeLine in candidate.InteriorRings ??
                     new List<string>())
            {
                if (string.IsNullOrWhiteSpace(holeLine))
                {
                    continue;
                }

                if (!TryParseRing(holeLine, out var hole))
                {
                    return null;
                }

                holes.Add(hole);
            }

            return new ParsedCandidate(candidate, outer, holes);
        }

        private static bool TryParseRing(
            string? polygonLine,
            out List<PointF> points)
        {
            points = new List<PointF>();

            if (string.IsNullOrWhiteSpace(polygonLine))
            {
                return false;
            }

            var pointParts = polygonLine.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries |
                StringSplitOptions.TrimEntries);

            foreach (var pointPart in pointParts)
            {
                var coordinates = pointPart.Split(
                    ',',
                    StringSplitOptions.TrimEntries);

                if (coordinates.Length != 2 ||
                    !float.TryParse(
                        coordinates[0],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var x) ||
                    !float.TryParse(
                        coordinates[1],
                        System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture,
                        out var y) ||
                    !float.IsFinite(x) ||
                    !float.IsFinite(y))
                {
                    points.Clear();
                    return false;
                }

                points.Add(new PointF(x, y));
            }

            return points.Count >= 3;
        }

        private static bool TryProjectPoint(
            SettlementPoint point,
            int targetZone,
            out PointF projected)
        {
            if (targetZone <= 0)
            {
                projected = new PointF(point.X, point.Y);
                return true;
            }

            return SettlementUtmProjection.TryReproject(
                point,
                targetZone,
                out projected);
        }

        private static bool IsPointInsideGeometry(
            PointF point,
            ParsedCandidate geometry)
        {
            return IsPointInsidePolygon(point, geometry.Outer) &&
                   !geometry.Holes.Any(hole =>
                       IsPointInsidePolygon(point, hole));
        }

        private static double GetNetAreaSquareMeters(ParsedCandidate geometry)
        {
            var outerArea = GetAreaSquareMeters(geometry.Outer);
            var holesArea = geometry.Holes.Sum(GetAreaSquareMeters);
            return Math.Max(0.0, outerArea - holesArea);
        }

        private static double? GetMinimumCenterDistanceMeters(
            IReadOnlyList<SettlementPoint> centers,
            IReadOnlyList<ParsedCandidate> geometries)
        {
            if (centers.Count == 0 || geometries.Count == 0)
            {
                return null;
            }

            var minimum = double.MaxValue;

            foreach (var centerPoint in centers)
            {
                foreach (var geometry in geometries)
                {
                    if (!TryProjectPoint(
                            centerPoint,
                            geometry.Candidate.UtmZone,
                            out var center))
                    {
                        continue;
                    }

                    if (IsPointInsideGeometry(center, geometry))
                    {
                        return 0.0;
                    }

                    minimum = Math.Min(
                        minimum,
                        GetDistanceToRing(center, geometry.Outer));

                    foreach (var hole in geometry.Holes)
                    {
                        minimum = Math.Min(
                            minimum,
                            GetDistanceToRing(center, hole));
                    }
                }
            }

            return minimum == double.MaxValue ? null : minimum;
        }

        private static double GetDistanceToRing(
            PointF point,
            IReadOnlyList<PointF> ring)
        {
            var minimum = double.MaxValue;

            for (var index = 0; index < ring.Count; index++)
            {
                minimum = Math.Min(
                    minimum,
                    GetDistanceToSegment(
                        point,
                        ring[index],
                        ring[(index + 1) % ring.Count]));
            }

            return minimum;
        }

        private static double GetDistanceToSegment(
            PointF point,
            PointF start,
            PointF end)
        {
            var deltaX = (double)end.X - start.X;
            var deltaY = (double)end.Y - start.Y;
            var lengthSquared = deltaX * deltaX + deltaY * deltaY;

            if (lengthSquared <= GeometryEpsilon * GeometryEpsilon)
            {
                return Math.Sqrt(
                    Math.Pow((double)point.X - start.X, 2) +
                    Math.Pow((double)point.Y - start.Y, 2));
            }

            var fraction = Math.Clamp(
                (((double)point.X - start.X) * deltaX +
                 ((double)point.Y - start.Y) * deltaY) /
                lengthSquared,
                0.0,
                1.0);
            var closestX = start.X + fraction * deltaX;
            var closestY = start.Y + fraction * deltaY;

            return Math.Sqrt(
                Math.Pow((double)point.X - closestX, 2) +
                Math.Pow((double)point.Y - closestY, 2));
        }

        private static double GetExplicitLinkCenterToleranceMeters(
            string? place)
        {
            return place?.Trim().ToLowerInvariant() switch
            {
                "city" => 2000.0,
                "town" => 1000.0,
                "village" => 500.0,
                "hamlet" => 300.0,
                "isolated_dwelling" => 200.0,
                _ => 500.0
            };
        }

        private static (double Minimum, double Maximum) GetExpectedAreaRange(
            string? place)
        {
            return place?.Trim().ToLowerInvariant() switch
            {
                "city" => (0.5, 5000.0),
                "town" => (0.1, 1000.0),
                "village" => (0.02, 250.0),
                "hamlet" => (0.005, 50.0),
                "isolated_dwelling" => (0.001, 10.0),
                _ => (0.005, 5000.0)
            };
        }

        private static double GetCompactness(ParsedCandidate geometry)
        {
            var width = (double)geometry.Outer.Max(x => x.X) -
                        geometry.Outer.Min(x => x.X);
            var height = (double)geometry.Outer.Max(x => x.Y) -
                         geometry.Outer.Min(x => x.Y);
            var boundingArea = Math.Abs(width * height);

            return boundingArea < 0.0001
                ? 0
                : GetNetAreaSquareMeters(geometry) / boundingArea;
        }

        private static double GetAreaSquareMeters(
            IReadOnlyList<PointF> polygon)
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

        private sealed class ParsedCandidate
        {
            public ParsedCandidate(
                SettlementPolygonCandidate candidate,
                List<PointF> outer,
                List<List<PointF>> holes)
            {
                Candidate = candidate;
                Outer = outer;
                Holes = holes;
            }

            public SettlementPolygonCandidate Candidate { get; }

            public List<PointF> Outer { get; }

            public List<List<PointF>> Holes { get; }
        }

        private readonly struct SegmentInfo
        {
            public SegmentInfo(
                PointF start,
                PointF end,
                int ringIndex,
                int segmentIndex,
                int ringVertexCount)
            {
                Start = start;
                End = end;
                RingIndex = ringIndex;
                SegmentIndex = segmentIndex;
                RingVertexCount = ringVertexCount;
                MinX = Math.Min(start.X, end.X);
                MaxX = Math.Max(start.X, end.X);
                MinY = Math.Min(start.Y, end.Y);
                MaxY = Math.Max(start.Y, end.Y);
            }

            public PointF Start { get; }

            public PointF End { get; }

            public int RingIndex { get; }

            public int SegmentIndex { get; }

            public int RingVertexCount { get; }

            public float MinX { get; }

            public float MaxX { get; }

            public float MinY { get; }

            public float MaxY { get; }
        }
    }
}
