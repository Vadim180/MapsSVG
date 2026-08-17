using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace MapsWPF.Services.Settlements
{
    public static class SettlementGeometryMerger
    {
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

            SettlementGeometryQualitySelector.RecalculateAll(
                merged.Settlements);

            merged.CreatedAt = DateTime.Now;

            return merged;
        }

        public static SettlementGeometryCache MergeReplacingProviders(
            SettlementGeometryCache? existingCache,
            SettlementGeometryCache? incomingCache,
            IEnumerable<string>? providersToReplace)
        {
            var merged = CloneCache(existingCache ?? new SettlementGeometryCache());
            var incoming = CloneCache(incomingCache ?? new SettlementGeometryCache());
            var providers = new HashSet<string>(
                providersToReplace ?? Array.Empty<string>(),
                StringComparer.OrdinalIgnoreCase);

            Normalize(merged);
            Normalize(incoming);

            if (incoming.Bounds != null)
            {
                merged.Bounds = CloneBounds(incoming.Bounds);
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

                foreach (var provider in providers)
                {
                    RemoveProviderCandidates(
                        target,
                        provider,
                        removeCenters: true,
                        removePolygons: true);
                }

                MergeSettlement(target, incomingSettlement);
            }

            SettlementGeometryQualitySelector.RecalculateAll(
                merged.Settlements);
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
                SchemaVersion = cache.SchemaVersion,
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
            cache.SchemaVersion = SettlementGeometryCache.CurrentSchemaVersion;
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
                NormalizeSettlement(
                    settlement,
                    itemDefaultSource,
                    cache.UtmZone,
                    cache.UtmBand);

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

            SettlementGeometryQualitySelector.RecalculateAll(
                cache.Settlements);

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
            int priority,
            int utmZone = 0,
            string? utmBand = null,
            IEnumerable<string>? interiorRings = null,
            string? boundary = null,
            string? adminLevel = null,
            bool hasExplicitSettlementLink = false)
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
                    Polygon = polygon,
                    UtmZone = utmZone,
                    UtmBand = utmBand ?? string.Empty,
                    InteriorRings = interiorRings?
                        .Where(x => !string.IsNullOrWhiteSpace(x))
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .ToList() ?? new List<string>(),
                    Boundary = boundary ?? string.Empty,
                    AdminLevel = adminLevel ?? string.Empty,
                    HasExplicitSettlementLink = hasExplicitSettlementLink
                });
            }
            else
            {
                existing.ExternalId = externalId;
                existing.Priority = priority;
                existing.QualityScore = 0;
                existing.ContainedSettlementCount = 0;
                existing.RejectedBySettlementContainmentGuard = false;
                existing.RejectedByGeometryGuard = false;
                existing.RejectionReason = string.Empty;
                existing.HasExplicitSettlementLink =
                    existing.HasExplicitSettlementLink ||
                    hasExplicitSettlementLink;
                existing.CenterDistanceMeters = null;
                existing.AcceptedByExplicitSettlementLink = false;
                existing.UtmZone = utmZone > 0
                    ? utmZone
                    : existing.UtmZone;
                existing.UtmBand = !string.IsNullOrWhiteSpace(utmBand)
                    ? utmBand
                    : existing.UtmBand;
                existing.InteriorRings = interiorRings?
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList() ?? existing.InteriorRings ?? new List<string>();
                existing.Boundary = !string.IsNullOrWhiteSpace(boundary)
                    ? boundary
                    : existing.Boundary;
                existing.AdminLevel = !string.IsNullOrWhiteSpace(adminLevel)
                    ? adminLevel
                    : existing.AdminLevel;
                existing.UpdatedAt = DateTime.Now;
            }
        }

        public static void AddPolygonCandidate(
            SettlementGeometryItem settlement,
            SettlementPolygonGeometry geometry,
            string provider,
            string externalId,
            int priority,
            string? boundary = null,
            string? adminLevel = null,
            bool hasExplicitSettlementLink = false)
        {
            if (geometry == null)
            {
                return;
            }

            AddPolygonCandidate(
                settlement,
                geometry.OuterRing,
                provider,
                externalId,
                priority,
                geometry.UtmZone,
                geometry.UtmBand,
                geometry.InteriorRings,
                boundary,
                adminLevel,
                hasExplicitSettlementLink);
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
                .Where(SettlementGeometryQualitySelector.IsUsablePolygonCandidate)
                .ToList();

            if (validCandidates.Count == 0)
            {
                // Якщо metadata кандидатів уже є, але всі вони відкинуті
                // guard-ом, не повертаємо старий сирий список Polygons.
                if (settlement.PolygonCandidates.Count > 0)
                {
                    return Array.Empty<string>();
                }

                return settlement.Polygons
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            return SettlementGeometryQualitySelector
                .GetPreferredPolygonCandidates(settlement)
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
                        SettlementGeometryQualitySelector.IsUsablePolygonCandidate(x) &&
                       string.Equals(
                           x.Provider,
                           provider,
                           StringComparison.OrdinalIgnoreCase));
        }

        public static void RemoveProviderCandidates(
            SettlementGeometryItem settlement,
            string provider,
            bool removeCenters,
            bool removePolygons)
        {
            if (settlement == null || string.IsNullOrWhiteSpace(provider))
            {
                return;
            }

            settlement.CenterCandidates ??= new List<SettlementCenterCandidate>();
            settlement.PolygonCandidates ??= new List<SettlementPolygonCandidate>();
            settlement.ProviderReferences ??= new List<SettlementProviderReference>();

            if (removeCenters)
            {
                settlement.CenterCandidates.RemoveAll(x =>
                    string.Equals(
                        x.Provider,
                        provider,
                        StringComparison.OrdinalIgnoreCase));
            }

            if (removePolygons)
            {
                settlement.PolygonCandidates.RemoveAll(x =>
                    string.Equals(
                        x.Provider,
                        provider,
                        StringComparison.OrdinalIgnoreCase));
                settlement.Polygons = settlement.PolygonCandidates
                    .Select(x => x.Polygon)
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            settlement.ProviderReferences.RemoveAll(x =>
                string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase) &&
                ((removeCenters && string.Equals(
                      x.FeatureType,
                      "center",
                      StringComparison.OrdinalIgnoreCase)) ||
                 (removePolygons &&
                  (string.Equals(
                       x.FeatureType,
                       "polygon",
                       StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(
                       x.FeatureType,
                       "relation",
                       StringComparison.OrdinalIgnoreCase)))));

            SelectPreferredCenter(settlement);

            if (settlement.CenterCandidates.Count == 0)
            {
                settlement.FallbackPoint = null;
            }
        }

        public static void MergeSettlementCandidatesReplacingProviders(
            SettlementGeometryItem target,
            SettlementGeometryItem incoming,
            IEnumerable<string>? refreshedCenterProviders,
            IEnumerable<string>? refreshedPolygonProviders)
        {
            foreach (var provider in refreshedCenterProviders ?? Array.Empty<string>())
            {
                RemoveProviderCandidates(
                    target,
                    provider,
                    removeCenters: true,
                    removePolygons: false);
            }

            foreach (var provider in refreshedPolygonProviders ?? Array.Empty<string>())
            {
                RemoveProviderCandidates(
                    target,
                    provider,
                    removeCenters: false,
                    removePolygons: true);
            }

            MergeSettlementCandidates(target, incoming);
        }

        private static void NormalizeSettlement(
            SettlementGeometryItem settlement,
            string cacheSource,
            int legacyUtmZone = 0,
            string? legacyUtmBand = null)
        {
            settlement.Name ??= string.Empty;
            settlement.NameAliases ??= new List<string>();
            settlement.Place ??= string.Empty;
            settlement.Boundary ??= string.Empty;
            settlement.AdminLevel ??= string.Empty;
            settlement.CountryCode ??= string.Empty;
            settlement.Region ??= string.Empty;
            settlement.District ??= string.Empty;
            settlement.OsmType ??= string.Empty;
            settlement.GeometrySourceOsmType ??= string.Empty;
            settlement.Polygons ??= new List<string>();
            settlement.ProviderReferences ??= new List<SettlementProviderReference>();
            settlement.CenterCandidates ??= new List<SettlementCenterCandidate>();
            settlement.PolygonCandidates ??= new List<SettlementPolygonCandidate>();

            AddAlias(settlement.NameAliases, settlement.Name);

            if (settlement.FallbackPoint != null)
            {
                EnsurePointCoordinateMetadata(
                    settlement.FallbackPoint,
                    legacyUtmZone,
                    legacyUtmBand);
            }

            foreach (var center in settlement.CenterCandidates)
            {
                center.Point ??= new SettlementPoint();
                EnsurePointCoordinateMetadata(
                    center.Point,
                    legacyUtmZone,
                    legacyUtmBand);
            }

            foreach (var candidate in settlement.PolygonCandidates)
            {
                candidate.InteriorRings ??= new List<string>();
                candidate.UtmBand ??= string.Empty;
                candidate.Boundary ??= string.Empty;
                candidate.AdminLevel ??= string.Empty;
                candidate.RejectionReason ??= string.Empty;

                if (candidate.UtmZone <= 0)
                {
                    candidate.UtmZone = legacyUtmZone;
                }

                if (string.IsNullOrWhiteSpace(candidate.UtmBand))
                {
                    candidate.UtmBand = legacyUtmBand ?? string.Empty;
                }
            }

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
                    SettlementDataSources.GetDefaultPriority(polygonProvider),
                    legacyUtmZone,
                    legacyUtmBand,
                    null,
                    settlement.Boundary,
                    settlement.AdminLevel);
            }

            SettlementGeometryQualitySelector.Recalculate(settlement);
        }

        private static void MergeSettlement(
            SettlementGeometryItem target,
            SettlementGeometryItem incoming)
        {
            MergeAliases(target.NameAliases, incoming.NameAliases);
            AddAlias(target.NameAliases, target.Name);
            AddAlias(target.NameAliases, incoming.Name);

            if (string.IsNullOrWhiteSpace(target.Name))
            {
                target.Name = incoming.Name;
            }

            if (string.IsNullOrWhiteSpace(target.Place))
            {
                target.Place = incoming.Place;
            }

            target.Boundary = PreferExisting(target.Boundary, incoming.Boundary);
            target.AdminLevel = PreferExisting(target.AdminLevel, incoming.AdminLevel);
            target.CountryCode = PreferExisting(target.CountryCode, incoming.CountryCode);
            target.Region = PreferExisting(target.Region, incoming.Region);
            target.District = PreferExisting(target.District, incoming.District);

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
                    polygon.Priority,
                    polygon.UtmZone,
                    polygon.UtmBand,
                    polygon.InteriorRings,
                    polygon.Boundary,
                    polygon.AdminLevel,
                    polygon.HasExplicitSettlementLink);
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
                        AreSameProviderEntity(
                            existingReference,
                            reference)));

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

            var incomingNames = GetNormalizedNames(incoming);

            if (incomingNames.Count == 0)
            {
                return null;
            }

            var candidates = settlements
                .Where(x => GetNormalizedNames(x).Overlaps(incomingNames))
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
                var exactContextCandidates = candidates
                    .Where(x => HasSameAdministrativeContext(x, incoming))
                    .ToList();

                if (exactContextCandidates.Count == 1)
                {
                    candidates = exactContextCandidates;
                }

                var ranked = candidates
                    .Where(x => x.FallbackPoint != null)
                    .Select(x => new
                    {
                        Settlement = x,
                        Distance = GetDistance(
                            incoming.FallbackPoint,
                            x.FallbackPoint!)
                    })
                    .OrderBy(x => x.Distance)
                    .ToList();
                var closest = ranked.FirstOrDefault();

                if (closest != null &&
                    closest.Distance <= GetMaximumIdentityDistanceMeters(
                        incoming.Place) &&
                    IsNearestMatchUnambiguous(ranked.Select(x => x.Distance)))
                {
                    return closest.Settlement;
                }

                return null;
            }

            // Без координат назви недостатньо: дві сусідні Ковалівки мають
            // залишатися окремими записами. Дозволяємо name-only merge лише
            // коли повністю збігається адміністративний контекст.
            var contextMatches = candidates
                .Where(x => HasSameAdministrativeContext(x, incoming))
                .ToList();

            return contextMatches.Count == 1
                ? contextMatches[0]
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
                NameAliases = source.NameAliases?.ToList() ?? new List<string>(),
                Place = source.Place ?? string.Empty,
                Boundary = source.Boundary ?? string.Empty,
                AdminLevel = source.AdminLevel ?? string.Empty,
                CountryCode = source.CountryCode ?? string.Empty,
                Region = source.Region ?? string.Empty,
                District = source.District ?? string.Empty,
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
                        HasExplicitSettlementLink =
                            x.HasExplicitSettlementLink,
                        CenterDistanceMeters = x.CenterDistanceMeters,
                        AcceptedByExplicitSettlementLink =
                            x.AcceptedByExplicitSettlementLink,
                        ContainedSettlementCount = x.ContainedSettlementCount,
                        RejectedBySettlementContainmentGuard =
                            x.RejectedBySettlementContainmentGuard,
                        RejectedByGeometryGuard = x.RejectedByGeometryGuard,
                        RejectionReason = x.RejectionReason ?? string.Empty,
                        UtmZone = x.UtmZone,
                        UtmBand = x.UtmBand ?? string.Empty,
                        InteriorRings = x.InteriorRings?.ToList() ?? new List<string>(),
                        Boundary = x.Boundary ?? string.Empty,
                        AdminLevel = x.AdminLevel ?? string.Empty,
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
            return new SettlementPoint(
                source.X,
                source.Y,
                source.UtmZone,
                source.UtmBand,
                source.Latitude,
                source.Longitude);
        }

        private static string CreatePointExternalId(SettlementPoint point)
        {
            return $"{point.UtmZone}{point.UtmBand}:" +
                   $"{point.X:0.###},{point.Y:0.###}";
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
            return SettlementUtmProjection.GetDistanceMeters(first, second);
        }

        private static void EnsurePointCoordinateMetadata(
            SettlementPoint point,
            int legacyUtmZone,
            string? legacyUtmBand)
        {
            if (point.UtmZone <= 0)
            {
                point.UtmZone = legacyUtmZone;
            }

            if (string.IsNullOrWhiteSpace(point.UtmBand))
            {
                point.UtmBand = legacyUtmBand ?? string.Empty;
            }

            if ((!point.Latitude.HasValue || !point.Longitude.HasValue) &&
                SettlementUtmProjection.TryToLatLng(
                    point,
                    out var latitude,
                    out var longitude))
            {
                point.Latitude = latitude;
                point.Longitude = longitude;
            }
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
            var normalized = NormalizeName(value);

            if (!string.IsNullOrWhiteSpace(normalized))
            {
                target.Add(normalized);
            }
        }

        private static void MergeAliases(
            List<string> target,
            IEnumerable<string>? aliases)
        {
            if (aliases == null)
            {
                return;
            }

            foreach (var alias in aliases)
            {
                AddAlias(target, alias);
            }
        }

        private static void AddAlias(List<string> target, string? alias)
        {
            if (string.IsNullOrWhiteSpace(alias) ||
                target.Contains(alias.Trim(), StringComparer.OrdinalIgnoreCase))
            {
                return;
            }

            target.Add(alias.Trim());
        }

        private static double GetMaximumIdentityDistanceMeters(string? place)
        {
            return place?.Trim().ToLowerInvariant() switch
            {
                "city" => 8000.0,
                "town" => 5000.0,
                "village" => 2500.0,
                "hamlet" => 1500.0,
                "isolated_dwelling" => 800.0,
                _ => 2000.0
            };
        }

        private static bool IsNearestMatchUnambiguous(
            IEnumerable<double> orderedDistances)
        {
            var distances = orderedDistances.Take(2).ToList();

            if (distances.Count < 2 || distances[0] <= 250.0)
            {
                return true;
            }

            // Якщо два однойменні НП майже однаково близькі, координати самі
            // по собі не є достатнім доказом. Краще залишити окремий запис,
            // ніж приклеїти контур до сусідньої Ковалівки.
            return distances[1] - distances[0] >= 500.0;
        }

        private static bool AreSameProviderEntity(
            SettlementProviderReference first,
            SettlementProviderReference second)
        {
            if (!string.Equals(
                    first.Provider,
                    second.Provider,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    first.ExternalId,
                    second.ExternalId,
                    StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            if (!string.Equals(
                    first.Provider,
                    SettlementDataSources.OpenStreetMapOverpass,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            // ID node/way/relation належать різним просторам OSM і можуть
            // випадково мати однакове число.
            return string.Equals(
                first.FeatureType,
                second.FeatureType,
                StringComparison.OrdinalIgnoreCase);
        }

        private static bool HasSameAdministrativeContext(
            SettlementGeometryItem first,
            SettlementGeometryItem second)
        {
            var hasContext =
                !string.IsNullOrWhiteSpace(first.Region) ||
                !string.IsNullOrWhiteSpace(first.District) ||
                !string.IsNullOrWhiteSpace(second.Region) ||
                !string.IsNullOrWhiteSpace(second.District);

            return hasContext &&
                   string.Equals(
                       first.Region,
                       second.Region,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       first.District,
                       second.District,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       first.Place,
                       second.Place,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string PreferExisting(string? target, string? incoming)
        {
            if (!string.IsNullOrWhiteSpace(target))
            {
                return target;
            }

            return incoming ?? string.Empty;
        }
    }
}
