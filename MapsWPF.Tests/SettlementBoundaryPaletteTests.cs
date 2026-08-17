using MapsWPF.GMapIntegration;
using System.Windows.Media;

namespace MapsWPF.Tests;

public sealed class SettlementBoundaryPaletteTests
{
    [Fact]
    public void Palette_DoesNotReuseOrangeBufferOrGoldCenterColors()
    {
        Assert.DoesNotContain(Brushes.Orange, SettlementBoundaryPalette.Colors);
        Assert.DoesNotContain(Brushes.DarkOrange, SettlementBoundaryPalette.Colors);
        Assert.DoesNotContain(Brushes.Gold, SettlementBoundaryPalette.Colors);
    }

    [Fact]
    public void PoltavaBoundary_HasHighContrastColorInsteadOfOrange()
    {
        var brush = SettlementBoundaryPalette.GetBrush(
            "osm:relation:3540279");

        Assert.Same(Brushes.WhiteSmoke, brush);
    }
}
