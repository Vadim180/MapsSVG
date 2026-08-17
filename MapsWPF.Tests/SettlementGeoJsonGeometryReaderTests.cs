using MapsWPF.Services.Settlements;
using Newtonsoft.Json.Linq;

namespace MapsWPF.Tests;

public sealed class SettlementGeoJsonGeometryReaderTests
{
    [Fact]
    public void Polygon_PreservesInteriorRing()
    {
        var geoJson = JToken.Parse(
            """
            {
              "type": "Polygon",
              "coordinates": [
                [[36.0,49.0],[36.02,49.0],[36.02,49.02],[36.0,49.02],[36.0,49.0]],
                [[36.005,49.005],[36.015,49.005],[36.015,49.015],[36.005,49.015],[36.005,49.005]]
              ]
            }
            """);

        var polygons = SettlementGeoJsonGeometryReader.ReadPolygons(
            geoJson,
            TryConvert);

        var polygon = Assert.Single(polygons);
        Assert.False(string.IsNullOrWhiteSpace(polygon.OuterRing));
        Assert.Single(polygon.InteriorRings);
        Assert.Equal(37, polygon.UtmZone);
        Assert.Equal("U", polygon.UtmBand);
    }

    [Fact]
    public void MultiPolygon_PreservesEveryPolygonPart()
    {
        var geoJson = JToken.Parse(
            """
            {
              "type": "MultiPolygon",
              "coordinates": [
                [[[36.0,49.0],[36.01,49.0],[36.01,49.01],[36.0,49.01],[36.0,49.0]]],
                [[[36.02,49.0],[36.03,49.0],[36.03,49.01],[36.02,49.01],[36.02,49.0]]]
              ]
            }
            """);

        var polygons = SettlementGeoJsonGeometryReader.ReadPolygons(
            geoJson,
            TryConvert);

        Assert.Equal(2, polygons.Count);
        Assert.All(polygons, polygon =>
            Assert.False(string.IsNullOrWhiteSpace(polygon.OuterRing)));
    }

    private static bool TryConvert(
        double latitude,
        double longitude,
        out System.Drawing.PointF utm,
        out int zone,
        out string band)
    {
        zone = 37;
        band = "U";
        utm = System.Drawing.PointF.Empty;

        if (!SettlementUtmProjection.TryProject(
                latitude,
                longitude,
                zone,
                out var projected))
        {
            return false;
        }

        utm = new System.Drawing.PointF(projected.X, projected.Y);
        return true;
    }
}
