using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MapsWPF.Services.Settlements
{
    public static class SettlementGeometryMerger
    {
        private const double MaxNameMatchDistanceMeters = 15000.0;

        public static SettlementGeometryCache Merge(
            SettlementGeometryCache? existingCache,
            SettlementGeometryCache? incomingCache)
        {
            var merged = CloneCache(existingCache ?? new SettlementGeometryCache());
            var incoming = CloneCache(incomingCache ?? new SettlementGeometryCache());

            Normalize(merged);
            Normalize(incoming);

            if (incoming.Bounds != null)
            {
                merged.Bounds = CloneBounds(incoming.Bounds);
            }

            if (!string.IsNullOrWhiteSpace(incoming.CoordinateSystem))
            {
                merged.CoordinateSystem = incoming.CoordinateSystem;
            }

            if (incoming.UtmZone > 0)
            {
                merged.UtmZone = incoming.UtmZone;
            }

            if (!string.IsNullOrWhiteSpace(incoming.UtmBand))
            {
                merged.UtmBand = incoming.UtmBand;
            }

            AddSources(merged.Sources, incoming.Sources);

            foreach (var incomingSettlement in incoming.Settlements)
            {
                var target = FindMatchingSettlement(
                    merged.Settlements,
                    incomingSettlement);

                if (target == null)
                {
                    merged.Settlements.Add(CloneSettlement(incomingSettlement));
                    continue;
                }

                MergeSettlement(target, incomingSettlement);
            }

            merged.Source = merged.Sources.Count > 1
                ? "multi-provider"
                : merged.Sources.FirstOrDefault() ?? "local";

            merged.CreatedAt = DateTime.Now;

            return merged;
        }

        public static SettlementGeometryCache CloneCache(
            SettlementGeometryCache? cache)
        {
            if (cache == null)
            {
                return new SettlementGeometryCache();
            }

            return new SettlementGeometryCache
            {
                CreatedAt = cache.CreatedAt,
                Source = cache.Source ?? "local",
                Sources = cache.Sources?.ToList() ?? new List<string>(),
                CoordinateSystem = cache.CoordinateSystem ?? "UTM",
                UtmZone = cache.UtmZone,
                UtmBand = cache.UtmBand ?? string.Empty,
                Bounds = cache.Bounds == null
                    ? null
                    : CloneBounds(cache.Bounds),
                Settlements = cache.Settlements?
                    .Select(CloneSettlement)
                    .ToList() ?? new List<SettlementGeometryItem>()
            };
        }

        public static void Normalize(SettlementGeometryCache cache)
        {
            cache.Sources ??= new List<string>();
            cache.Settlements ??= new List<SettlementGeometryItem>();

            var cacheSource = SettlementDataSources.Normalize(cache.Source);

            if (cache.Settlements.Count > 0 ||
                !string.Equals(
                    cacheSource,
                    SettlementDataSources.LegacyCache,
                    StringComparison.OrdinalIgnoreCase))
            {
                AddSource(cache.Sources, cacheSource);
            }

            var itemDefaultSource = !string.IsNullOrWhiteSpace(cacheSource)
                ? cacheSource
                : cache.Sources.FirstOrDefault() ??
                  SettlementDataSources.LegacyCache;

            foreach (var settlement in cache.Settlements)
            {
                NormalizeSettlement(settlement, itemDefaultSource);

                foreach (var reference in settlement.ProviderReferences)
                {
                    AddSource(cache.Sources, reference.Provider);
                }

                foreach (var center in settlement.CenterCandidates)
                {
                    AddSource(cache.Sources, center.Provider);
                }

                foreach (var polygon in settlement.PolygonCandidates)
                {
                    AddSource(cache.Sources, polygon.Provider);
                }
            }

            cache.Source = cache.Sources.Count > 1
                ? "multi-provider"
                : cache.Sources.FirstOrDefault() ?? "local";
        }

        public static void AddProviderReference(
            SettlementGeometryItem settlement,
            string provider,
            string externalId,
            string featureType)
        {
            if (settlement == null ||
                string.IsNullOrWhiteSpace(provider) ||
                string.IsNullOrWhiteSpace(externalId))
            {
                return;
            }

            settlement.ProviderReferences ??= new List<SettlementProviderReference>();

            if (settlement.ProviderReferences.Any(x =>
                    string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.ExternalId, externalId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(x.FeatureType, featureType, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            settlement.ProviderReferences.Add(new SettlementProviderReference
            {
                Provider = provider,
                ExternalId = externalId,
                FeatureType = featureType ?? string.Empty
            });
        }

        public static void AddCenterCandidate(
            SettlementGeometryItem settlement,
            SettlementPoint point,
            string provider,
            string externalId,
            int priority)
        {
            if (settlement == null || point == null)
            {
                return;
            }

            settlement.CenterCandidates ??= new List<SettlementCenterCandidate>();

            var existing = settlement.CenterCandidates.FirstOrDefault(x =>
                string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.ExternalId, externalId, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                settlement.CenterCandidates.Add(new SettlementCenterCandidate
                {
                    Provider = provider,
                    ExternalId = externalId,
                    Priority = priority,
                    UpdatedAt = DateTime.Now,
                    Point = ClonePoint(point)
                });
            }
            else
            {
                existing.Point = ClonePoint(point);
                existing.Priority = priority;
                existing.QualityScore = 0;
                existing.UpdatedAt = DateTime.Now;
            }

            SelectPreferredCenter(settlement);
        }

        public static void AddPolygonCandidate(
            SettlementGeometryItem settlement,
            string polygon,
            string provider,
            string externalId,
            int priority)
        {
            if (settlement == null || string.IsNullOrWhiteSpace(polygon))
            {
                return;
            }

            settlement.Polygons ??= new List<string>();
            settlement.PolygonCandidates ??= new List<SettlementPolygonCandidate>();

            if (!settlement.Polygons.Contains(polygon, StringComparer.OrdinalIgnoreCase))
            {
                settlement.Polygons.Add(polygon);
            }

            var existing = settlement.PolygonCandidates.FirstOrDefault(x =>
                string.Equals(x.Polygon, polygon, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase));

            if (existing == null)
            {
                settlement.PolygonCandidates.Add(new SettlementPolygonCandidate
                {
                    Provider = provider,
                    ExternalId = externalId,
                    Priority = priority,
                    UpdatedAt = DateTime.Now,
                    Polygon = polygon
                });
            }
            else
            {
                existing.ExternalId = externalId;
                existing.Priority = priority;
                existing.QualityScore = 0;
                existing.UpdatedAt = DateTime.Now;
            }
        }

        public static IReadOnlyList<string> GetPreferredPolygonLines(
            SettlementGeometryItem settlement)
        {
            if (settlement == null)
            {
                return Array.Empty<string>();
            }

            settlement.Polygons ??= new List<string>();
            settlement.PolygonCandidates ??= new List<SettlementPolygonCandidate>();

            var validCandidates = settlement.PolygonCandidates
                .Where(x => !string.IsNullOrWhiteSpace(x.Polygon))
                .ToList();

            if (validCandidates.Count == 0)
            {
                return settlement.Polygons
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            var priority = validCandidates.Max(
                SettlementGeometryQualitySelector.GetEffectivePolygonScore);

            return validCandidates
                .Where(x =>
                    SettlementGeometryQualitySelector.GetEffectivePolygonScore(x) ==
                    priority)
                .Select(x => x.Polygon)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static bool HasUsablePolygons(SettlementGeometryItem? settlement)
        {
            return settlement != null &&
                   GetPreferredPolygonLines(settlement).Count > 0;
        }

        public static bool HasPolygonsFromProvider(
            SettlementGeometryItem? settlement,
            string provider)
        {
            return settlement?.PolygonCandidates != null &&
                   settlement.PolygonCandidates.Any(x =>
                       !string.IsNullOrWhiteSpace(x.Polygon) &&
                       string.Equals(
                           x.Provider,
                           provider,
                           StringComparison.OrdinalIgnoreCase));
        }

        private static void NormalizeSettlement(
            SettlementGeometryItem settlement,
            string cacheSource)
        {
            settlement.Name ??= string.Empty;
            settlement.Place ??= string.Empty;
            settlement.OsmType ??= string.Empty;
            settlement.GeometrySourceOsmType ??= string.Empty;
            settlement.Polygons ??= new List<string>();
            settlement.ProviderReferences ??= new List<SettlementProviderReference>();
            settlement.CenterCandidates ??= new List<SettlementCenterCandidate>();
            settlement.PolygonCandidates ??= new List<SettlementPolygonCandidate>();

            var inferredSource = settlement.OsmId > 0 ||
                                 settlement.GeometrySourceOsmId > 0
                ? SettlementDataSources.OpenStreetMapOverpass
                : cacheSource;

            if (settlement.OsmId > 0)
            {
                AddProviderReference(
                    settlement,
                    SettlementDataSources.OpenStreetMapOverpass,
                    settlement.OsmId.ToString(),
                    settlement.OsmType);
            }

            if (settlement.FallbackPoint != null &&
                settlement.CenterCandidates.Count == 0)
            {
                var externalId = settlement.OsmId > 0
                    ? settlement.OsmId.ToString()
                    : CreatePointExternalId(settlement.FallbackPoint);

                AddCenterCandidate(
                    settlement,
                    settlement.FallbackPoint,
                    inferredSource,
                    externalId,
                    SettlementDataSources.GetDefaultPriority(inferredSource));
            }

            var polygonProvider = settlement.GeometrySourceOsmId > 0 ||
                                  settlement.OsmId > 0
                ? SettlementDataSources.OpenStreetMapOverpass
                : inferredSource;

            var polygonExternalId = settlement.GeometrySourceOsmId > 0
                ? settlement.GeometrySourceOsmId.ToString()
                : settlement.OsmId > 0
                    ? settlement.OsmId.ToString()
                    : string.Empty;

            foreach (var polygon in settlement.Polygons.ToList())
            {
                if (string.IsNullOrWhiteSpace(polygon) ||
                    settlement.PolygonCandidates.Any(x =>
                        string.Equals(
                            x.Polygon,
                            polygon,
                            StringComparison.OrdinalIgnoreCase)))
                {
                    continue;
                }

                AddPolygonCandidate(
                    settlement,
                    polygon,
                    polygonProvider,
                    polygonExternalId,
                    SettlementDataSources.GetDefaultPriority(polygonProvider));
            }

            SettlementGeometryQualitySelector.Recalculate(settlement);
        }

        private static void MergeSettlement(
            SettlementGeometryItem target,
            SettlementGeometryItem incoming)
        {
            if (string.IsNullOrWhiteSpace(target.Name))
            {
                target.Name = incoming.Name;
            }

            if (string.IsNullOrWhiteSpace(target.Place))
            {
                target.Place = incoming.Place;
            }

            if (target.OsmId <= 0 && incoming.OsmId > 0)
            {
                target.OsmId = incoming.OsmId;
                target.OsmType = incoming.OsmType;
            }

            if (target.GeometrySourceOsmId <= 0 &&
                incoming.GeometrySourceOsmId > 0)
            {
                target.GeometrySourceOsmId = incoming.GeometrySourceOsmId;
                target.GeometrySourceOsmType = incoming.GeometrySourceOsmType;
            }

            foreach (var reference in incoming.ProviderReferences)
            {
                AddProviderReference(
                    target,
                    reference.Provider,
                    reference.ExternalId,
                    reference.FeatureType);
            }

            foreach (var center in incoming.CenterCandidates)
            {
                AddCenterCandidate(
                    target,
                    center.Point,
                    center.Provider,
                    center.ExternalId,
                    center.Priority);
            }

            if (incoming.FallbackPoint != null &&
                incoming.CenterCandidates.Count == 0)
            {
                AddCenterCandidate(
                    target,
                    incoming.FallbackPoint,
                    SettlementDataSources.LegacyCache,
                    CreatePointExternalId(incoming.FallbackPoint),
                    SettlementDataSources.LegacyPriority);
            }

            foreach (var polygon in incoming.PolygonCandidates)
            {
                AddPolygonCandidate(
                    target,
                    polygon.Polygon,
                    polygon.Provider,
                    polygon.ExternalId,
                    polygon.Priority);
            }

            foreach (var polygon in incoming.Polygons)
            {
                if (!target.Polygons.Contains(
                        polygon,
                        StringComparer.OrdinalIgnoreCase))
                {
                    target.Polygons.Add(polygon);
                }
            }

            SettlementGeometryQualitySelector.Recalculate(target);
        }

        public static SettlementGeometryItem CloneSettlementItem(
            SettlementGeometryItem source)
        {
            return CloneSettlement(source);
        }

        public static void MergeSettlementCandidates(
            SettlementGeometryItem target,
            SettlementGeometryItem incoming)
        {
            if (target == null || incoming == null)
            {
                return;
            }

            NormalizeSettlement(
                incoming,
                SettlementDataSources.LegacyCache);
            MergeSettlement(target, incoming);
        }

        private static SettlementGeometryItem? FindMatchingSettlement(
            List<SettlementGeometryItem> settlements,
            SettlementGeometryItem incoming)
        {
            foreach (var reference in incoming.ProviderReferences)
            {
                var exact = settlements.FirstOrDefault(candidate =>
                    candidate.ProviderReferences.Any(existingReference =>
                        string.Equals(
                            existingReference.Provider,
                            reference.Provider,
                            StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(
                            existingReference.ExternalId,
                            reference.ExternalId,
                            StringComparison.OrdinalIgnoreCase)));

                if (exact != null)
                {
                    return exact;
                }
            }

            if (incoming.OsmId > 0)
            {
                var exactOsm = settlements.FirstOrDefault(x =>
                    x.OsmId == incoming.OsmId &&
                    string.Equals(x.OsmType, incoming.OsmType, StringComparison.OrdinalIgnoreCase));

                if (exactOsm != null)
                {
                    return exactOsm;
                }
            }

            var nameKey = NormalizeName(incoming.Name);

            if (string.IsNullOrWhiteSpace(nameKey))
            {
                return null;
            }

            var candidates = settlements
                .Where(x => NormalizeName(x.Name) == nameKey)
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            var samePlaceCandidates = candidates
                .Where(x =>
                    !string.IsNullOrWhiteSpace(incoming.Place) &&
                    string.Equals(x.Place, incoming.Place, StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (samePlaceCandidates.Count > 0)
            {
                candidates = samePlaceCandidates;
            }

            if (incoming.FallbackPoint != null)
            {
                var closest = candidates
                    .Where(x => x.FallbackPoint != null)
                    .Select(x => new
                    {
                        Settlement = x,
                        Distance = GetDistance(
                            incoming.FallbackPoint,
                            x.FallbackPoint!)
                    })
                    .OrderBy(x => x.Distance)
                    .FirstOrDefault();

                if (closest != null &&
                    closest.Distance <= MaxNameMatchDistanceMeters)
                {
                    return closest.Settlement;
                }
            }

            return candidates.Count == 1
                ? candidates[0]
                : null;
        }

        private static void SelectPreferredCenter(SettlementGeometryItem settlement)
        {
            var preferred = settlement.CenterCandidates?
                .Where(x => x.Point != null)
                .OrderByDescending(x =>
                    x.QualityScore != 0
                        ? x.QualityScore
                        : x.Priority)
                .ThenByDescending(x => x.UpdatedAt)
                .FirstOrDefault();

            if (preferred != null)
            {
                settlement.FallbackPoint = ClonePoint(preferred.Point);
            }
        }

        private static void AddSources(
            List<string> target,
            IEnumerable<string>? sources)
        {
            if (sources == null)
            {
                return;
            }

            foreach (var source in sources)
            {
                AddSource(target, source);
            }
        }

        private static void AddSource(List<string> sources, string? source)
        {
            if (string.IsNullOrWhiteSpace(source) ||
                sources.Contains(source, StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            sources.Add(source);
        }

        private static SettlementGeometryItem CloneSettlement(
            SettlementGeometryItem source)
        {
            return new SettlementGeometryItem
            {
                Name = source.Name ?? string.Empty,
                Place = source.Place ?? string.Empty,
                OsmType = source.OsmType ?? string.Empty,
                OsmId = source.OsmId,
                GeometrySourceOsmType = source.GeometrySourceOsmType ?? string.Empty,
                GeometrySourceOsmId = source.GeometrySourceOsmId,
                Polygons = source.Polygons?.ToList() ?? new List<string>(),
                FallbackPoint = source.FallbackPoint == null
                    ? null
                    : ClonePoint(source.FallbackPoint),
                ProviderReferences = source.ProviderReferences?
                    .Select(x => new SettlementProviderReference
                    {
                        Provider = x.Provider ?? string.Empty,
                        ExternalId = x.ExternalId ?? string.Empty,
                        FeatureType = x.FeatureType ?? string.Empty
                    })
                    .ToList() ?? new List<SettlementProviderReference>(),
                CenterCandidates = source.CenterCandidates?
                    .Where(x => x.Point != null)
                    .Select(x => new SettlementCenterCandidate
                    {
                        Provider = x.Provider ?? string.Empty,
                        ExternalId = x.ExternalId ?? string.Empty,
                        Priority = x.Priority,
                        QualityScore = x.QualityScore,
                        UpdatedAt = x.UpdatedAt,
                        Point = ClonePoint(x.Point)
                    })
                    .ToList() ?? new List<SettlementCenterCandidate>(),
                PolygonCandidates = source.PolygonCandidates?
                    .Select(x => new SettlementPolygonCandidate
                    {
                        Provider = x.Provider ?? string.Empty,
                        ExternalId = x.ExternalId ?? string.Empty,
                        Priority = x.Priority,
                        QualityScore = x.QualityScore,
                        AreaSquareKilometers = x.AreaSquareKilometers,
                        VertexCount = x.VertexCount,
                        ContainsCenter = x.ContainsCenter,
                        UpdatedAt = x.UpdatedAt,
                        Polygon = x.Polygon ?? string.Empty
                    })
                    .ToList() ?? new List<SettlementPolygonCandidate>()
            };
        }

        private static SettlementCacheBounds CloneBounds(SettlementCacheBounds source)
        {
            return new SettlementCacheBounds
            {
                Top = source.Top,
                Bottom = source.Bottom,
                Left = source.Left,
                Right = source.Right,
                PaddingKm = source.PaddingKm
            };
        }

        private static SettlementPoint ClonePoint(SettlementPoint source)
        {
            return new SettlementPoint(source.X, source.Y);
        }

        private static string CreatePointExternalId(SettlementPoint point)
        {
            return $"{point.X:0.###},{point.Y:0.###}";
        }

        private static string NormalizeName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new string(
                value
                    .Trim()
                    .ToLowerInvariant()
                    .Where(char.IsLetterOrDigit)
                    .ToArray());
        }

        private static double GetDistance(
            SettlementPoint first,
            SettlementPoint second)
        {
            var dx = first.X - second.X;
            var dy = first.Y - second.Y;

            return Math.Sqrt(dx * dx + dy * dy);
        }
    }
}
