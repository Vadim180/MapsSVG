using MapsWPF.Services.Settlements;
using Newtonsoft.Json.Linq;

namespace MapsWPF.Tests;

public sealed class SettlementOverpassLoaderTests
{
    [Fact]
    public void OsmApiFullJson_IsAcceptedByRelationGeometryParser()
    {
        const string json =
            """
            {
              "elements": [
                {"type":"node","id":1,"lat":49.0,"lon":36.0},
                {"type":"node","id":2,"lat":49.0,"lon":36.01},
                {"type":"node","id":3,"lat":49.01,"lon":36.01},
                {"type":"node","id":4,"lat":49.01,"lon":36.0},
                {"type":"way","id":10,"nodes":[1,2,3,4,1]},
                {
                  "type":"relation",
                  "id":100,
                  "members":[
                    {"type":"way","ref":10,"role":"outer"},
                    {"type":"node","ref":99,"role":"label"}
                  ]
                }
              ]
            }
            """;

        var polygons = SettlementOverpassLoader.ParseRelationPolygons(
            json,
            100);

        var polygon = Assert.Single(polygons);
        Assert.False(string.IsNullOrWhiteSpace(polygon.OuterRing));
        Assert.Equal(37, polygon.UtmZone);
    }

    [Fact]
    public void RelationLabelAndAdminCentreNodeIds_AreReadExactly()
    {
        var relation = JObject.Parse(
            """
            {
              "members": [
                {"type":"way","ref":10,"role":"outer"},
                {"type":"node","ref":337547188,"role":"label"},
                {"type":"node","ref":42,"role":"admin_centre"},
                {"type":"node","ref":7,"role":"stop"}
              ]
            }
            """);

        var nodeIds = SettlementOverpassLoader.ReadSettlementLabelNodeIds(
            relation);

        Assert.Equal(new long[] { 337547188, 42 }, nodeIds);
    }
}
