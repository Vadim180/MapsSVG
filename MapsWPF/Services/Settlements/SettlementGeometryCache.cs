using System;
using System.Collections.Generic;

namespace MapsWPF.Services.Settlements
{
    public sealed class SettlementGeometryCache
    {
        public const int CurrentSchemaVersion = 3;

        public int SchemaVersion { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string Source { get; set; } = "local";

        public List<string> Sources { get; set; } = new();

        public string CoordinateSystem { get; set; } = "UTM";

        public int UtmZone { get; set; } = 37;

        public string UtmBand { get; set; } = "U";

        public SettlementCacheBounds? Bounds { get; set; }

        public List<SettlementGeometryItem> Settlements { get; set; } = new();
    }

    public sealed class SettlementCacheBounds
    {
        public double Top { get; set; }

        public double Bottom { get; set; }

        public double Left { get; set; }

        public double Right { get; set; }

        public double PaddingKm { get; set; }
    }

    public sealed class SettlementGeometryItem
    {
        public string Name { get; set; } = string.Empty;

        // Усі відомі варіанти назви (name, name:uk, name:en, alt_name тощо).
        // Вони використовуються лише разом із координатами та ідентифікаторами,
        // тому однойменні сусідні НП не зливаються автоматично.
        public List<string> NameAliases { get; set; } = new();

        public string Place { get; set; } = string.Empty;

        public string Boundary { get; set; } = string.Empty;

        public string AdminLevel { get; set; } = string.Empty;

        public string CountryCode { get; set; } = string.Empty;

        public string Region { get; set; } = string.Empty;

        public string District { get; set; } = string.Empty;

        public string OsmType { get; set; } = string.Empty;

        public long OsmId { get; set; }

        public string GeometrySourceOsmType { get; set; } = string.Empty;

        public long GeometrySourceOsmId { get; set; }

        // Один рядок = один полігон:
        // "399627,5510807;398868,5510245;397675,5509486"
        public List<string> Polygons { get; set; } = new();

        public SettlementPoint? FallbackPoint { get; set; }

        public List<SettlementProviderReference> ProviderReferences { get; set; } = new();

        public List<SettlementCenterCandidate> CenterCandidates { get; set; } = new();

        public List<SettlementPolygonCandidate> PolygonCandidates { get; set; } = new();
    }

    public sealed class SettlementProviderReference
    {
        public string Provider { get; set; } = string.Empty;

        public string ExternalId { get; set; } = string.Empty;

        public string FeatureType { get; set; } = string.Empty;
    }

    public sealed class SettlementCenterCandidate
    {
        public string Provider { get; set; } = string.Empty;

        public string ExternalId { get; set; } = string.Empty;

        public int Priority { get; set; }

        public int QualityScore { get; set; }

        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public SettlementPoint Point { get; set; } = new();
    }

    public sealed class SettlementPolygonCandidate
    {
        public string Provider { get; set; } = string.Empty;

        public string ExternalId { get; set; } = string.Empty;

        public int Priority { get; set; }

        public int QualityScore { get; set; }

        public double AreaSquareKilometers { get; set; }

        public int VertexCount { get; set; }

        public bool ContainsCenter { get; set; }

        // Relation може прямо посилатися на node НП роллю label/admin_centre.
        // Це сильніший зв'язок, ніж збіг назви або близькість bbox-center.
        public bool HasExplicitSettlementLink { get; set; }

        // Для діагностики неточних OSM-меж: 0 означає, що центр усередині.
        public double? CenterDistanceMeters { get; set; }

        public bool AcceptedByExplicitSettlementLink { get; set; }

        // Захист від полігона, який насправді є великою територією
        // та охоплює кілька інших населених пунктів.
        public int ContainedSettlementCount { get; set; }

        public bool RejectedBySettlementContainmentGuard { get; set; }

        // Загальна жорстка перевірка геометрії: власний центр, площа,
        // коректність кілець і самоперетини. Вона відокремлена від перевірки
        // інших НП, щоб у кеші було видно точну причину відхилення.
        public bool RejectedByGeometryGuard { get; set; }

        public string RejectionReason { get; set; } = string.Empty;

        // UTM-зона належить конкретній геометрії, а не всьому кешу.
        // Полігон може бути безпечно збережений поруч із полігонами інших зон.
        public int UtmZone { get; set; }

        public string UtmBand { get; set; } = string.Empty;

        // Внутрішні кільця GeoJSON/OSM (озера, анклави, виключені території).
        public List<string> InteriorRings { get; set; } = new();

        public string Boundary { get; set; } = string.Empty;

        public string AdminLevel { get; set; } = string.Empty;

        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        public string Polygon { get; set; } = string.Empty;
    }

    public sealed class SettlementPoint
    {
        public float X { get; set; }

        public float Y { get; set; }

        public int UtmZone { get; set; }

        public string UtmBand { get; set; } = string.Empty;

        // WGS84 дублюється лише для надійного зіставлення між різними
        // UTM-зонами. Основними координатами кешу залишаються UTM X/Y.
        public double? Latitude { get; set; }

        public double? Longitude { get; set; }

        public SettlementPoint()
        {
        }

        public SettlementPoint(float x, float y)
        {
            X = x;
            Y = y;
        }

        public SettlementPoint(
            float x,
            float y,
            int utmZone,
            string? utmBand,
            double? latitude = null,
            double? longitude = null)
        {
            X = x;
            Y = y;
            UtmZone = utmZone;
            UtmBand = utmBand ?? string.Empty;
            Latitude = latitude;
            Longitude = longitude;
        }
    }

    public sealed class SettlementPolygonGeometry
    {
        public string OuterRing { get; set; } = string.Empty;

        public List<string> InteriorRings { get; set; } = new();

        public int UtmZone { get; set; }

        public string UtmBand { get; set; } = string.Empty;
    }
}
