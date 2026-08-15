using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MapsWPF.Services.Settlements
{
    public sealed class SettlementOverpassLoader
    {
        private const string OverpassUrl = "https://overpass-api.de/api/interpreter";

        private const double MaxResidentialAttachDistanceMeters = 8000.0;

        private const double SettlementTileSizeDegrees = 0.20;

        private const int SettlementTileDelayMs = 3000;
        private const int SettlementTileTimeoutSeconds = 30;

        private const double ResidentialTileSizeDegrees = 0.10;
        private const int ResidentialTileDelayMs = 2000;

        private const int RelationGeometryDelayMs = 15000;
        private const int RelationGeometryRequestTimeoutSeconds = 60;
        private const int RelationGeometryDelayAfter429Ms = 90000;
        private const int RelationGeometryDelayAfterServerErrorMs = 30000;

        private const double MaxRelationAttachDistanceMeters = 12000.0;

        private static readonly HashSet<string> AllowedPlaceTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "city",
                "town",
                "village",
                "hamlet",
                "isolated_dwelling"
            };

        private static readonly HashSet<string> AllowedBoundaryAdminLevels =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "9",
                "10",
                "11"
            };

        private readonly HttpClient _httpClient = new();
        private readonly TryConvertLatLngToUtmDelegate _tryConvertLatLngToUtm;

        public delegate bool TryConvertLatLngToUtmDelegate(
            double lat,
            double lng,
            out PointF utm,
            out int zone,
            out string band);

        private delegate bool TryConvertToUtm(
            double lat,
            double lng,
            out PointF utm);

        public SettlementOverpassLoader(TryConvertLatLngToUtmDelegate tryConvertLatLngToUtm)
        {
            _tryConvertLatLngToUtm = tryConvertLatLngToUtm
                ?? throw new ArgumentNullException(nameof(tryConvertLatLngToUtm));

            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "MapsWPF/1.0"
            );
        }

        public async Task<List<string>> LoadRelationPolygonsForTestAsync(
    long relationId,
    CancellationToken cancellationToken = default)
        {
            int? expectedZone = null;
            var expectedBand = string.Empty;

            TryConvertToUtm tryConvert = (
                double lat,
                double lng,
                out PointF utm) =>
            {
                utm = PointF.Empty;

                if (!_tryConvertLatLngToUtm(
                        lat,
                        lng,
                        out var converted,
                        out var zone,
                        out var band))
                {
                    return false;
                }

                if (!expectedZone.HasValue)
                {
                    expectedZone = zone;
                    expectedBand = band;
                }

                if (zone != expectedZone.Value ||
                    !string.Equals(
                        band,
                        expectedBand,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                utm = converted;
                return true;
            };

            Debug.WriteLine(
                $"[SETTLEMENT TEST] Relation {relationId}: loading started");

            var rings = await LoadRelationOuterPolygonsByIdAsync(
                relationId,
                tryConvert,
                cancellationToken);

            var polygonLines = new List<string>();

            foreach (var ring in rings)
            {
                if (ring.Count < 3)
                {
                    continue;
                }

                var polygonLine = SettlementGeometryService.ToPolygonLine(ring);

                if (string.IsNullOrWhiteSpace(polygonLine))
                {
                    continue;
                }

                polygonLines.Add(polygonLine);
            }

            Debug.WriteLine(
                $"[SETTLEMENT TEST] Relation {relationId}: " +
                $"rings={rings.Count}, polygons={polygonLines.Count}, " +
                $"zone={expectedZone}, band={expectedBand}");

            return polygonLines;
        }

        public async Task<SettlementGeometryCache> LoadAsync(
     SettlementCacheBounds bounds,
     CancellationToken cancellationToken = default,
     Action<SettlementGeometryCache>? onPartialCacheReady = null)
        {
            if (bounds == null)
            {
                throw new ArgumentNullException(nameof(bounds));
            }

            int? cacheZone = null;
            string cacheBand = string.Empty;

            TryConvertToUtm tryConvert = (
                double lat,
                double lng,
                out PointF utm) =>
            {
                utm = PointF.Empty;

                if (!_tryConvertLatLngToUtm(
                        lat,
                        lng,
                        out var converted,
                        out var zone,
                        out var band))
                {
                    return false;
                }

                if (!cacheZone.HasValue)
                {
                    cacheZone = zone;
                    cacheBand = band ?? string.Empty;
                }

                if (zone != cacheZone.Value)
                {
                    return false;
                }

                if (!string.Equals(
                        band ?? string.Empty,
                        cacheBand,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                utm = converted;
                return true;
            };

            var settlements = await LoadSettlementsByTilesAsync(
     bounds,
     tryConvert,
     cancellationToken,
     currentSettlements =>
     {
         onPartialCacheReady?.Invoke(
             CreateCacheSnapshot(
                 bounds,
                 currentSettlements,
                 cacheZone,
                 cacheBand,
                 "overpass-place-nodes-tiles"));
     });

            settlements = Deduplicate(settlements);

            Debug.WriteLine(
                $"[SETTLEMENT LOAD] Parsed settlements before residential: {settlements.Count}");

            onPartialCacheReady?.Invoke(
                CreateCacheSnapshot(
                    bounds,
                    settlements,
                    cacheZone,
                    cacheBand,
                    "overpass-place-nodes-tiles"));

            var relationCandidates = await LoadRelationGeometryCandidatesAsync(
     bounds,
     tryConvert,
     cancellationToken);

            await AttachRelationGeometriesAsync(
     settlements,
     relationCandidates,
     tryConvert,
     cancellationToken,
     () =>
     {
         onPartialCacheReady?.Invoke(
             CreateCacheSnapshot(
                 bounds,
                 settlements,
                 cacheZone,
                 cacheBand,
                 "overpass"));
     });

            Debug.WriteLine(
                $"[SETTLEMENT LOAD] Parsed settlements={settlements.Count}, " +
                $"relationCandidates={relationCandidates.Count}");

            return CreateCacheSnapshot(
     bounds,
     settlements,
     cacheZone,
     cacheBand,
     "overpass");
        }

        private static SettlementGeometryCache CreateCacheSnapshot(
    SettlementCacheBounds bounds,
    List<SettlementGeometryItem> settlements,
    int? cacheZone,
    string cacheBand,
    string source)
        {
            return new SettlementGeometryCache
            {
                Source = source,
                CoordinateSystem = "UTM",
                UtmZone = cacheZone ?? 37,
                UtmBand = string.IsNullOrWhiteSpace(cacheBand) ? "U" : cacheBand,
                Bounds = bounds,
                Settlements = settlements
            };
        }

        private async Task<List<RelationGeometryCandidate>> LoadRelationGeometryCandidatesAsync(
    SettlementCacheBounds bounds,
    TryConvertToUtm tryConvert,
    CancellationToken cancellationToken)
        {
            try
            {
                var query = BuildRelationListQuery(bounds);

                var json = await ExecuteOverpassQueryAsync(
                    query,
                    TimeSpan.FromSeconds(30),
                    cancellationToken);

                var candidates = ParseRelationGeometryCandidates(
                    json,
                    tryConvert);

                Debug.WriteLine(
                    $"[SETTLEMENT RELATION] Candidates loaded: {candidates.Count}");

                return candidates;
            }
            catch (Exception ex) when (
                ex is TimeoutException ||
                ex is HttpRequestException ||
                ex is TaskCanceledException)
            {
                Debug.WriteLine(
                    $"[SETTLEMENT RELATION] Candidates skipped: {ex.Message}");

                return new List<RelationGeometryCandidate>();
            }
        }

        private static string BuildRelationListQuery(SettlementCacheBounds bounds)
        {
            var south = Math.Min(bounds.Bottom, bounds.Top);
            var north = Math.Max(bounds.Bottom, bounds.Top);
            var west = Math.Min(bounds.Left, bounds.Right);
            var east = Math.Max(bounds.Left, bounds.Right);

            return
                "[out:json][timeout:30];" +
                "(" +
                $"relation[\"place\"~\"^(city|town|village|hamlet|isolated_dwelling)$\"]({ToInvariant(south)},{ToInvariant(west)},{ToInvariant(north)},{ToInvariant(east)});" +
                ");" +
                "out center tags;";
        }

        private static List<RelationGeometryCandidate> ParseRelationGeometryCandidates(
            string json,
            TryConvertToUtm tryConvert)
        {
            var result = new List<RelationGeometryCandidate>();

            var root = JObject.Parse(json);
            var elements = root["elements"] as JArray;

            if (elements == null)
            {
                return result;
            }

            foreach (var token in elements)
            {
                if (token is not JObject element)
                {
                    continue;
                }

                var type = GetString(element["type"]);

                if (!string.Equals(type, "relation", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var tags = element["tags"] as JObject;

                var place = GetTag(tags, "place");

                if (!AllowedPlaceTypes.Contains(place))
                {
                    continue;
                }

                var name = GetBestName(tags);

                if (!IsUsableSettlementName(name))
                {
                    continue;
                }

                var relationId = GetLong(element["id"]);

                if (relationId <= 0)
                {
                    continue;
                }

                PointF? center = null;

                if (TryReadLatLng(element["center"], out var lat, out var lng) &&
                    tryConvert(lat, lng, out var centerUtm))
                {
                    center = centerUtm;
                }

                result.Add(new RelationGeometryCandidate
                {
                    Name = name,
                    Place = place,
                    RelationId = relationId,
                    Center = center
                });
            }

            return DeduplicateRelationGeometryCandidates(result);
        }

        private static List<RelationGeometryCandidate> DeduplicateRelationGeometryCandidates(
            List<RelationGeometryCandidate> candidates)
        {
            var result = new List<RelationGeometryCandidate>();
            var seen = new HashSet<long>();

            foreach (var candidate in candidates)
            {
                if (candidate.RelationId <= 0)
                {
                    continue;
                }

                if (!seen.Add(candidate.RelationId))
                {
                    continue;
                }

                result.Add(candidate);
            }

            return result;
        }

        private async Task AttachRelationGeometriesAsync(
     List<SettlementGeometryItem> settlements,
     List<RelationGeometryCandidate> relationCandidates,
     TryConvertToUtm tryConvert,
     CancellationToken cancellationToken,
     Action? onGeometryAttached)
        {
            if (settlements == null)
            {
                return;
            }

            if (relationCandidates == null || relationCandidates.Count == 0)
            {
                Debug.WriteLine("[SETTLEMENT RELATION] No relation candidates");
                return;
            }

            Debug.WriteLine(
                $"[SETTLEMENT RELATION] Geometry loading started: {relationCandidates.Count}");

            for (var i = 0; i < relationCandidates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var candidate = relationCandidates[i];
                var delayAfterCurrentAttemptMs = RelationGeometryDelayMs;

                var existingTarget = FindSettlementTargetForRelation(
                    settlements,
                    candidate);

                if (HasUsablePolygons(existingTarget))
                {
                    Debug.WriteLine(
                        $"[SETTLEMENT RELATION] {i + 1}/{relationCandidates.Count}: " +
                        $"{candidate.Name}, relation={candidate.RelationId} skipped: already has polygon");

                    continue;
                }

                try
                {
                    Debug.WriteLine(
                        $"[SETTLEMENT RELATION] {i + 1}/{relationCandidates.Count}: " +
                        $"{candidate.Name}, relation={candidate.RelationId}");

                    var rings = await LoadRelationOuterPolygonsByIdAsync(
                        candidate.RelationId,
                        tryConvert,
                        cancellationToken);

                    Debug.WriteLine(
                        $"[SETTLEMENT RELATION] {candidate.Name}: rings={rings.Count}");

                    if (rings.Count == 0)
                    {
                        continue;
                    }

                    var target = existingTarget;

                    if (target == null)
                    {
                        target = CreateSettlementFromRelationCandidate(candidate);
                        settlements.Add(target);
                    }

                    target.Polygons ??= new List<string>();

                    var existing = new HashSet<string>(
                        target.Polygons,
                        StringComparer.OrdinalIgnoreCase);

                    var added = 0;

                    foreach (var ring in rings)
                    {
                        if (ring.Count < 3)
                        {
                            continue;
                        }

                        var line = SettlementGeometryService.ToPolygonLine(ring);

                        if (string.IsNullOrWhiteSpace(line))
                        {
                            continue;
                        }

                        if (!existing.Add(line))
                        {
                            continue;
                        }

                        target.Polygons.Add(line);
                        added++;
                    }

                    if (added > 0)
                    {
                        target.GeometrySourceOsmType = "relation";
                        target.GeometrySourceOsmId = candidate.RelationId;

                        onGeometryAttached?.Invoke();
                    }

                    Debug.WriteLine(
                        $"[SETTLEMENT RELATION] {candidate.Name}: polygons attached={added}");
                }
                catch (Exception ex) when (
     ex is TimeoutException ||
     ex is HttpRequestException ||
     ex is TaskCanceledException)
                {
                    delayAfterCurrentAttemptMs = GetRelationDelayAfterErrorMs(ex);

                    Debug.WriteLine(
                        $"[SETTLEMENT RELATION] {candidate.Name}: skipped: {ex.Message}");

                    Debug.WriteLine(
                        $"[SETTLEMENT RELATION] Delay after error: {delayAfterCurrentAttemptMs / 1000}s");
                }
                catch (Exception ex)
                {
                    delayAfterCurrentAttemptMs = RelationGeometryDelayAfterServerErrorMs;

                    Debug.WriteLine(
                        $"[SETTLEMENT RELATION] {candidate.Name}: error: {ex}");

                    Debug.WriteLine(
                        $"[SETTLEMENT RELATION] Delay after error: {delayAfterCurrentAttemptMs / 1000}s");
                }

                if (delayAfterCurrentAttemptMs > 0)
                {
                    await Task.Delay(delayAfterCurrentAttemptMs, cancellationToken);
                }
            }
        }

        private static bool HasUsablePolygons(SettlementGeometryItem? settlement)
        {
            return settlement?.Polygons != null &&
                   settlement.Polygons.Any(x => !string.IsNullOrWhiteSpace(x));
        }

        private static int GetRelationDelayAfterErrorMs(Exception ex)
        {
            var message = ex.Message ?? string.Empty;

            if (ex is HttpRequestException &&
                message.Contains("429", StringComparison.OrdinalIgnoreCase))
            {
                return RelationGeometryDelayAfter429Ms;
            }

            if (ex is HttpRequestException &&
                message.Contains("504", StringComparison.OrdinalIgnoreCase))
            {
                return RelationGeometryDelayAfterServerErrorMs;
            }

            if (ex is TimeoutException || ex is TaskCanceledException)
            {
                return RelationGeometryDelayAfterServerErrorMs;
            }

            return RelationGeometryDelayMs;
        }

        private static SettlementGeometryItem? FindSettlementTargetForRelation(
            List<SettlementGeometryItem> settlements,
            RelationGeometryCandidate relation)
        {
            var relationNameKey = NormalizeNameKey(relation.Name);

            if (string.IsNullOrWhiteSpace(relationNameKey))
            {
                return null;
            }

            var candidates = settlements
                .Where(s => NormalizeNameKey(s.Name) == relationNameKey)
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            if (candidates.Count == 1)
            {
                return candidates[0];
            }

            if (!relation.Center.HasValue)
            {
                return candidates[0];
            }

            SettlementGeometryItem? best = null;
            double bestDistance = double.MaxValue;

            foreach (var candidate in candidates)
            {
                if (!TryGetFallbackPoint(candidate, out var anchor))
                {
                    continue;
                }

                var distance = GetDistance(relation.Center.Value, anchor);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            if (best != null && bestDistance <= MaxRelationAttachDistanceMeters)
            {
                return best;
            }

            return candidates[0];
        }

        private static SettlementGeometryItem CreateSettlementFromRelationCandidate(
            RelationGeometryCandidate candidate)
        {
            var item = new SettlementGeometryItem
            {
                Name = candidate.Name,
                Place = candidate.Place,
                OsmType = "relation",
                OsmId = candidate.RelationId,
                GeometrySourceOsmType = "relation",
                GeometrySourceOsmId = candidate.RelationId
            };

            if (candidate.Center.HasValue)
            {
                item.FallbackPoint = new SettlementPoint(
                    candidate.Center.Value.X,
                    candidate.Center.Value.Y);
            }

            return item;
        }

        private async Task<List<List<PointF>>> LoadRelationOuterPolygonsByIdAsync(
     long relationId,
     TryConvertToUtm tryConvert,
     CancellationToken cancellationToken)
        {
            var query =
     $"[out:json][timeout:{RelationGeometryRequestTimeoutSeconds}];" +
     $"relation({relationId});" +
     "out body;" +
     ">;" +
     "out body qt;";

            var json = await ExecuteOverpassQueryAsync(
                query,
                TimeSpan.FromSeconds(RelationGeometryRequestTimeoutSeconds),
                cancellationToken);

            return ParseRelationOuterPolygons(
                json,
                relationId,
                tryConvert);
        }

        private static List<List<PointF>> ParseRelationOuterPolygons(
            string json,
            long relationId,
            TryConvertToUtm tryConvert)
        {
            var root = JObject.Parse(json);
            var elements = root["elements"] as JArray;

            if (elements == null)
            {
                return new List<List<PointF>>();
            }

            JObject? relation = null;
            var ways = new Dictionary<long, JObject>();
            var nodes = new Dictionary<long, JObject>();

            foreach (var token in elements)
            {
                if (token is not JObject element)
                {
                    continue;
                }

                var type = GetString(element["type"]);
                var id = GetLong(element["id"]);

                if (string.Equals(type, "relation", StringComparison.OrdinalIgnoreCase) &&
                    id == relationId)
                {
                    relation = element;
                    continue;
                }

                if (string.Equals(type, "way", StringComparison.OrdinalIgnoreCase))
                {
                    ways[id] = element;
                    continue;
                }

                if (string.Equals(type, "node", StringComparison.OrdinalIgnoreCase))
                {
                    nodes[id] = element;
                }
            }

            if (relation == null)
            {
                Debug.WriteLine($"[SETTLEMENT RELATION TEST] Relation {relationId} not found");
                return new List<List<PointF>>();
            }

            var members = relation["members"] as JArray;

            if (members == null)
            {
                Debug.WriteLine($"[SETTLEMENT RELATION TEST] Relation {relationId} has no members");
                return new List<List<PointF>>();
            }

            var segments = new List<List<PointF>>();

            foreach (var token in members)
            {
                if (token is not JObject member)
                {
                    continue;
                }

                var memberType = GetString(member["type"]);

                if (!string.Equals(memberType, "way", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var role = GetString(member["role"]);

                if (!string.IsNullOrWhiteSpace(role) &&
                    !string.Equals(role, "outer", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var wayRef = GetLong(member["ref"]);

                if (!ways.TryGetValue(wayRef, out var way))
                {
                    continue;
                }

                var segment = ReadWayNodesAsUtmPoints(
                    way,
                    nodes,
                    tryConvert);

                if (segment.Count >= 2)
                {
                    segments.Add(segment);
                }
            }

            Debug.WriteLine(
     $"[SETTLEMENT RELATION] Relation {relationId}: " +
     $"ways={ways.Count}, nodes={nodes.Count}, outerSegments={segments.Count}");

            return BuildRingsFromSegments(segments);
        }

        private static List<PointF> ReadWayNodesAsUtmPoints(
            JObject way,
            Dictionary<long, JObject> nodes,
            TryConvertToUtm tryConvert)
        {
            var result = new List<PointF>();

            if (way["nodes"] is not JArray nodeRefs)
            {
                return result;
            }

            foreach (var token in nodeRefs)
            {
                var nodeRef = GetLong(token);

                if (!nodes.TryGetValue(nodeRef, out var node))
                {
                    continue;
                }

                if (!TryReadLatLng(node, out var lat, out var lng))
                {
                    continue;
                }

                if (!tryConvert(lat, lng, out var utm))
                {
                    continue;
                }

                result.Add(utm);
            }

            return RemoveDuplicateConsecutivePoints(result);
        }

        private static SettlementGeometryItem? ParseElement(
            JObject element,
            TryConvertToUtm tryConvert)
        {
            var osmType = GetString(element["type"]);
            var osmId = GetLong(element["id"]);

            var tags = element["tags"] as JObject;

            var place = GetTag(tags, "place");
            var boundary = GetTag(tags, "boundary");
            var adminLevel = GetTag(tags, "admin_level");

            var isAllowedPlace =
                AllowedPlaceTypes.Contains(place);

            var isAllowedAdministrativeBoundary =
                string.Equals(boundary, "administrative", StringComparison.OrdinalIgnoreCase) &&
                AllowedBoundaryAdminLevels.Contains(adminLevel);

            if (!isAllowedPlace && !isAllowedAdministrativeBoundary)
            {
                return null;
            }

            var name = GetBestName(tags);

            if (!IsUsableSettlementName(name))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(place))
            {
                place = string.IsNullOrWhiteSpace(adminLevel)
                    ? "boundary"
                    : $"boundary:{adminLevel}";
            }

            var item = new SettlementGeometryItem
            {
                Name = name,
                Place = place,
                OsmType = osmType,
                OsmId = osmId
            };

            if (string.Equals(osmType, "node", StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadLatLng(element, out var lat, out var lng) &&
                    tryConvert(lat, lng, out var utm))
                {
                    item.FallbackPoint = new SettlementPoint(utm.X, utm.Y);
                }
            }
            else if (string.Equals(osmType, "way", StringComparison.OrdinalIgnoreCase))
            {
                var polygon = ReadGeometryAsUtmPoints(element["geometry"], tryConvert);

                if (polygon.Count >= 3)
                {
                    item.Polygons.Add(SettlementGeometryService.ToPolygonLine(polygon));
                }

                if (item.Polygons.Count == 0 &&
                    TryReadLatLng(element["center"], out var lat, out var lng) &&
                    tryConvert(lat, lng, out var utm))
                {
                    item.FallbackPoint = new SettlementPoint(utm.X, utm.Y);
                }
            }
            else if (string.Equals(osmType, "relation", StringComparison.OrdinalIgnoreCase))
            {
                var directGeometry = ReadGeometryAsUtmPoints(element["geometry"], tryConvert);

                if (directGeometry.Count >= 3)
                {
                    item.Polygons.Add(SettlementGeometryService.ToPolygonLine(directGeometry));
                }

                var segments = ReadRelationOuterSegments(element["members"], tryConvert);
                var rings = BuildRingsFromSegments(segments);

                foreach (var ring in rings)
                {
                    if (ring.Count >= 3)
                    {
                        item.Polygons.Add(SettlementGeometryService.ToPolygonLine(ring));
                    }
                }

                if (item.Polygons.Count == 0 &&
                    TryReadLatLng(element["center"], out var lat, out var lng) &&
                    tryConvert(lat, lng, out var utm))
                {
                    item.FallbackPoint = new SettlementPoint(utm.X, utm.Y);
                }
            }

            if (item.Polygons.Count == 0 && item.FallbackPoint == null)
            {
                return null;
            }

            return item;
        }

        private static ResidentialGeometryItem? ParseResidentialGeometry(
            JObject element,
            TryConvertToUtm tryConvert)
        {
            var osmType = GetString(element["type"]);

            if (string.Equals(osmType, "node", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var tags = element["tags"] as JObject;

            var item = new ResidentialGeometryItem
            {
                Name = GetBestName(tags),
                OsmType = osmType,
                OsmId = GetLong(element["id"])
            };

            var directGeometry = ReadGeometryAsUtmPoints(element["geometry"], tryConvert);

            if (directGeometry.Count >= 3)
            {
                item.Polygons.Add(directGeometry);
            }

            var segments = ReadRelationOuterSegments(element["members"], tryConvert);
            var rings = BuildRingsFromSegments(segments);

            foreach (var ring in rings)
            {
                if (ring.Count >= 3)
                {
                    item.Polygons.Add(ring);
                }
            }

            if (item.Polygons.Count == 0)
            {
                return null;
            }

            if (TryReadLatLng(element["center"], out var lat, out var lng) &&
                tryConvert(lat, lng, out var centerUtm))
            {
                item.Center = centerUtm;
            }
            else
            {
                item.Center = GetAveragePoint(item.Polygons);
            }

            return item;
        }

        private static void AttachResidentialPolygonsToSettlements(
            List<SettlementGeometryItem> settlements,
            List<ResidentialGeometryItem> residentialAreas)
        {
            if (settlements.Count == 0 || residentialAreas.Count == 0)
            {
                return;
            }

            foreach (var residentialArea in residentialAreas)
            {
                if (residentialArea.Polygons.Count == 0)
                {
                    continue;
                }

                var target = FindNamedSettlementTarget(
                    settlements,
                    residentialArea);

                target ??= FindNearestSettlementTarget(
                    settlements,
                    residentialArea.Center,
                    MaxResidentialAttachDistanceMeters);

                if (target == null)
                {
                    continue;
                }

                var existing = new HashSet<string>(
                    target.Polygons ?? new List<string>(),
                    StringComparer.OrdinalIgnoreCase);

                target.Polygons ??= new List<string>();

                foreach (var polygon in residentialArea.Polygons)
                {
                    if (polygon.Count < 3)
                    {
                        continue;
                    }

                    var line = SettlementGeometryService.ToPolygonLine(polygon);

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (!existing.Add(line))
                    {
                        continue;
                    }

                    target.Polygons.Add(line);
                }
            }
        }

        private static SettlementGeometryItem? FindNamedSettlementTarget(
            List<SettlementGeometryItem> settlements,
            ResidentialGeometryItem residentialArea)
        {
            if (string.IsNullOrWhiteSpace(residentialArea.Name))
            {
                return null;
            }

            var residentialNameKey = NormalizeNameKey(residentialArea.Name);

            if (string.IsNullOrWhiteSpace(residentialNameKey))
            {
                return null;
            }

            var candidates = settlements
                .Where(s => NormalizeNameKey(s.Name) == residentialNameKey)
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            SettlementGeometryItem? best = null;
            double bestDistance = double.MaxValue;

            foreach (var candidate in candidates)
            {
                if (!TryGetFallbackPoint(candidate, out var anchor))
                {
                    best ??= candidate;
                    continue;
                }

                var distance = GetDistance(residentialArea.Center, anchor);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        private static SettlementGeometryItem? FindNearestSettlementTarget(
            List<SettlementGeometryItem> settlements,
            PointF residentialCenter,
            double maxDistanceMeters)
        {
            SettlementGeometryItem? best = null;
            double bestDistance = double.MaxValue;

            foreach (var settlement in settlements)
            {
                if (!TryGetFallbackPoint(settlement, out var anchor))
                {
                    continue;
                }

                var distance = GetDistance(residentialCenter, anchor);

                if (distance > maxDistanceMeters)
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = settlement;
                }
            }

            return best;
        }

        private static bool TryGetFallbackPoint(
            SettlementGeometryItem settlement,
            out PointF point)
        {
            point = PointF.Empty;

            if (settlement.FallbackPoint == null)
            {
                return false;
            }

            point = new PointF(
                settlement.FallbackPoint.X,
                settlement.FallbackPoint.Y);

            return true;
        }

        private static bool IsResidentialElement(JObject element)
        {
            var tags = element["tags"] as JObject;

            return string.Equals(
                GetTag(tags, "landuse"),
                "residential",
                StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildSettlementListQuery(SettlementCacheBounds bounds)
        {
            var south = Math.Min(bounds.Bottom, bounds.Top);
            var north = Math.Max(bounds.Bottom, bounds.Top);
            var west = Math.Min(bounds.Left, bounds.Right);
            var east = Math.Max(bounds.Left, bounds.Right);

            return
                $"[out:json][timeout:{SettlementTileTimeoutSeconds}];" +
                "(" +
                $"node[\"place\"~\"^(city|town|village|hamlet|isolated_dwelling)$\"]({ToInvariant(south)},{ToInvariant(west)},{ToInvariant(north)},{ToInvariant(east)});" +
                ");" +
                "out body qt;";
        }

        private static string BuildResidentialAreaQuery(SettlementCacheBounds bounds)
        {
            var south = Math.Min(bounds.Bottom, bounds.Top);
            var north = Math.Max(bounds.Bottom, bounds.Top);
            var west = Math.Min(bounds.Left, bounds.Right);
            var east = Math.Max(bounds.Left, bounds.Right);

            return
                "[out:json][timeout:25];" +
                "(" +
                $"way[\"landuse\"=\"residential\"]({ToInvariant(south)},{ToInvariant(west)},{ToInvariant(north)},{ToInvariant(east)});" +
                $"relation[\"landuse\"=\"residential\"]({ToInvariant(south)},{ToInvariant(west)},{ToInvariant(north)},{ToInvariant(east)});" +
                ");" +
                "out body geom center;";
        }

        private async Task<string> ExecuteOverpassQueryAsync(
    string query,
    TimeSpan timeout,
    CancellationToken cancellationToken)
        {
            using var timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            timeoutCts.CancelAfter(timeout);

            try
            {
                using var content = new FormUrlEncodedContent(
                    new[]
                    {
                new KeyValuePair<string, string>("data", query)
                    });

                using var response = await _httpClient.PostAsync(
                    OverpassUrl,
                    content,
                    timeoutCts.Token);

                response.EnsureSuccessStatusCode();

                return await response.Content.ReadAsStringAsync(timeoutCts.Token);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException("Overpass не відповів вчасно.");
            }
        }

        private static List<SettlementGeometryItem> ParseSettlements(
            string json,
            TryConvertToUtm tryConvert)
        {
            var result = new List<SettlementGeometryItem>();

            var root = JObject.Parse(json);
            var elements = root["elements"] as JArray;

            if (elements == null)
            {
                return result;
            }

            foreach (var token in elements)
            {
                if (token is not JObject element)
                {
                    continue;
                }

                var settlement = ParseElement(element, tryConvert);

                if (settlement == null)
                {
                    continue;
                }

                result.Add(settlement);
            }

            return result;
        }

        private async Task<List<SettlementGeometryItem>> LoadSettlementsByTilesAsync(
            SettlementCacheBounds bounds,
            TryConvertToUtm tryConvert,
            CancellationToken cancellationToken,
            Action<List<SettlementGeometryItem>>? onProgress = null)
        {
            var result = new List<SettlementGeometryItem>();
            var tiles = CreateSettlementTiles(bounds).ToList();

            Debug.WriteLine($"[SETTLEMENT LOAD] Settlement tiles: {tiles.Count}");

            for (var i = 0; i < tiles.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var tile = tiles[i];

                try
                {
                    Debug.WriteLine(
                        $"[SETTLEMENT LOAD] Settlement tile {i + 1}/{tiles.Count}: " +
                        $"Top={tile.Top}, Bottom={tile.Bottom}, Left={tile.Left}, Right={tile.Right}");

                    var query = BuildSettlementListQuery(tile);

                    var json = await ExecuteOverpassQueryAsync(
                        query,
                        TimeSpan.FromSeconds(SettlementTileTimeoutSeconds),
                        cancellationToken);

                    var items = ParseSettlements(
                        json,
                        tryConvert);

                    if (items.Count > 0)
                    {
                        result.AddRange(items);
                        result = Deduplicate(result);

                        onProgress?.Invoke(new List<SettlementGeometryItem>(result));
                    }

                    Debug.WriteLine(
                        $"[SETTLEMENT LOAD] Settlement tile {i + 1}/{tiles.Count}: " +
                        $"items={items.Count}, total={result.Count}");
                }
                catch (Exception ex) when (
                    !cancellationToken.IsCancellationRequested &&
                    (ex is TimeoutException ||
                     ex is HttpRequestException ||
                     ex is TaskCanceledException))
                {
                    Debug.WriteLine(
                        $"[SETTLEMENT LOAD] Settlement tile {i + 1}/{tiles.Count} skipped: {ex.Message}");
                }
                catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                {
                    Debug.WriteLine(
                        $"[SETTLEMENT LOAD] Settlement tile {i + 1}/{tiles.Count} error: {ex}");
                }

                if (SettlementTileDelayMs > 0)
                {
                    await Task.Delay(SettlementTileDelayMs, cancellationToken);
                }
            }

            return result;
        }

        private async Task<List<ResidentialGeometryItem>> LoadResidentialAreasByTilesAsync(
            SettlementCacheBounds bounds,
            TryConvertToUtm tryConvert,
            CancellationToken cancellationToken)
        {
            var result = new List<ResidentialGeometryItem>();

            var tiles = CreateResidentialTiles(bounds).ToList();

            Debug.WriteLine($"[SETTLEMENT LOAD] Residential tiles: {tiles.Count}");

            for (var i = 0; i < tiles.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var tile = tiles[i];

                try
                {
                    Debug.WriteLine(
                        $"[SETTLEMENT LOAD] Residential tile {i + 1}/{tiles.Count}: " +
                        $"Top={tile.Top}, Bottom={tile.Bottom}, Left={tile.Left}, Right={tile.Right}");

                    var query = BuildResidentialAreaQuery(tile);

                    var json = await ExecuteOverpassQueryAsync(
                        query,
                        TimeSpan.FromSeconds(25),
                        cancellationToken);

                    var tileAreas = ParseResidentialAreas(
                        json,
                        tryConvert);

                    result.AddRange(tileAreas);

                    Debug.WriteLine(
                        $"[SETTLEMENT LOAD] Residential tile {i + 1}/{tiles.Count}: " +
                        $"areas={tileAreas.Count}, total={result.Count}");
                }
                catch (Exception ex) when (
                    ex is TimeoutException ||
                    ex is HttpRequestException ||
                    ex is TaskCanceledException)
                {
                    Debug.WriteLine(
                        $"[SETTLEMENT LOAD] Residential tile {i + 1}/{tiles.Count} skipped: {ex.Message}");
                }

                if (ResidentialTileDelayMs > 0)
                {
                    await Task.Delay(ResidentialTileDelayMs, cancellationToken);
                }
            }

            return result;
        }

        private static List<ResidentialGeometryItem> ParseResidentialAreas(
            string json,
            TryConvertToUtm tryConvert)
        {
            var result = new List<ResidentialGeometryItem>();

            var root = JObject.Parse(json);
            var elements = root["elements"] as JArray;

            if (elements == null)
            {
                return result;
            }

            foreach (var token in elements)
            {
                if (token is not JObject element)
                {
                    continue;
                }

                if (!IsResidentialElement(element))
                {
                    continue;
                }

                var area = ParseResidentialGeometry(element, tryConvert);

                if (area == null)
                {
                    continue;
                }

                result.Add(area);
            }

            return DeduplicateResidentialAreas(result);
        }

        private static IEnumerable<SettlementCacheBounds> CreateSettlementTiles(
    SettlementCacheBounds bounds)
        {
            var south = Math.Min(bounds.Bottom, bounds.Top);
            var north = Math.Max(bounds.Bottom, bounds.Top);
            var west = Math.Min(bounds.Left, bounds.Right);
            var east = Math.Max(bounds.Left, bounds.Right);

            for (var tileSouth = south; tileSouth < north; tileSouth += SettlementTileSizeDegrees)
            {
                var tileNorth = Math.Min(
                    tileSouth + SettlementTileSizeDegrees,
                    north);

                for (var tileWest = west; tileWest < east; tileWest += SettlementTileSizeDegrees)
                {
                    var tileEast = Math.Min(
                        tileWest + SettlementTileSizeDegrees,
                        east);

                    yield return new SettlementCacheBounds
                    {
                        Top = tileNorth,
                        Bottom = tileSouth,
                        Left = tileWest,
                        Right = tileEast,
                        PaddingKm = bounds.PaddingKm
                    };
                }
            }
        }

        private static IEnumerable<SettlementCacheBounds> CreateResidentialTiles(
            SettlementCacheBounds bounds)
        {
            var south = Math.Min(bounds.Bottom, bounds.Top);
            var north = Math.Max(bounds.Bottom, bounds.Top);
            var west = Math.Min(bounds.Left, bounds.Right);
            var east = Math.Max(bounds.Left, bounds.Right);

            for (var tileSouth = south; tileSouth < north; tileSouth += ResidentialTileSizeDegrees)
            {
                var tileNorth = Math.Min(
                    tileSouth + ResidentialTileSizeDegrees,
                    north);

                for (var tileWest = west; tileWest < east; tileWest += ResidentialTileSizeDegrees)
                {
                    var tileEast = Math.Min(
                        tileWest + ResidentialTileSizeDegrees,
                        east);

                    yield return new SettlementCacheBounds
                    {
                        Top = tileNorth,
                        Bottom = tileSouth,
                        Left = tileWest,
                        Right = tileEast,
                        PaddingKm = bounds.PaddingKm
                    };
                }
            }
        }

        private static List<ResidentialGeometryItem> DeduplicateResidentialAreas(
            List<ResidentialGeometryItem> areas)
        {
            var result = new List<ResidentialGeometryItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var area in areas)
            {
                var key = area.OsmId > 0
                    ? $"{area.OsmType}:{area.OsmId}"
                    : $"{area.Name}:{area.Center.X:0}:{area.Center.Y:0}";

                if (!seen.Add(key))
                {
                    continue;
                }

                result.Add(area);
            }

            return result;
        }

        private static List<PointF> ReadGeometryAsUtmPoints(
            JToken? geometryToken,
            TryConvertToUtm tryConvert)
        {
            var result = new List<PointF>();

            if (geometryToken is not JArray geometry)
            {
                return result;
            }

            foreach (var token in geometry)
            {
                if (token is not JObject pointObject)
                {
                    continue;
                }

                if (!TryReadLatLng(pointObject, out var lat, out var lng))
                {
                    continue;
                }

                if (!tryConvert(lat, lng, out var utm))
                {
                    continue;
                }

                result.Add(utm);
            }

            return RemoveDuplicateConsecutivePoints(result);
        }

        private static List<List<PointF>> ReadRelationOuterSegments(
            JToken? membersToken,
            TryConvertToUtm tryConvert)
        {
            var result = new List<List<PointF>>();

            if (membersToken is not JArray members)
            {
                return result;
            }

            foreach (var token in members)
            {
                if (token is not JObject member)
                {
                    continue;
                }

                var role = GetString(member["role"]);

                if (!string.IsNullOrWhiteSpace(role) &&
                    !string.Equals(role, "outer", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var segment = ReadGeometryAsUtmPoints(member["geometry"], tryConvert);

                if (segment.Count >= 2)
                {
                    result.Add(segment);
                }
            }

            return result;
        }

        private static List<List<PointF>> BuildRingsFromSegments(
            List<List<PointF>> segments)
        {
            var result = new List<List<PointF>>();

            var remaining = segments
                .Where(s => s != null && s.Count >= 2)
                .Select(s => new List<PointF>(s))
                .ToList();

            while (remaining.Count > 0)
            {
                var ring = remaining[0];
                remaining.RemoveAt(0);

                var changed = true;

                while (changed)
                {
                    changed = false;

                    for (var i = remaining.Count - 1; i >= 0; i--)
                    {
                        var segment = remaining[i];

                        if (TryAttachSegment(ring, segment))
                        {
                            remaining.RemoveAt(i);
                            changed = true;
                        }
                    }
                }

                ring = RemoveDuplicateConsecutivePoints(ring);

                if (ring.Count >= 3)
                {
                    result.Add(ring);
                }
            }

            return result;
        }

        private static bool TryAttachSegment(
            List<PointF> ring,
            List<PointF> segment)
        {
            if (ring.Count == 0 || segment.Count == 0)
            {
                return false;
            }

            var ringFirst = ring[0];
            var ringLast = ring[ring.Count - 1];

            var segmentFirst = segment[0];
            var segmentLast = segment[segment.Count - 1];

            if (AreSameUtmPoint(ringLast, segmentFirst))
            {
                ring.AddRange(segment.Skip(1));
                return true;
            }

            if (AreSameUtmPoint(ringLast, segmentLast))
            {
                var reversed = segment.AsEnumerable().Reverse().Skip(1);
                ring.AddRange(reversed);
                return true;
            }

            if (AreSameUtmPoint(ringFirst, segmentLast))
            {
                ring.InsertRange(0, segment.Take(segment.Count - 1));
                return true;
            }

            if (AreSameUtmPoint(ringFirst, segmentFirst))
            {
                var reversed = segment.AsEnumerable().Reverse().Take(segment.Count - 1);
                ring.InsertRange(0, reversed);
                return true;
            }

            return false;
        }

        private static List<PointF> RemoveDuplicateConsecutivePoints(
            List<PointF> points)
        {
            var result = new List<PointF>();

            foreach (var point in points)
            {
                if (result.Count == 0 ||
                    !AreSameUtmPoint(result[result.Count - 1], point))
                {
                    result.Add(point);
                }
            }

            if (result.Count > 1 &&
                AreSameUtmPoint(result[0], result[result.Count - 1]))
            {
                result.RemoveAt(result.Count - 1);
            }

            return result;
        }

        private static bool TryReadLatLng(
            JToken? token,
            out double lat,
            out double lng)
        {
            lat = 0;
            lng = 0;

            if (token is not JObject obj)
            {
                return false;
            }

            if (!TryReadDouble(obj["lat"], out lat))
            {
                return false;
            }

            if (!TryReadDouble(obj["lon"], out lng) &&
                !TryReadDouble(obj["lng"], out lng))
            {
                return false;
            }

            return true;
        }

        private static bool TryReadDouble(
            JToken? token,
            out double value)
        {
            value = 0;

            if (token == null)
            {
                return false;
            }

            if (token.Type == JTokenType.Float ||
                token.Type == JTokenType.Integer)
            {
                value = token.Value<double>();
                return true;
            }

            return double.TryParse(
                token.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        private static string GetBestName(JObject? tags)
        {
            var nameUk = GetTag(tags, "name:uk");

            if (!string.IsNullOrWhiteSpace(nameUk))
            {
                return nameUk.Trim();
            }

            var name = GetTag(tags, "name");

            return name.Trim();
        }

        private static string GetTag(
            JObject? tags,
            string key)
        {
            if (tags == null)
            {
                return string.Empty;
            }

            return tags[key]?.ToString()?.Trim() ?? string.Empty;
        }

        private static long GetLong(JToken? token)
        {
            if (token == null)
            {
                return 0;
            }

            return long.TryParse(
                token.ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value)
                ? value
                : 0;
        }

        private static string GetString(JToken? token)
        {
            return token?.ToString()?.Trim() ?? string.Empty;
        }

        private static List<SettlementGeometryItem> Deduplicate(
            List<SettlementGeometryItem> settlements)
        {
            var result = new List<SettlementGeometryItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var settlement in settlements)
            {
                var key = settlement.OsmId > 0
                    ? $"{settlement.OsmType}:{settlement.OsmId}"
                    : $"{settlement.Name}:{settlement.Place}";

                if (!seen.Add(key))
                {
                    continue;
                }

                result.Add(settlement);
            }

            return result;
        }

        private static bool IsUsableSettlementName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var name = value.Trim().ToLowerInvariant();

            return !name.Contains("область") &&
                   !name.Contains("район") &&
                   !name.Contains("громада") &&
                   !name.Contains("територіальна громада") &&
                   !name.Contains("міська громада") &&
                   !name.Contains("сільська громада") &&
                   !name.Contains("селищна громада") &&
                   !name.Contains("міська рада") &&
                   !name.Contains("сільська рада") &&
                   !name.Contains("селищна рада") &&
                   !name.Contains("вулиця") &&
                   !name.Contains("вул.") &&
                   !name.Contains("площа") &&
                   !name.Contains("дорога") &&
                   !name.Contains("шосе") &&
                   !name.Contains("стадіон") &&
                   !name.Contains("парк") &&
                   !name.Contains("кладовище") &&
                   !name.Contains("урочище");
        }

        private static bool AreSameUtmPoint(PointF a, PointF b)
        {
            return Math.Abs(a.X - b.X) <= 0.5 &&
                   Math.Abs(a.Y - b.Y) <= 0.5;
        }

        private static double GetDistance(PointF a, PointF b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;

            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static PointF GetAveragePoint(List<List<PointF>> polygons)
        {
            double sumX = 0;
            double sumY = 0;
            var count = 0;

            foreach (var polygon in polygons)
            {
                foreach (var point in polygon)
                {
                    sumX += point.X;
                    sumY += point.Y;
                    count++;
                }
            }

            if (count == 0)
            {
                return PointF.Empty;
            }

            return new PointF(
                (float)(sumX / count),
                (float)(sumY / count));
        }

        private static string NormalizeNameKey(string? value)
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

        private static string ToInvariant(double value)
        {
            return value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        private sealed class ResidentialGeometryItem
        {
            public string Name { get; set; } = string.Empty;

            public string OsmType { get; set; } = string.Empty;

            public long OsmId { get; set; }

            public PointF Center { get; set; }

            public List<List<PointF>> Polygons { get; set; } = new();
        }

        private sealed class RelationGeometryCandidate
        {
            public string Name { get; set; } = string.Empty;

            public string Place { get; set; } = string.Empty;

            public long RelationId { get; set; }

            public PointF? Center { get; set; }
        }

    }
}