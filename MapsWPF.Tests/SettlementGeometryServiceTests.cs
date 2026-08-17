using MapsWPF.Services.Settlements;

namespace MapsWPF.Tests;

public sealed class SettlementGeometryServiceTests
{
    [Fact]
    public void CacheIsLazyAndCanBeReleasedFromMemory()
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            var writer = new SettlementGeometryService(directory);
            Assert.False(writer.IsMemoryCacheLoaded);
            Assert.True(writer.Save(CreateCache("Тестове")));
            Assert.True(writer.IsMemoryCacheLoaded);

            writer.ReleaseMemory();

            Assert.False(writer.IsMemoryCacheLoaded);
            Assert.Equal("Тестове", Assert.Single(writer.CurrentCache.Settlements).Name);
            Assert.True(writer.IsMemoryCacheLoaded);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void CorruptedPrimaryCache_IsRecoveredFromAtomicBackup()
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            var writer = new SettlementGeometryService(directory);
            Assert.True(writer.Save(CreateCache("Перша версія")));
            Assert.True(writer.Save(CreateCache("Друга версія")));
            Assert.True(writer.Save(CreateCache("Третя версія")));
            Assert.True(File.Exists(writer.CacheBackupFilePath));

            File.WriteAllText(writer.CacheFilePath, "{ пошкоджений json");

            var reader = new SettlementGeometryService(directory);
            Assert.False(reader.IsMemoryCacheLoaded);

            var recovered = reader.CurrentCache;

            Assert.Equal(
                "Друга версія",
                Assert.Single(recovered.Settlements).Name);
            Assert.Contains("резервної копії", reader.LastLoadWarning);
            Assert.Equal(
                SettlementGeometryCache.CurrentSchemaVersion,
                recovered.SchemaVersion);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public void ExplicitRelationLinkMetadata_SurvivesCacheRoundTrip()
    {
        var directory = CreateTemporaryDirectory();

        try
        {
            var cache = CreateCache("Ковалівка");
            var settlement = Assert.Single(cache.Settlements);
            var candidate = GeometryTestData.Polygon(
                GeometryTestData.Ring(
                    (400_000, 5_500_000),
                    (401_000, 5_500_000),
                    (401_000, 5_501_000),
                    (400_000, 5_501_000)));
            candidate.HasExplicitSettlementLink = true;
            settlement.PolygonCandidates.Add(candidate);

            var writer = new SettlementGeometryService(directory);
            Assert.True(writer.Save(cache));
            writer.ReleaseMemory();

            var loadedCandidate = Assert.Single(
                Assert.Single(writer.CurrentCache.Settlements)
                    .PolygonCandidates);

            Assert.True(loadedCandidate.HasExplicitSettlementLink);
            Assert.Equal(
                SettlementGeometryCache.CurrentSchemaVersion,
                writer.CurrentCache.SchemaVersion);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static SettlementGeometryCache CreateCache(string settlementName)
    {
        return new SettlementGeometryCache
        {
            Settlements = new List<SettlementGeometryItem>
            {
                GeometryTestData.Settlement(
                    settlementName,
                    1,
                    GeometryTestData.Point(400_500, 5_500_500))
            }
        };
    }

    private static string CreateTemporaryDirectory()
    {
        var path = Path.Combine(
            Path.GetTempPath(),
            "MapsWPF.Tests",
            Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
