using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using System.Globalization;

namespace MapsWPF.Services.Settlements
{
    public sealed class SettlementGeometryService
    {
        private const string CacheFileName = "settlements_geometry_cache.json";

        private const double MaxPreservedGeometryAttachDistanceMeters = 15000.0;

        private readonly string _settingsFolderPath;
        private readonly string _cacheFilePath;

        private SettlementGeometryCache _cache = new();

        public SettlementGeometryService()
        {
            _settingsFolderPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MapsWPF",
                "settings"
            );

            _cacheFilePath = Path.Combine(_settingsFolderPath, CacheFileName);

            Load();
        }

        public string CacheFilePath => _cacheFilePath;

        public bool HasSettlements =>
            _cache.Settlements != null &&
            _cache.Settlements.Count > 0;

        public int SettlementCount =>
            _cache.Settlements?.Count ?? 0;

        public SettlementGeometryCache CurrentCache => _cache;

        public void Load()
        {
            try
            {
                if (!File.Exists(_cacheFilePath))
                {
                    _cache = new SettlementGeometryCache();
                    return;
                }

                var json = File.ReadAllText(_cacheFilePath);

                var loaded = JsonConvert.DeserializeObject<SettlementGeometryCache>(json);

                _cache = loaded ?? new SettlementGeometryCache();

                if (_cache.Settlements == null)
                {
                    _cache.Settlements = new List<SettlementGeometryItem>();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SETTLEMENT CACHE] Load error: {ex}");
                _cache = new SettlementGeometryCache();
            }
        }

