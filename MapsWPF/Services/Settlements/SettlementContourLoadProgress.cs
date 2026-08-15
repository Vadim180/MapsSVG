namespace MapsWPF.Services.Settlements
{
    public sealed class SettlementContourLoadProgress
    {
        public int Completed { get; init; }

        public int Total { get; init; }

        public string CurrentSettlementName { get; init; } = string.Empty;

        public string State { get; init; } = string.Empty;

        public bool HasPolygon { get; init; }

        public bool HasFallbackCenter { get; init; }

        public string ProviderName { get; init; } = string.Empty;

        public int ProviderIndex { get; init; }

        public int ProviderCount { get; init; }

        public double? OverallPercent { get; init; }
    }
}
