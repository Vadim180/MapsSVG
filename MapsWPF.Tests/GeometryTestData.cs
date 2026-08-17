using System.Globalization;
using MapsWPF.Services.Settlements;

namespace MapsWPF.Tests;

internal static class GeometryTestData
{
    public const int Zone = 37;
    public const string Band = "U";

    public static SettlementPoint Point(float x, float y)
    {
        return new SettlementPoint(x, y, Zone, Band);
    }

    public static string Ring(params (float X, float Y)[] points)
    {
        return string.Join(
            ";",
            points.Select(point =>
                point.X.ToString("0.###", CultureInfo.InvariantCulture) +
                "," +
                point.Y.ToString("0.###", CultureInfo.InvariantCulture)));
    }

    public static SettlementGeometryItem Settlement(
        string name,
        long osmId,
        SettlementPoint center,
        string place = "village")
    {
        return new SettlementGeometryItem
        {
            Name = name,
            Place = place,
            OsmType = "node",
            OsmId = osmId,
            FallbackPoint = center,
            NameAliases = new List<string> { name },
            ProviderReferences = new List<SettlementProviderReference>
            {
                new()
                {
                    Provider = SettlementDataSources.OpenStreetMapOverpass,
                    ExternalId = osmId.ToString(CultureInfo.InvariantCulture),
                    FeatureType = "node"
                }
            },
            CenterCandidates = new List<SettlementCenterCandidate>
            {
                new()
                {
                    Provider = SettlementDataSources.OpenStreetMapOverpass,
                    ExternalId = osmId.ToString(CultureInfo.InvariantCulture),
                    Priority = SettlementDataSources.OpenStreetMapPriority,
                    Point = center
                }
            }
        };
    }

    public static SettlementPolygonCandidate Polygon(
        string outerRing,
        params string[] holes)
    {
        return new SettlementPolygonCandidate
        {
            Provider = SettlementDataSources.OpenStreetMapOverpass,
            ExternalId = "relation-1",
            Priority = SettlementDataSources.OpenStreetMapPriority,
            Polygon = outerRing,
            InteriorRings = holes.ToList(),
            UtmZone = Zone,
            UtmBand = Band
        };
    }
}
