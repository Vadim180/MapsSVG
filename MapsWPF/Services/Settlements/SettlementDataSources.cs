using System;

namespace MapsWPF.Services.Settlements
{
    public static class SettlementDataSources
    {
        public const string OpenStreetMapOverpass = "OpenStreetMap/Overpass";

        public const string GeoapifyBoundaries = "Geoapify/Boundaries";

        public const string AzureMapsPolygon = "Azure Maps/Polygon";

        public const string GoogleGeocoding = "Google/Geocoding (POC)";

        public const string BingMapsLocations = "Bing Maps/Locations";

        public const string GraphHopperGeocoding = "GraphHopper/Geocoding";

        public const string LegacyCache = "Legacy cache";

        public const int AzureMapsPolygonPriority = 120;

        public const int OpenStreetMapPriority = 115;

        public const int GeoapifyBoundaryPriority = 105;

        public const int GoogleGeocodingPriority = 85;

        public const int BingMapsGeocodingPriority = 80;

        public const int GraphHopperGeocodingPriority = 75;

        public const int MapGeocoderPriority = 60;

        public const int LegacyPriority = 10;

        public static string MapGeocoder(string? providerName)
        {
            var name = string.IsNullOrWhiteSpace(providerName)
                ? "Unknown"
                : providerName.Trim();

            return $"GMap geocoder/{name}";
        }

        public static string Normalize(string? source)
        {
            if (string.IsNullOrWhiteSpace(source) ||
                string.Equals(source, "local", StringComparison.OrdinalIgnoreCase))
            {
                return LegacyCache;
            }

            if (string.Equals(
                    source,
                    "multi-provider",
                    StringComparison.OrdinalIgnoreCase))
            {
                return string.Empty;
            }

            if (source.Contains("overpass", StringComparison.OrdinalIgnoreCase) ||
                source.Contains("openstreetmap", StringComparison.OrdinalIgnoreCase))
            {
                return OpenStreetMapOverpass;
            }

            return source.Trim();
        }

        public static int GetDefaultPriority(string? source)
        {
            var normalized = Normalize(source);

            if (string.Equals(
                    normalized,
                    OpenStreetMapOverpass,
                    StringComparison.OrdinalIgnoreCase))
            {
                return OpenStreetMapPriority;
            }

            if (string.Equals(
                    normalized,
                    AzureMapsPolygon,
                    StringComparison.OrdinalIgnoreCase))
            {
                return AzureMapsPolygonPriority;
            }

            if (string.Equals(
                    normalized,
                    GeoapifyBoundaries,
                    StringComparison.OrdinalIgnoreCase))
            {
                return GeoapifyBoundaryPriority;
            }

            if (string.Equals(
                    normalized,
                    GoogleGeocoding,
                    StringComparison.OrdinalIgnoreCase))
            {
                return GoogleGeocodingPriority;
            }

            if (string.Equals(
                    normalized,
                    BingMapsLocations,
                    StringComparison.OrdinalIgnoreCase))
            {
                return BingMapsGeocodingPriority;
            }

            if (string.Equals(
                    normalized,
                    GraphHopperGeocoding,
                    StringComparison.OrdinalIgnoreCase))
            {
                return GraphHopperGeocodingPriority;
            }

            if (normalized.StartsWith(
                    "GMap geocoder/",
                    StringComparison.OrdinalIgnoreCase))
            {
                return MapGeocoderPriority;
            }

            return LegacyPriority;
        }
    }
}
