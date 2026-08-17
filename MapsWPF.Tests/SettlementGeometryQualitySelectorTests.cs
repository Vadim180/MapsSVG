using MapsWPF.Services.Settlements;

namespace MapsWPF.Tests;

public sealed class SettlementGeometryQualitySelectorTests
{
    [Fact]
    public void ValidPolygonContainingOwnCenter_IsSelected()
    {
        var settlement = GeometryTestData.Settlement(
            "Тестове",
            1,
            GeometryTestData.Point(400_500, 5_500_500));
        var candidate = GeometryTestData.Polygon(
            GeometryTestData.Ring(
                (400_000, 5_500_000),
                (401_000, 5_500_000),
                (401_000, 5_501_000),
                (400_000, 5_501_000)));
        settlement.PolygonCandidates.Add(candidate);

        SettlementGeometryQualitySelector.RecalculateAll(new[] { settlement });

        Assert.False(candidate.RejectedByGeometryGuard);
        Assert.False(candidate.RejectedBySettlementContainmentGuard);
        Assert.True(candidate.ContainsCenter);
        Assert.Single(
            SettlementGeometryQualitySelector.GetPreferredPolygonCandidates(
                settlement));
    }

    [Fact]
    public void PolygonOutsideOwnCenter_IsRejected()
    {
        var settlement = GeometryTestData.Settlement(
            "Тестове",
            1,
            GeometryTestData.Point(405_000, 5_505_000));
        var candidate = GeometryTestData.Polygon(
            GeometryTestData.Ring(
                (400_000, 5_500_000),
                (401_000, 5_500_000),
                (401_000, 5_501_000),
                (400_000, 5_501_000)));
        settlement.PolygonCandidates.Add(candidate);

        SettlementGeometryQualitySelector.RecalculateAll(new[] { settlement });

        Assert.True(candidate.RejectedByGeometryGuard);
        Assert.Contains("не містить центр", candidate.RejectionReason);
        Assert.Empty(
            SettlementGeometryQualitySelector.GetPreferredPolygonCandidates(
                settlement));
    }

    [Fact]
    public void ExplicitlyLinkedOsmBoundary_NearOwnCenter_IsAccepted()
    {
        var settlement = GeometryTestData.Settlement(
            "Ковалівка",
            337547188,
            GeometryTestData.Point(401_200, 5_500_500));
        var candidate = GeometryTestData.Polygon(
            GeometryTestData.Ring(
                (400_000, 5_500_000),
                (401_000, 5_500_000),
                (401_000, 5_501_000),
                (400_000, 5_501_000)));
        candidate.HasExplicitSettlementLink = true;
        settlement.PolygonCandidates.Add(candidate);

        SettlementGeometryQualitySelector.RecalculateAll(new[] { settlement });

        Assert.False(candidate.ContainsCenter);
        Assert.True(candidate.AcceptedByExplicitSettlementLink);
        Assert.InRange(candidate.CenterDistanceMeters!.Value, 199.0, 201.0);
        Assert.False(candidate.RejectedByGeometryGuard);
        Assert.Single(
            SettlementGeometryQualitySelector.GetPreferredPolygonCandidates(
                settlement));
    }

    [Fact]
    public void ExplicitlyLinkedOsmBoundary_TooFarFromOwnCenter_IsRejected()
    {
        var settlement = GeometryTestData.Settlement(
            "Ковалівка",
            337547188,
            GeometryTestData.Point(401_600, 5_500_500));
        var candidate = GeometryTestData.Polygon(
            GeometryTestData.Ring(
                (400_000, 5_500_000),
                (401_000, 5_500_000),
                (401_000, 5_501_000),
                (400_000, 5_501_000)));
        candidate.HasExplicitSettlementLink = true;
        settlement.PolygonCandidates.Add(candidate);

        SettlementGeometryQualitySelector.RecalculateAll(new[] { settlement });

        Assert.False(candidate.AcceptedByExplicitSettlementLink);
        Assert.True(candidate.RejectedByGeometryGuard);
        Assert.Contains("не містить центр", candidate.RejectionReason);
    }

    [Fact]
    public void OsmPlaceNode_IsPreferredOverRelationBoundingBoxCenter()
    {
        var nodeCenter = GeometryTestData.Point(400_000, 5_500_000);
        var settlement = GeometryTestData.Settlement(
            "Ковалівка",
            337547188,
            nodeCenter);
        settlement.CenterCandidates.Add(new SettlementCenterCandidate
        {
            Provider = SettlementDataSources.OpenStreetMapOverpass,
            ExternalId = "3558186",
            Priority = SettlementDataSources.OpenStreetMapPriority,
            UpdatedAt = DateTime.Now.AddMinutes(1),
            Point = GeometryTestData.Point(400_250, 5_500_000)
        });

        SettlementGeometryQualitySelector.RecalculateAll(new[] { settlement });

        Assert.Equal(nodeCenter.X, settlement.FallbackPoint!.X);
        Assert.Equal(nodeCenter.Y, settlement.FallbackPoint.Y);
    }

    [Fact]
    public void PolygonContainingAlternateConfirmedCenter_IsNotRejected()
    {
        var settlement = GeometryTestData.Settlement(
            "Тестове",
            1,
            GeometryTestData.Point(405_000, 5_505_000));
        settlement.CenterCandidates.Add(new SettlementCenterCandidate
        {
            Provider = "Second provider",
            ExternalId = "inside-center",
            Priority = 80,
            Point = GeometryTestData.Point(400_500, 5_500_500)
        });
        var candidate = GeometryTestData.Polygon(
            GeometryTestData.Ring(
                (400_000, 5_500_000),
                (401_000, 5_500_000),
                (401_000, 5_501_000),
                (400_000, 5_501_000)));
        settlement.PolygonCandidates.Add(candidate);

        SettlementGeometryQualitySelector.RecalculateAll(new[] { settlement });

        Assert.False(candidate.RejectedByGeometryGuard);
        Assert.True(candidate.ContainsCenter);
        Assert.Single(
            SettlementGeometryQualitySelector.GetPreferredPolygonCandidates(
                settlement));
    }

