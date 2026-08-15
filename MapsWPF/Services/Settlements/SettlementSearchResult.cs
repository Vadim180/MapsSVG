namespace MapsWPF.Services.Settlements
{
    public sealed class SettlementSearchResult
    {
        public string Name { get; set; } = string.Empty;

        public string Place { get; set; } = string.Empty;

        public double DistanceMeters { get; set; }

        public bool IsInsidePolygon { get; set; }
    }
}