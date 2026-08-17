using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using System.Globalization;

namespace MapsWPF.Services.Settlements
{
    public sealed class SettlementGeometryService
    {
        private const string CacheFileName = "settlements_geometry_cache.json";
        private const string CacheBackupFileName =
            "settlements_geometry_cache.backup.json";
        private const string CacheTemporaryFileName =
            "settlements_geometry_cache.tmp";

        private readonly string _settingsFolderPath;
        private readonly string _cacheFilePath;
        private readonly string _cacheBackupFilePath;
        private readonly string _cacheTemporaryFilePath;
        private readonly object _cacheSync = new();

        private SettlementGeometryCache? _cache;
        private int? _knownSettlementCount;

        public SettlementGeometryService()
            : this(Path.Combine(
                Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData),
                "MapsWPF",
                "settings"))
        {
        }

        public SettlementGeometryService(string settingsFolderPath)
        {
            if (string.IsNullOrWhiteSpace(settingsFolderPath))
            {
                throw new ArgumentException(
                    "Папку кешу не вказано.",
                    nameof(settingsFolderPath));
            }

            _settingsFolderPath = Path.GetFullPath(settingsFolderPath);

            _cacheFilePath = Path.Combine(_settingsFolderPath, CacheFileName);
            _cacheBackupFilePath = Path.Combine(
                _settingsFolderPath,
                CacheBackupFileName);
            _cacheTemporaryFilePath = Path.Combine(
                _settingsFolderPath,
                CacheTemporaryFileName);
        }

        public string CacheFilePath => _cacheFilePath;

        public string CacheBackupFilePath => _cacheBackupFilePath;

        public string LastLoadWarning { get; private set; } = string.Empty;

        public string LastSaveError { get; private set; } = string.Empty;

        public bool IsMemoryCacheLoaded
        {
            get
            {
                lock (_cacheSync)
                {
                    return _cache != null;
                }
            }
        }

        public bool HasSettlements
        {
            get
            {
                lock (_cacheSync)
                {
                    if (_cache != null)
                    {
                        return _cache.Settlements?.Count > 0;
                    }

                    if (_knownSettlementCount.HasValue)
                    {
                        return _knownSettlementCount.Value > 0;
                    }

                    return File.Exists(_cacheFilePath) ||
                           File.Exists(_cacheBackupFilePath);
                }
            }
        }

        public int SettlementCount
        {
            get
            {
                lock (_cacheSync)
                {
                    return _cache?.Settlements?.Count ??
                           _knownSettlementCount ??
                           0;
                }
            }
        }

        public SettlementGeometryCache CurrentCache
        {
            get
            {
                lock (_cacheSync)
                {
                    return EnsureLoadedCore();
                }
            }
        }

        public void Load()
        {
            lock (_cacheSync)
            {
                _cache = null;
                EnsureLoadedCore();
            }
        }

        public void ReleaseMemory()
        {
            lock (_cacheSync)
            {
                if (_cache != null)
                {
                    _knownSettlementCount = _cache.Settlements?.Count ?? 0;
                }

                _cache = null;
            }
        }

        public bool Save(SettlementGeometryCache cache)
        {
            if (cache == null)
            {
                LastSaveError = "Кеш для збереження не передано.";
                return false;
            }

            lock (_cacheSync)
            {
                try
                {
                    Directory.CreateDirectory(_settingsFolderPath);

                    SettlementGeometryMerger.Normalize(cache);
                    cache.SchemaVersion =
                        SettlementGeometryCache.CurrentSchemaVersion;
                    cache.CreatedAt = DateTime.Now;

                    var json = JsonConvert.SerializeObject(
                        cache,
                        Formatting.Indented);

                    WriteTemporaryCacheFile(json);
                    ReplaceCacheFileAtomically();

                    _cache = cache;
                    _knownSettlementCount = cache.Settlements?.Count ?? 0;
                    LastSaveError = string.Empty;
                    return true;
                }
                catch (Exception ex)
                {
                    LastSaveError = ex.Message;
                    Debug.WriteLine($"[SETTLEMENT CACHE] Save error: {ex}");
                    return false;
                }
                finally
                {
                    TryDeleteTemporaryCacheFile();
                }
            }
        }

