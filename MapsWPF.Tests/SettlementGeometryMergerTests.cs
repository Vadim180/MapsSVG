using MapsWPF.Services.Settlements;

namespace MapsWPF.Tests;

public sealed class SettlementGeometryMergerTests
{
    [Fact]
    public void SameNamedNearbySettlements_AreMatchedToNearestUnambiguousCenter()
    {
        var first = GeometryTestData.Settlement(
            "Ковалівка",
            101,
            GeometryTestData.Point(400_000, 5_500_000));
        first.Region = "Харківська область";
        first.District = "Тестовий район";

        var second = GeometryTestData.Settlement(
            "Ковалівка",
            202,
            GeometryTestData.Point(404_000, 5_500_000));
        second.Region = first.Region;
        second.District = first.District;

        var incoming = new SettlementGeometryItem
        {
            Name = "Ковалівка",
            NameAliases = new List<string> { "Ковалівка" },
            Place = "village",
            Region = first.Region,
            District = first.District,
            FallbackPoint = GeometryTestData.Point(404_050, 5_500_000),
            ProviderReferences = new List<SettlementProviderReference>
            {
                new()
                {
                    Provider = "Test provider",
                    ExternalId = "nearest-kovalivka",
                    FeatureType = "locality"
                }
            },
            CenterCandidates = new List<SettlementCenterCandidate>
            {
                new()
                {
                    Provider = "Test provider",
                    ExternalId = "nearest-kovalivka",
                    Priority = 80,
                    Point = GeometryTestData.Point(404_050, 5_500_000)
                }
            }
        };

        var merged = SettlementGeometryMerger.Merge(
            new SettlementGeometryCache
            {
                Settlements = new List<SettlementGeometryItem>
                {
                    first,
                    second
                }
            },
            new SettlementGeometryCache
            {
                Settlements = new List<SettlementGeometryItem> { incoming }
            });

        Assert.Equal(2, merged.Settlements.Count);

        var mergedFirst = Assert.Single(
            merged.Settlements,
            x => x.OsmId == 101);
        var mergedSecond = Assert.Single(
            merged.Settlements,
            x => x.OsmId == 202);

        Assert.DoesNotContain(
            mergedFirst.ProviderReferences,
            x => x.ExternalId == "nearest-kovalivka");
        Assert.Contains(
            mergedSecond.ProviderReferences,
            x => x.ExternalId == "nearest-kovalivka");
    }

    [Fact]
    public void SameNameWithoutCoordinatesOrUniqueContext_DoesNotMerge()
    {
        var existing = new SettlementGeometryItem
        {
            Name = "Ковалівка",
            Place = "village"
        };
        var incoming = new SettlementGeometryItem
        {
            Name = "Ковалівка",
            Place = "village"
        };

        var merged = SettlementGeometryMerger.Merge(
            new SettlementGeometryCache
            {
                Settlements = new List<SettlementGeometryItem> { existing }
            },
            new SettlementGeometryCache
            {
                Settlements = new List<SettlementGeometryItem> { incoming }
            });

        Assert.Equal(2, merged.Settlements.Count);
    }

    [Fact]
    public void ForceRefresh_ReplacesChangedGeometryButPreservesFailedProviderData()
    {
        const string failedProvider = "Provider with temporary failure";
        var settlement = GeometryTestData.Settlement(
            "Тестове",
            101,
            GeometryTestData.Point(400_500, 5_500_500));
        var oldOsmPolygon = GeometryTestData.Ring(
            (400_000, 5_500_000),
            (401_000, 5_500_000),
            (401_000, 5_501_000),
            (400_000, 5_501_000));
        var newOsmPolygon = GeometryTestData.Ring(
            (399_900, 5_499_900),
            (401_100, 5_499_900),
            (401_100, 5_501_100),
            (399_900, 5_501_100));
        var failedProviderPolygon = GeometryTestData.Ring(
            (400_100, 5_500_100),
            (400_900, 5_500_100),
            (400_900, 5_500_900),
            (400_100, 5_500_900));

        SettlementGeometryMerger.AddPolygonCandidate(
            settlement,
            oldOsmPolygon,
            SettlementDataSources.OpenStreetMapOverpass,
            "osm-relation",
            SettlementDataSources.OpenStreetMapPriority,
            GeometryTestData.Zone,
            GeometryTestData.Band);
        SettlementGeometryMerger.AddPolygonCandidate(
            settlement,
            failedProviderPolygon,
            failedProvider,
            "failed-provider-id",
            80,
            GeometryTestData.Zone,
            GeometryTestData.Band);

        var existing = new SettlementGeometryCache
        {
            Settlements = new List<SettlementGeometryItem> { settlement }
        };
        var incoming = SettlementGeometryMerger.CloneCache(existing);
        var refreshedSettlement = Assert.Single(incoming.Settlements);

        SettlementGeometryMerger.RemoveProviderCandidates(
            refreshedSettlement,
            SettlementDataSources.OpenStreetMapOverpass,
            removeCenters: false,
            removePolygons: true);
        SettlementGeometryMerger.AddPolygonCandidate(
            refreshedSettlement,
            newOsmPolygon,
            SettlementDataSources.OpenStreetMapOverpass,
            "osm-relation",
            SettlementDataSources.OpenStreetMapPriority,
            GeometryTestData.Zone,
            GeometryTestData.Band);

        var merged = SettlementGeometryMerger.MergeReplacingProviders(
            existing,
            incoming,
            new[]
            {
                SettlementDataSources.OpenStreetMapOverpass,
                failedProvider
            });
        var result = Assert.Single(merged.Settlements);

        Assert.DoesNotContain(
            result.PolygonCandidates,
            candidate => candidate.Polygon == oldOsmPolygon);
        Assert.Contains(
            result.PolygonCandidates,
            candidate => candidate.Polygon == newOsmPolygon);
        Assert.Contains(
            result.PolygonCandidates,
            candidate =>
                candidate.Provider == failedProvider &&
                candidate.Polygon == failedProviderPolygon);
    }
}
