using GMap.NET;
using GMap.NET.Internals;

namespace MapsWPF.Tests;

public sealed class TilePrefetchPlannerTests
{
    [Fact]
    public void Build_AddsPreparedRingAndKeepsCenterFirstForLoaderStack()
    {
        var center = new GPoint(50, 50);
        var tiles = TilePrefetchPlanner.Build(
            center,
            new GSize(3, 2),
            new GSize(0, 0),
            new GSize(100, 100),
            tileWidth: 256,
            tileHeight: 256,
            scaleX: 1,
            scaleY: 1,
            additionalMargin: 1,
            shuffle: false);

        Assert.Equal(63, tiles.Count);

        var loaderStack = new Stack<DrawTile>();
        foreach (var tile in tiles)
        {
            loaderStack.Push(tile);
        }

        Assert.Equal(center, loaderStack.Pop().PosXY);
    }

    [Fact]
    public void Build_ClipsPreparedWindowAtProjectionBoundary()
    {
        var tiles = TilePrefetchPlanner.Build(
            new GPoint(0, 0),
            new GSize(1, 1),
            new GSize(0, 0),
            new GSize(10, 10),
            tileWidth: 256,
            tileHeight: 256,
            scaleX: 1,
            scaleY: 1,
            additionalMargin: 1,
            shuffle: false);

        Assert.Equal(9, tiles.Count);
        Assert.All(tiles, tile =>
        {
            Assert.InRange(tile.PosXY.X, 0, 2);
            Assert.InRange(tile.PosXY.Y, 0, 2);
        });
        Assert.Equal(
            tiles.Count,
            tiles.Select(tile => tile.PosXY).Distinct().Count());
    }

    [Fact]
    public void Build_UsesFractionalZoomScaleWithoutDroppingSafetyRing()
    {
        var tiles = TilePrefetchPlanner.Build(
            new GPoint(20, 20),
            new GSize(3, 2),
            new GSize(0, 0),
            new GSize(100, 100),
            tileWidth: 256,
            tileHeight: 256,
            scaleX: 0.5,
            scaleY: 0.5,
            additionalMargin: 1,
            shuffle: false);

        Assert.Equal(35, tiles.Count);
        Assert.Contains(tiles, tile => tile.PosXY == new GPoint(17, 18));
        Assert.Contains(tiles, tile => tile.PosXY == new GPoint(23, 22));
    }
}