        private SettlementGeometryCache EnsureLoadedCore()
        {
            if (_cache != null)
            {
                return _cache;
            }

            LastLoadWarning = string.Empty;

            if (TryReadCacheFile(
                    _cacheFilePath,
                    out var loaded,
                    out var primaryError))
            {
                _cache = loaded;
                _knownSettlementCount = loaded.Settlements?.Count ?? 0;
                return loaded;
            }

            if (TryReadCacheFile(
                    _cacheBackupFilePath,
                    out var backup,
                    out var backupError))
            {
                _cache = backup;
                _knownSettlementCount = backup.Settlements?.Count ?? 0;
                LastLoadWarning =
                    "Основний кеш пошкоджений або недоступний. " +
                    "Дані відновлено з резервної копії.";

                Debug.WriteLine(
                    $"[SETTLEMENT CACHE] Primary load error: {primaryError}");
                return backup;
            }

            if (!string.IsNullOrWhiteSpace(primaryError) ||
                !string.IsNullOrWhiteSpace(backupError))
            {
                LastLoadWarning =
                    "Не вдалося прочитати основний або резервний кеш. " +
                    "Використовується порожній кеш.";

                Debug.WriteLine(
                    $"[SETTLEMENT CACHE] Primary: {primaryError}; " +
                    $"backup: {backupError}");
            }

            _cache = new SettlementGeometryCache();
            SettlementGeometryMerger.Normalize(_cache);
            _knownSettlementCount = 0;
            return _cache;
        }

