using System;
using System.Collections.Generic;

namespace MapsWPF.Services.Settlements
{
    public sealed class SettlementGeometryCache
    {
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        public string Source { get; set; } = "local";

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

        public string Place { get; set; } = string.Empty;

        public string OsmType { get; set; } = string.Empty;

        public long OsmId { get; set; }

        public string GeometrySourceOsmType { get; set; } = string.Empty;

        public long GeometrySourceOsmId { get; set; }

        // Один рядок = один полігон:
        // "399627,5510807;398868,5510245;397675,5509486"
        public List<string> Polygons { get; set; } = new();

        public SettlementPoint? FallbackPoint { get; set; }
    }

    public sealed class SettlementPoint
    {
        public float X { get; set; }

        public float Y { get; set; }

        public SettlementPoint()
        {
        }

        public SettlementPoint(float x, float y)
        {
            X = x;
            Y = y;
        }
    }
}