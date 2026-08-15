using System;
using System.Collections.Generic;

namespace MapsWPF.Services.Settlements
{
    public sealed class SettlementProviderOptions
    {
        public bool UseOpenStreetMapOverpass { get; init; } = true;

        public bool UseAllAvailableProviders { get; init; } = true;

        public string GeoapifyApiKey { get; init; } = string.Empty;

        public string AzureMapsSubscriptionKey { get; init; } = string.Empty;

        public string GoogleGeocodingApiKey { get; init; } = string.Empty;

        public string BingMapsApiKey { get; init; } = string.Empty;

        public string GraphHopperApiKey { get; init; } = string.Empty;
    }

    public sealed class SettlementCityProviderResult
    {
        public SettlementGeometryItem Settlement { get; init; } = new();

        public int AttemptedProviderCount { get; init; }

        public int PolygonProviderCount { get; init; }

        public int CenterProviderCount { get; init; }

        public IReadOnlyList<string> ProviderStates { get; init; } =
            Array.Empty<string>();
    }

    public sealed class SettlementProviderAvailability
    {
        public IReadOnlyList<string> ActiveProviders { get; init; } =
            Array.Empty<string>();

        public IReadOnlyList<string> SkippedProviders { get; init; } =
            Array.Empty<string>();
    }
}