        private static bool TryReadCacheFile(
            string path,
            out SettlementGeometryCache cache,
            out string error)
        {
            cache = new SettlementGeometryCache();
            error = string.Empty;

            if (!File.Exists(path))
            {
                return false;
            }

            try
            {
                var json = File.ReadAllText(path);
                var loaded = JsonConvert.DeserializeObject<SettlementGeometryCache>(
                    json) ?? throw new InvalidDataException(
                    "Файл кешу не містить даних.");

                if (loaded.SchemaVersion >
                    SettlementGeometryCache.CurrentSchemaVersion)
                {
                    throw new InvalidDataException(
                        $"Версія кешу {loaded.SchemaVersion} новіша за " +
                        $"підтримувану " +
                        $"{SettlementGeometryCache.CurrentSchemaVersion}.");
                }

                loaded.Settlements ??= new List<SettlementGeometryItem>();
                SettlementGeometryMerger.Normalize(loaded);
                cache = loaded;
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private void WriteTemporaryCacheFile(string json)
        {
            using var stream = new FileStream(
                _cacheTemporaryFilePath,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None,
                64 * 1024,
                FileOptions.WriteThrough);
            using var writer = new StreamWriter(
                stream,
                new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

            writer.Write(json);
            writer.Flush();
            stream.Flush(flushToDisk: true);
        }

        private void ReplaceCacheFileAtomically()
        {
            if (File.Exists(_cacheFilePath))
            {
                File.Replace(
                    _cacheTemporaryFilePath,
                    _cacheFilePath,
                    _cacheBackupFilePath,
                    ignoreMetadataErrors: true);
                return;
            }

            File.Move(_cacheTemporaryFilePath, _cacheFilePath);
        }

        private void TryDeleteTemporaryCacheFile()
        {
            try
            {
                if (File.Exists(_cacheTemporaryFilePath))
                {
                    File.Delete(_cacheTemporaryFilePath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine(
                    $"[SETTLEMENT CACHE] Temp cleanup error: {ex.Message}");
            }
        }

        public SettlementGeometryCache SavePreservingExistingGeometry(
            SettlementGeometryCache cache)
        {
            return MergePreservingExistingGeometry(cache, persistToDisk: true);
        }

        public SettlementGeometryCache SaveReplacingProviderGeometry(
            SettlementGeometryCache cache,
            IEnumerable<string> providersToReplace)
        {
            if (cache == null)
            {
                return CurrentCache;
            }

            try
            {
                var existing = CurrentCache;
                var merged = SettlementGeometryMerger.MergeReplacingProviders(
                    existing,
                    cache,
                    providersToReplace);

                return Save(merged) ? merged : existing;
            }
            catch (Exception ex)
            {
                LastSaveError = ex.Message;
                Debug.WriteLine(
                    $"[SETTLEMENT CACHE] Provider replacement error: {ex}");
                return CurrentCache;
            }
        }

        public SettlementGeometryCache MergePreservingExistingGeometry(
            SettlementGeometryCache cache,
            bool persistToDisk)
        {
            if (cache == null)
            {
                return CurrentCache;
            }

            try
            {
                var merged = SettlementGeometryMerger.Merge(
                    CurrentCache,
                    cache);

                if (persistToDisk)
                {
                    if (!Save(merged))
                    {
                        lock (_cacheSync)
                        {
                            _cache = merged;
                            _knownSettlementCount =
                                merged.Settlements?.Count ?? 0;
                        }
                    }
                }
                else
                {
                    lock (_cacheSync)
                    {
                        _cache = merged;
                        _knownSettlementCount =
                            merged.Settlements?.Count ?? 0;
                    }
                }

                return merged;
            }
            catch (Exception ex)
            {
                LastSaveError = ex.Message;
                Debug.WriteLine($"[SETTLEMENT CACHE] Save preserving error: {ex}");
                return CurrentCache;
            }
        }

        public bool TryFindSettlement(PointF point, out SettlementSearchResult result)
        {
            var releaseAfterSearch = !IsMemoryCacheLoaded;
            var cache = CurrentCache;

            try
            {
                return TryFindSettlement(
                    new SettlementPoint(
                        point.X,
                        point.Y,
                        cache.UtmZone,
                        cache.UtmBand),
                    cache,
                    out result);
            }
            finally
            {
                if (releaseAfterSearch)
                {
                    ReleaseMemory();
                }
            }
        }

        public bool TryFindSettlement(
            PointF point,
            int utmZone,
            string? utmBand,
            out SettlementSearchResult result)
        {
            var releaseAfterSearch = !IsMemoryCacheLoaded;
            var cache = CurrentCache;

            try
            {
                return TryFindSettlement(
                    new SettlementPoint(
                        point.X,
                        point.Y,
                        utmZone,
                        utmBand),
                    cache,
                    out result);
            }
            finally
            {
                if (releaseAfterSearch)
                {
                    ReleaseMemory();
                }
            }
        }

        private bool TryFindSettlement(
            SettlementPoint point,
            SettlementGeometryCache cache,
            out SettlementSearchResult result)
        {
            result = new SettlementSearchResult();

            if (cache.Settlements == null || cache.Settlements.Count == 0)
            {
                return false;
            }

            // 1. Перший прохід:
            // якщо точка всередині полігона НП — це головний результат.
            SettlementSearchResult? bestInside = null;
            double bestInsideArea = double.MaxValue;

            foreach (var settlement in cache.Settlements)
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

            foreach (var settlement in cache.Settlements)
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
                    var fallbackDistance =
                        SettlementUtmProjection.GetDistanceMeters(
                            point,
                            settlement.FallbackPoint);

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
            SettlementPoint point)
        {
            var result = new PolygonAnalyzeResult();

            var candidates = SettlementGeometryQualitySelector
                .GetPreferredPolygonCandidates(settlement);

            if (candidates.Count == 0)
            {
                return result;
            }

            foreach (var candidate in candidates)
            {
                var polygon = ParsePolygonLine(candidate.Polygon);

                if (polygon.Count < 3 ||
                    !SettlementUtmProjection.TryReproject(
                        point,
                        candidate.UtmZone,
                        out var projectedPoint))
                {
                    continue;
                }

                var holes = (candidate.InteriorRings ?? new List<string>())
                    .Select(ParsePolygonLine)
                    .Where(x => x.Count >= 3)
                    .ToList();

                result.HasPolygon = true;

                var insideOuter = IsPointInsidePolygon(
                    projectedPoint,
                    polygon);
                var insideHole = holes.Any(hole =>
                    IsPointInsidePolygon(projectedPoint, hole));

                if (insideOuter && !insideHole)
                {
                    result.IsInside = true;

                    var area = Math.Max(
                        0.0,
                        Math.Abs(GetPolygonArea(polygon)) -
                        holes.Sum(hole => Math.Abs(GetPolygonArea(hole))));

                    if (area < result.SmallestContainingArea)
                    {
                        result.SmallestContainingArea = area;
                    }

                    continue;
                }

                var distance = GetDistanceToPolygon(
                    projectedPoint,
                    polygon);

                foreach (var hole in holes)
                {
                    distance = Math.Min(
                        distance,
                        GetDistanceToPolygon(projectedPoint, hole));
                }

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