    [Fact]
    public void OversizedVillagePolygon_IsRejectedEvenWhenItIsTheOnlyCandidate()
    {
        var settlement = GeometryTestData.Settlement(
            "Тестове",
            1,
            GeometryTestData.Point(410_500, 5_510_500));
        var candidate = GeometryTestData.Polygon(
            GeometryTestData.Ring(
                (400_000, 5_500_000),
                (421_000, 5_500_000),
                (421_000, 5_521_000),
                (400_000, 5_521_000)));
        candidate.HasExplicitSettlementLink = true;
        settlement.PolygonCandidates.Add(candidate);

        SettlementGeometryQualitySelector.RecalculateAll(new[] { settlement });

        Assert.True(candidate.RejectedByGeometryGuard);
        Assert.Contains("перевищує допустимі", candidate.RejectionReason);
        Assert.Empty(
            SettlementGeometryQualitySelector.GetPreferredPolygonCandidates(
                settlement));
    }

    [Fact]
    public void PolygonContainingTwoOtherSettlements_IsRejected()
    {
        var owner = GeometryTestData.Settlement(
            "Власник",
            1,
            GeometryTestData.Point(400_500, 5_500_500));
        var firstOther = GeometryTestData.Settlement(
            "Сусіднє 1",
            2,
            GeometryTestData.Point(401_500, 5_501_500));
        var secondOther = GeometryTestData.Settlement(
            "Сусіднє 2",
            3,
            GeometryTestData.Point(402_500, 5_502_500));
        var candidate = GeometryTestData.Polygon(
            GeometryTestData.Ring(
                (400_000, 5_500_000),
                (403_000, 5_500_000),
                (403_000, 5_503_000),
                (400_000, 5_503_000)));
        candidate.HasExplicitSettlementLink = true;
        owner.PolygonCandidates.Add(candidate);

        SettlementGeometryQualitySelector.RecalculateAll(
            new[] { owner, firstOther, secondOther });

        Assert.True(candidate.RejectedBySettlementContainmentGuard);
        Assert.Equal(2, candidate.ContainedSettlementCount);
        Assert.Empty(
            SettlementGeometryQualitySelector.GetPreferredPolygonCandidates(
                owner));
    }

    [Fact]
    public void SettlementCenterInsideGeoJsonHole_IsNotCountedAsContained()
    {
        var owner = GeometryTestData.Settlement(
            "Власник",
            1,
            GeometryTestData.Point(400_100, 5_500_100));
        var enclave = GeometryTestData.Settlement(
            "Анклав",
            2,
            GeometryTestData.Point(400_500, 5_500_500));
        var hole = GeometryTestData.Ring(
            (400_400, 5_500_400),
            (400_600, 5_500_400),
            (400_600, 5_500_600),
            (400_400, 5_500_600));
        var candidate = GeometryTestData.Polygon(
            GeometryTestData.Ring(
                (400_000, 5_500_000),
                (401_000, 5_500_000),
                (401_000, 5_501_000),
                (400_000, 5_501_000)),
            hole);
        owner.PolygonCandidates.Add(candidate);

        SettlementGeometryQualitySelector.RecalculateAll(
            new[] { owner, enclave });

        Assert.False(candidate.RejectedByGeometryGuard);
        Assert.Equal(0, candidate.ContainedSettlementCount);
        Assert.False(
            SettlementGeometryQualitySelector.IsPointInsideCandidate(
                enclave.FallbackPoint!,
                candidate));
    }

    [Fact]
    public void SelfIntersectingPolygon_IsRejected()
    {
        var settlement = GeometryTestData.Settlement(
            "Тестове",
            1,
            GeometryTestData.Point(400_500, 5_500_500));
        var candidate = GeometryTestData.Polygon(
            GeometryTestData.Ring(
                (400_000, 5_500_000),
                (401_000, 5_501_000),
                (400_000, 5_501_000),
                (401_000, 5_500_000)));
        settlement.PolygonCandidates.Add(candidate);

        SettlementGeometryQualitySelector.RecalculateAll(new[] { settlement });

        Assert.True(candidate.RejectedByGeometryGuard);
        Assert.Empty(
            SettlementGeometryQualitySelector.GetPreferredPolygonCandidates(
                settlement));
    }

    [Fact]
    public void MalformedInteriorRing_IsRejectedInsteadOfSilentlyIgnored()
    {
        var settlement = GeometryTestData.Settlement(
            "Тестове",
            1,
            GeometryTestData.Point(400_500, 5_500_500));
        var candidate = GeometryTestData.Polygon(
            GeometryTestData.Ring(
                (400_000, 5_500_000),
                (401_000, 5_500_000),
                (401_000, 5_501_000),
                (400_000, 5_501_000)),
            "400100,5500100;пошкоджена точка;400200,5500200");
        settlement.PolygonCandidates.Add(candidate);

        SettlementGeometryQualitySelector.RecalculateAll(new[] { settlement });

        Assert.True(candidate.RejectedByGeometryGuard);
        Assert.Contains("пошкоджене", candidate.RejectionReason);
    }
}