        public void Save(SettlementGeometryCache cache)
        {
            try
            {
                Directory.CreateDirectory(_settingsFolderPath);

                cache.CreatedAt = DateTime.Now;

                var json = JsonConvert.SerializeObject(cache, Formatting.Indented);

                File.WriteAllText(_cacheFilePath, json);

                _cache = cache;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SETTLEMENT CACHE] Save error: {ex}");
            }
        }

        public void SavePreservingExistingGeometry(SettlementGeometryCache cache)
        {
            if (cache == null)
            {
                return;
            }

            try
            {
                Load();

                PreserveExistingGeometry(_cache, cache);

                Save(cache);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SETTLEMENT CACHE] Save preserving error: {ex}");

                Save(cache);
            }
        }

        private static void PreserveExistingGeometry(
    SettlementGeometryCache existingCache,
    SettlementGeometryCache newCache)
        {
            if (existingCache?.Settlements == null ||
                newCache?.Settlements == null ||
                existingCache.Settlements.Count == 0 ||
                newCache.Settlements.Count == 0)
            {
                return;
            }

            foreach (var newSettlement in newCache.Settlements)
            {
                var existingSettlement = FindMatchingExistingSettlement(
                    existingCache.Settlements,
                    newSettlement);

                if (existingSettlement == null)
                {
                    continue;
                }

                if (existingSettlement.Polygons != null &&
                    existingSettlement.Polygons.Count > 0)
                {
                    newSettlement.Polygons ??= new List<string>();

                    var existingLines = new HashSet<string>(
                        newSettlement.Polygons,
                        StringComparer.OrdinalIgnoreCase);

                    foreach (var polygonLine in existingSettlement.Polygons)
                    {
                        if (string.IsNullOrWhiteSpace(polygonLine))
                        {
                            continue;
                        }

                        if (!existingLines.Add(polygonLine))
                        {
                            continue;
                        }

                        newSettlement.Polygons.Add(polygonLine);
                    }
                }

                if (newSettlement.GeometrySourceOsmId <= 0 &&
                    existingSettlement.GeometrySourceOsmId > 0)
                {
                    newSettlement.GeometrySourceOsmType =
                        existingSettlement.GeometrySourceOsmType;

                    newSettlement.GeometrySourceOsmId =
                        existingSettlement.GeometrySourceOsmId;
                }
            }
        }

        private static SettlementGeometryItem? FindMatchingExistingSettlement(
            List<SettlementGeometryItem> existingSettlements,
            SettlementGeometryItem newSettlement)
        {
            var newNameKey = NormalizeNameKey(newSettlement.Name);

            if (string.IsNullOrWhiteSpace(newNameKey))
            {
                return null;
            }

            var candidates = existingSettlements
                .Where(x => NormalizeNameKey(x.Name) == newNameKey)
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            if (newSettlement.OsmId > 0)
            {
                var exactByOsm = candidates.FirstOrDefault(x =>
                    x.OsmId == newSettlement.OsmId &&
                    string.Equals(
                        x.OsmType,
                        newSettlement.OsmType,
                        StringComparison.OrdinalIgnoreCase));

                if (exactByOsm != null)
                {
                    return exactByOsm;
                }
            }

            if (TryGetFallbackPoint(newSettlement, out var newPoint))
            {
                SettlementGeometryItem? best = null;
                double bestDistance = double.MaxValue;

                foreach (var candidate in candidates)
                {
                    if (!TryGetFallbackPoint(candidate, out var candidatePoint))
                    {
                        continue;
                    }

                    var distance = GetDistance(newPoint, candidatePoint);

                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = candidate;
                    }
                }

                if (best != null &&
                    bestDistance <= MaxPreservedGeometryAttachDistanceMeters)
                {
                    return best;
                }
            }

            return candidates.Count == 1
                ? candidates[0]
                : null;
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

        public bool TryFindSettlement(PointF point, out SettlementSearchResult result)
        {
            result = new SettlementSearchResult();

            if (_cache.Settlements == null || _cache.Settlements.Count == 0)
            {
                return false;
            }

            // 1. Перший прохід:
            // якщо точка всередині полігона НП — це головний результат.
            SettlementSearchResult? bestInside = null;
            double bestInsideArea = double.MaxValue;

            foreach (var settlement in _cache.Settlements)
            {
                if (!IsUsableSettlement(settlement))
                {
                    continue;
                }

                var polygonResult = AnalyzePolygons(settlement, point);

                if (!polygonResult.HasPolygon || !polygonResult.IsInside)
                {
                    continue;
                }

                if (polygonResult.SmallestContainingArea < bestInsideArea)
                {
                    bestInsideArea = polygonResult.SmallestContainingArea;

                    bestInside = new SettlementSearchResult
                    {
                        Name = settlement.Name.Trim(),
                        Place = settlement.Place.Trim(),
                        DistanceMeters = 0,
                        IsInsidePolygon = true
                    };
                }
            }

            if (bestInside != null)
            {
                result = bestInside;
                return true;
            }

            // 2. Другий прохід:
            // якщо точка не всередині жодного полігона,
            // порівнюємо:
            // - відстань до меж полігонів;
            // - відстань до fallbackPoint для НП без полігона.
            SettlementSearchResult? bestNearest = null;

            foreach (var settlement in _cache.Settlements)
            {
                if (!IsUsableSettlement(settlement))
                {
                    continue;
                }

                var polygonResult = AnalyzePolygons(settlement, point);

                if (polygonResult.HasPolygon)
                {
                    if (double.IsInfinity(polygonResult.DistanceMeters) ||
                        polygonResult.DistanceMeters == double.MaxValue)
                    {
                        continue;
                    }

                    var candidate = new SettlementSearchResult
                    {
                        Name = settlement.Name.Trim(),
                        Place = settlement.Place.Trim(),
                        DistanceMeters = polygonResult.DistanceMeters,
                        IsInsidePolygon = false
                    };

                    if (bestNearest == null ||
                        candidate.DistanceMeters < bestNearest.DistanceMeters)
                    {
                        bestNearest = candidate;
                    }

                    continue;
                }

                if (settlement.FallbackPoint != null)
                {
                    var fallbackDistance = GetDistance(
                        point,
                        new PointF(settlement.FallbackPoint.X, settlement.FallbackPoint.Y)
                    );

                    var candidate = new SettlementSearchResult
                    {
                        Name = settlement.Name.Trim(),
                        Place = settlement.Place.Trim(),
                        DistanceMeters = fallbackDistance,
                        IsInsidePolygon = false
                    };

                    if (bestNearest == null ||
                        candidate.DistanceMeters < bestNearest.DistanceMeters)
                    {
                        bestNearest = candidate;
                    }
                }
            }

            if (bestNearest != null)
            {
                result = bestNearest;
                return true;
            }

            return false;
        }

        private static PolygonAnalyzeResult AnalyzePolygons(
     SettlementGeometryItem settlement,
     PointF point)
        {
            var result = new PolygonAnalyzeResult();

            if (settlement.Polygons == null || settlement.Polygons.Count == 0)
            {
                return result;
            }

            foreach (var polygonLine in settlement.Polygons)
            {
                var polygon = ParsePolygonLine(polygonLine);

                if (polygon.Count < 3)
                {
                    continue;
                }

                result.HasPolygon = true;

                if (IsPointInsidePolygon(point, polygon))
                {
                    result.IsInside = true;

                    var area = Math.Abs(GetPolygonArea(polygon));

                    if (area < result.SmallestContainingArea)
                    {
                        result.SmallestContainingArea = area;
                    }

                    continue;
                }

                var distance = GetDistanceToPolygon(point, polygon);

                if (distance < result.DistanceMeters)
                {
                    result.DistanceMeters = distance;
                }
            }

            return result;
        }

        public static List<PointF> ParsePolygonLine(string? polygonLine)
        {
            var result = new List<PointF>();

            if (string.IsNullOrWhiteSpace(polygonLine))
            {
                return result;
            }

            var pointParts = polygonLine.Split(
                ';',
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
            );

            foreach (var pointPart in pointParts)
            {
                var xy = pointPart.Split(
                    ',',
                    StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries
                );

                if (xy.Length != 2)
                {
                    continue;
                }

                if (!float.TryParse(
                        xy[0],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var x))
                {
                    continue;
                }

                if (!float.TryParse(
                        xy[1],
                        NumberStyles.Float,
                        CultureInfo.InvariantCulture,
                        out var y))
                {
                    continue;
                }

                result.Add(new PointF(x, y));
            }

            return result;
        }

        public static string ToPolygonLine(IEnumerable<PointF> points)
        {
            if (points == null)
            {
                return string.Empty;
            }

            return string.Join(
                ";",
                points.Select(p =>
                    p.X.ToString("0.###", CultureInfo.InvariantCulture) +
                    "," +
                    p.Y.ToString("0.###", CultureInfo.InvariantCulture))
            );
        }

        private static bool IsPointInsidePolygon(PointF point, List<PointF> polygon)
        {
            var inside = false;

            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                var pi = polygon[i];
                var pj = polygon[j];

                var intersects =
                    ((pi.Y > point.Y) != (pj.Y > point.Y)) &&
                    (point.X < (pj.X - pi.X) * (point.Y - pi.Y) / ((pj.Y - pi.Y) + 0.000001f) + pi.X);

                if (intersects)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private static double GetDistanceToPolygon(PointF point, List<PointF> polygon)
        {
            var minDistance = double.MaxValue;

            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];

                var distance = GetDistanceToSegment(point, a, b);

                if (distance < minDistance)
                {
                    minDistance = distance;
                }
            }

            return minDistance;
        }

        private static double GetDistanceToSegment(PointF point, PointF a, PointF b)
        {
            var dx = b.X - a.X;
            var dy = b.Y - a.Y;

            if (Math.Abs(dx) < 0.000001f && Math.Abs(dy) < 0.000001f)
            {
                return GetDistance(point, a);
            }

            var t =
                ((point.X - a.X) * dx + (point.Y - a.Y) * dy) /
                (dx * dx + dy * dy);

            t = Math.Max(0, Math.Min(1, t));

            var closest = new PointF(
                a.X + t * dx,
                a.Y + t * dy
            );

            return GetDistance(point, closest);
        }

        private static double GetDistance(PointF a, PointF b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;

            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static double GetPolygonArea(List<PointF> polygon)
        {
            double area = 0;

            for (var i = 0; i < polygon.Count; i++)
            {
                var a = polygon[i];
                var b = polygon[(i + 1) % polygon.Count];

                area += (a.X * b.Y) - (b.X * a.Y);
            }

            return area / 2.0;
        }

        private static bool IsUsableSettlement(SettlementGeometryItem settlement)
        {
            if (settlement == null)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(settlement.Name))
            {
                return false;
            }

            var name = settlement.Name.Trim().ToLowerInvariant();

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

        private sealed class PolygonAnalyzeResult
        {
            public bool HasPolygon { get; set; }

            public bool IsInside { get; set; }

            public double DistanceMeters { get; set; } = double.MaxValue;

            public double SmallestContainingArea { get; set; } = double.MaxValue;
        }
    }
}