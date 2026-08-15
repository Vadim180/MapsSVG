using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using GMap.NET;
using GMap.NET.MapProviders;
using Newtonsoft.Json.Linq;

namespace MapsWPF.Services.Settlements
{
    public sealed class SettlementExternalGeometryLoader
    {
        private const int RequestTimeoutSeconds = 30;

        private readonly HttpClient _httpClient = new();
        private readonly TryConvertLatLngToUtmDelegate _tryConvertLatLngToUtm;
        private readonly TryConvertUtmToLatLngDelegate _tryConvertUtmToLatLng;

        public delegate bool TryConvertLatLngToUtmDelegate(
            double lat,
            double lng,
            out PointF utm,
            out int zone,
            out string band);

        public delegate bool TryConvertUtmToLatLngDelegate(
            PointF utm,
            out double lat,
            out double lng);

        public SettlementExternalGeometryLoader(
            TryConvertLatLngToUtmDelegate tryConvertLatLngToUtm,
            TryConvertUtmToLatLngDelegate tryConvertUtmToLatLng)
        {
            _tryConvertLatLngToUtm = tryConvertLatLngToUtm ??
                throw new ArgumentNullException(nameof(tryConvertLatLngToUtm));
            _tryConvertUtmToLatLng = tryConvertUtmToLatLng ??
                throw new ArgumentNullException(nameof(tryConvertUtmToLatLng));

            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "MapsWPF/1.0 settlement-boundary-cache");
        }

        public SettlementProviderAvailability GetAvailability(
            SettlementProviderOptions options,
            IEnumerable<GMapProvider>? mapProviders)
        {
            var active = new List<string>();
            var skipped = new List<string>();

            if (!options.UseAllAvailableProviders)
            {
                return new SettlementProviderAvailability
                {
                    ActiveProviders = active,
                    SkippedProviders = skipped
                };
            }

            AddKeyedProvider(
                active,
                skipped,
                SettlementDataSources.GeoapifyBoundaries,
                options.GeoapifyApiKey);
            AddKeyedProvider(
                active,
                skipped,
                SettlementDataSources.AzureMapsPolygon,
                options.AzureMapsSubscriptionKey);

            var families = GetGMapGeocoderFamilies(mapProviders);

            AddGMapProvider(
                active,
                skipped,
                families,
                "Google",
                SettlementDataSources.GoogleGeocoding,
                options.GoogleGeocodingApiKey);

            AddGMapProvider(
                active,
                skipped,
                families,
                "Bing",
                SettlementDataSources.BingMapsLocations,
                options.BingMapsApiKey);
            AddGMapProvider(
                active,
                skipped,
                families,
                "GraphHopper",
                SettlementDataSources.GraphHopperGeocoding,
                options.GraphHopperApiKey);

            if (families.Contains("OpenStreetMap"))
            {
                skipped.Add(
                    "Nominatim: публічний сервер не використовується для bulk-черги");
            }

            if (families.Contains("Yahoo"))
            {
                skipped.Add("Yahoo: застарілий API геокодування");
            }

            return new SettlementProviderAvailability
            {
                ActiveProviders = active,
                SkippedProviders = skipped
            };
        }

        public async Task<SettlementCityProviderResult> EnrichSettlementAsync(
            SettlementCacheBounds bounds,
            SettlementGeometryItem sourceSettlement,
            SettlementProviderOptions options,
            IEnumerable<GMapProvider>? mapProviders,
            CancellationToken cancellationToken)
        {
            var settlement = SettlementGeometryMerger.CloneSettlementItem(
                sourceSettlement);
            var availability = GetAvailability(options, mapProviders);
            var states = new List<string>();
            var results = new List<ProviderLookupResult>();
            var center = TryGetAnchorLatLng(settlement);

            var centerTasks = CreateCenterTasks(
                bounds,
                settlement,
                options,
                mapProviders,
                center,
                cancellationToken);

            var polygonTasks = center.HasValue
                ? CreatePolygonTasks(
                    settlement,
                    options,
                    center.Value,
                    cancellationToken)
                : new List<Task<ProviderLookupResult>>();

            if (centerTasks.Count > 0 || polygonTasks.Count > 0)
            {
                var firstWave = centerTasks.Concat(polygonTasks).ToArray();
                results.AddRange(await Task.WhenAll(firstWave));
                ApplyResults(settlement, results);
            }

            if (!center.HasValue)
            {
                center = TryGetAnchorLatLng(settlement);

                if (center.HasValue)
                {
                    var secondWave = CreatePolygonTasks(
                        settlement,
                        options,
                        center.Value,
                        cancellationToken);

                    if (secondWave.Count > 0)
                    {
                        var secondWaveResults = await Task.WhenAll(secondWave);
                        results.AddRange(secondWaveResults);
                        ApplyResults(settlement, secondWaveResults);
                    }
                }
            }

            SettlementGeometryQualitySelector.Recalculate(settlement);

            foreach (var result in results)
            {
                states.Add($"{result.Provider}: {result.State}");
            }

            return new SettlementCityProviderResult
            {
                Settlement = settlement,
                AttemptedProviderCount = results.Count(x => x.Attempted),
                PolygonProviderCount = results.Count(x => x.Polygons.Count > 0),
                CenterProviderCount = results.Count(x => x.Center.HasValue),
                ProviderStates = states.Count > 0
                    ? states
                    : availability.ActiveProviders.Count == 0
                        ? new[] { "Зовнішні API не налаштовані" }
                        : new[] { "Усі кандидати вже є в кеші" }
            };
        }

        public bool IsSettlementInsideBounds(
            SettlementGeometryItem settlement,
            SettlementCacheBounds bounds)
        {
            var anchor = TryGetAnchorLatLng(settlement);
            return anchor.HasValue && IsInsideBounds(anchor.Value, bounds);
        }

        private List<Task<ProviderLookupResult>> CreatePolygonTasks(
            SettlementGeometryItem settlement,
            SettlementProviderOptions options,
            PointLatLng center,
            CancellationToken cancellationToken)
        {
            var tasks = new List<Task<ProviderLookupResult>>();

            if (!options.UseAllAvailableProviders)
            {
                return tasks;
            }

            if (!string.IsNullOrWhiteSpace(options.GeoapifyApiKey) &&
                !SettlementGeometryMerger.HasPolygonsFromProvider(
                    settlement,
                    SettlementDataSources.GeoapifyBoundaries))
            {
                tasks.Add(LoadGeoapifyBoundaryAsync(
                    settlement,
                    center,
                    options.GeoapifyApiKey.Trim(),
                    cancellationToken));
            }

            if (!string.IsNullOrWhiteSpace(options.AzureMapsSubscriptionKey) &&
                !SettlementGeometryMerger.HasPolygonsFromProvider(
                    settlement,
                    SettlementDataSources.AzureMapsPolygon))
            {
                tasks.Add(LoadAzureMapsPolygonAsync(
                    center,
                    options.AzureMapsSubscriptionKey.Trim(),
                    cancellationToken));
            }

            return tasks;
        }

        private List<Task<ProviderLookupResult>> CreateCenterTasks(
            SettlementCacheBounds bounds,
            SettlementGeometryItem settlement,
            SettlementProviderOptions options,
            IEnumerable<GMapProvider>? mapProviders,
            PointLatLng? anchor,
            CancellationToken cancellationToken)
        {
            var tasks = new List<Task<ProviderLookupResult>>();

            if (!options.UseAllAvailableProviders)
            {
                return tasks;
            }

            var families = GetGMapGeocoderFamilies(mapProviders);
            var query = settlement.Name.Trim();

            if (families.Contains("Google") &&
                !string.IsNullOrWhiteSpace(options.GoogleGeocodingApiKey) &&
                !HasCenterFromProvider(settlement, SettlementDataSources.GoogleGeocoding))
            {
                tasks.Add(LoadGoogleCenterAsync(
                    query,
                    bounds,
                    anchor,
                    options.GoogleGeocodingApiKey.Trim(),
                    cancellationToken));
            }

            if (families.Contains("Bing") &&
                !string.IsNullOrWhiteSpace(options.BingMapsApiKey) &&
                !HasCenterFromProvider(settlement, SettlementDataSources.BingMapsLocations))
            {
                tasks.Add(LoadBingCenterAsync(
                    query,
                    bounds,
                    anchor,
                    options.BingMapsApiKey.Trim(),
                    cancellationToken));
            }

            if (families.Contains("GraphHopper") &&
                !string.IsNullOrWhiteSpace(options.GraphHopperApiKey) &&
                !HasCenterFromProvider(settlement, SettlementDataSources.GraphHopperGeocoding))
            {
                tasks.Add(LoadGraphHopperCenterAsync(
                    query,
                    bounds,
                    anchor,
                    options.GraphHopperApiKey.Trim(),
                    cancellationToken));
            }

            return tasks;
        }

        private Task<ProviderLookupResult> LoadGoogleCenterAsync(
            string query,
            SettlementCacheBounds bounds,
            PointLatLng? anchor,
            string apiKey,
            CancellationToken cancellationToken)
        {
            var uri =
                "https://maps.googleapis.com/maps/api/geocode/json?address=" +
                Uri.EscapeDataString(query) +
                "&key=" +
                Uri.EscapeDataString(apiKey);

            return LoadCenterFromJsonAsync(
                SettlementDataSources.GoogleGeocoding,
                SettlementDataSources.GoogleGeocodingPriority,
                uri,
                bounds,
                anchor,
                root =>
                    (root["results"] as JArray ?? new JArray())
                    .Select(x => new GeocodedPoint(
                        x["geometry"]?["location"]?["lat"]?.Value<double?>(),
                        x["geometry"]?["location"]?["lng"]?.Value<double?>(),
                        x["place_id"]?.Value<string>())),
                cancellationToken);
        }

        private async Task<ProviderLookupResult> LoadGeoapifyBoundaryAsync(
            SettlementGeometryItem settlement,
            PointLatLng center,
            string apiKey,
            CancellationToken cancellationToken)
        {
            const string provider = SettlementDataSources.GeoapifyBoundaries;

            try
            {
                var uri = string.Format(
                    CultureInfo.InvariantCulture,
                    "https://api.geoapify.com/v1/boundaries/part-of?lon={0:R}&lat={1:R}&boundaries=administrative&geometry=geometry_1000&lang=uk&apiKey={2}",
                    center.Lng,
                    center.Lat,
                    Uri.EscapeDataString(apiKey));

                var root = await GetJsonAsync(uri, null, cancellationToken);
                var feature = SelectBestGeoapifyFeature(root, settlement);

                if (feature == null)
                {
                    return ProviderLookupResult.Empty(
                        provider,
                        "відповідну межу міста не знайдено");
                }

                var lines = SettlementGeoJsonGeometryReader.ReadPolygonLines(
                    feature["geometry"],
                    TryConvertForGeoJson);
                var externalId =
                    feature["properties"]?["place_id"]?.Value<string>() ??
                    feature["properties"]?["datasource"]?["raw"]?["osm_id"]?.ToString() ??
                    CreateCoordinateId(center);

                return new ProviderLookupResult(
                    provider,
                    externalId,
                    SettlementDataSources.GeoapifyBoundaryPriority,
                    lines,
                    null,
                    lines.Count > 0 ? "контур отримано" : "повернуто лише точку");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SETTLEMENT PROVIDER] {provider}: {ex.Message}");
                return ProviderLookupResult.Empty(provider, "помилка API");
            }
        }

        private async Task<ProviderLookupResult> LoadAzureMapsPolygonAsync(
            PointLatLng center,
            string subscriptionKey,
            CancellationToken cancellationToken)
        {
            const string provider = SettlementDataSources.AzureMapsPolygon;

            try
            {
                var uri = string.Format(
                    CultureInfo.InvariantCulture,
                    "https://atlas.microsoft.com/search/polygon?api-version=2026-01-01&coordinates={0:R},{1:R}&resultType=locality&resolution=medium",
                    center.Lng,
                    center.Lat);
                var headers = new Dictionary<string, string>
                {
                    ["subscription-key"] = subscriptionKey
                };
                var root = await GetJsonAsync(uri, headers, cancellationToken);
                var lines = SettlementGeoJsonGeometryReader.ReadPolygonLines(
                    root,
                    TryConvertForGeoJson);

                return new ProviderLookupResult(
                    provider,
                    root["id"]?.Value<string>() ?? CreateCoordinateId(center),
                    SettlementDataSources.AzureMapsPolygonPriority,
                    lines,
                    null,
                    lines.Count > 0 ? "контур отримано" : "контур не знайдено");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SETTLEMENT PROVIDER] {provider}: {ex.Message}");
                return ProviderLookupResult.Empty(provider, "помилка API");
            }
        }

        private Task<ProviderLookupResult> LoadBingCenterAsync(
            string query,
            SettlementCacheBounds bounds,
            PointLatLng? anchor,
            string apiKey,
            CancellationToken cancellationToken)
        {
            var uri =
                "https://dev.virtualearth.net/REST/v1/Locations?q=" +
                Uri.EscapeDataString(query) +
                "&maxResults=5&key=" +
                Uri.EscapeDataString(apiKey);

            return LoadCenterFromJsonAsync(
                SettlementDataSources.BingMapsLocations,
                SettlementDataSources.BingMapsGeocodingPriority,
                uri,
                bounds,
                anchor,
                root =>
                    (root["resourceSets"] as JArray ?? new JArray())
                    .SelectMany(x => x["resources"] as JArray ?? new JArray())
                    .Select(x => new GeocodedPoint(
                        x["point"]?["coordinates"]?[0]?.Value<double?>(),
                        x["point"]?["coordinates"]?[1]?.Value<double?>(),
                        x["entityId"]?.Value<string>() ??
                        x["name"]?.Value<string>())),
                cancellationToken);
        }

        private Task<ProviderLookupResult> LoadGraphHopperCenterAsync(
            string query,
            SettlementCacheBounds bounds,
            PointLatLng? anchor,
            string apiKey,
            CancellationToken cancellationToken)
        {
            var uri =
                "https://graphhopper.com/api/1/geocode?q=" +
                Uri.EscapeDataString(query) +
                "&limit=5&locale=uk&key=" +
                Uri.EscapeDataString(apiKey);

            return LoadCenterFromJsonAsync(
                SettlementDataSources.GraphHopperGeocoding,
                SettlementDataSources.GraphHopperGeocodingPriority,
                uri,
                bounds,
                anchor,
                root =>
                    (root["hits"] as JArray ?? new JArray())
                    .Select(x => new GeocodedPoint(
                        x["point"]?["lat"]?.Value<double?>(),
                        x["point"]?["lng"]?.Value<double?>(),
                        x["osm_id"]?.ToString() ??
                        x["name"]?.Value<string>())),
                cancellationToken);
        }

        private async Task<ProviderLookupResult> LoadCenterFromJsonAsync(
            string provider,
            int priority,
            string uri,
            SettlementCacheBounds bounds,
            PointLatLng? anchor,
            Func<JObject, IEnumerable<GeocodedPoint>> readPoints,
            CancellationToken cancellationToken)
        {
            try
            {
                var root = await GetJsonAsync(uri, null, cancellationToken);
                var candidates = readPoints(root)
                    .Where(x => x.Point.HasValue)
                    .Where(x => IsInsideBounds(x.Point!.Value, bounds))
                    .ToList();

                if (candidates.Count == 0)
                {
                    return ProviderLookupResult.Empty(provider, "центр не знайдено");
                }

                var reference = anchor ?? new PointLatLng(
                    (bounds.Top + bounds.Bottom) / 2.0,
                    (bounds.Left + bounds.Right) / 2.0);
                var selected = candidates
                    .OrderBy(x => GetDistanceMeters(reference, x.Point!.Value))
                    .First();

                return new ProviderLookupResult(
                    provider,
                    selected.ExternalId,
                    priority,
                    Array.Empty<string>(),
                    selected.Point,
                    "центр отримано");
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[SETTLEMENT PROVIDER] {provider}: {ex.Message}");
                return ProviderLookupResult.Empty(provider, "помилка API");
            }
        }

        private async Task<JObject> GetJsonAsync(
            string uri,
            IReadOnlyDictionary<string, string>? headers,
            CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(RequestTimeoutSeconds));

            using var response = await _httpClient.SendAsync(
                request,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync(timeoutCts.Token);
            return JObject.Parse(json);
        }

        private void ApplyResults(
            SettlementGeometryItem settlement,
            IEnumerable<ProviderLookupResult> results)
        {
            foreach (var result in results)
            {
                if (result.Center.HasValue &&
                    _tryConvertLatLngToUtm(
                        result.Center.Value.Lat,
                        result.Center.Value.Lng,
                        out var utm,
                        out _,
                        out _))
                {
                    SettlementGeometryMerger.AddProviderReference(
                        settlement,
                        result.Provider,
                        result.ExternalId,
                        "center");
                    SettlementGeometryMerger.AddCenterCandidate(
                        settlement,
                        new SettlementPoint(utm.X, utm.Y),
                        result.Provider,
                        result.ExternalId,
                        result.Priority);
                }

                foreach (var polygon in result.Polygons)
                {
                    SettlementGeometryMerger.AddProviderReference(
                        settlement,
                        result.Provider,
                        result.ExternalId,
                        "polygon");
                    SettlementGeometryMerger.AddPolygonCandidate(
                        settlement,
                        polygon,
                        result.Provider,
                        result.ExternalId,
                        result.Priority);
                }
            }

            SettlementGeometryQualitySelector.Recalculate(settlement);
        }

        private JToken? SelectBestGeoapifyFeature(
            JObject root,
            SettlementGeometryItem settlement)
        {
            var targetName = NormalizeName(settlement.Name);
            var osmIds = new HashSet<string>(
                settlement.ProviderReferences
                    .Where(x => string.Equals(
                        x.Provider,
                        SettlementDataSources.OpenStreetMapOverpass,
                        StringComparison.OrdinalIgnoreCase))
                    .Select(x => x.ExternalId),
                StringComparer.OrdinalIgnoreCase);

            return (root["features"] as JArray ?? new JArray())
                .Select(feature => new
                {
                    Feature = feature,
                    Score = GetGeoapifyFeatureMatchScore(
                        feature,
                        targetName,
                        settlement.Place,
                        osmIds)
                })
                .Where(x => x.Score >= 50)
                .OrderByDescending(x => x.Score)
                .Select(x => x.Feature)
                .FirstOrDefault();
        }

        private static int GetGeoapifyFeatureMatchScore(
            JToken feature,
            string targetName,
            string settlementPlace,
            HashSet<string> osmIds)
        {
            var properties = feature["properties"];
            var score = 0;
            var rawOsmId = properties?["datasource"]?["raw"]?["osm_id"]?.ToString();

            if (!string.IsNullOrWhiteSpace(rawOsmId) && osmIds.Contains(rawOsmId))
            {
                score += 120;
            }

            if (NormalizeName(properties?["name"]?.Value<string>()) == targetName)
            {
                score += 100;
            }

            if (NormalizeName(properties?["city"]?.Value<string>()) == targetName)
            {
                score += 80;
            }

            var formatted = NormalizeName(properties?["formatted"]?.Value<string>());

            if (!string.IsNullOrWhiteSpace(targetName) && formatted.StartsWith(targetName))
            {
                score += 60;
            }

            var categories = properties?["categories"]?.ToString() ?? string.Empty;

            if (categories.Contains("administrative.city", StringComparison.OrdinalIgnoreCase) ||
                categories.Contains("administrative.town", StringComparison.OrdinalIgnoreCase) ||
                categories.Contains("administrative.village", StringComparison.OrdinalIgnoreCase))
            {
                score += 50;
            }

            if (!string.IsNullOrWhiteSpace(settlementPlace) &&
                categories.Contains(settlementPlace, StringComparison.OrdinalIgnoreCase))
            {
                score += 20;
            }

            return score;
        }

        private PointLatLng? TryGetAnchorLatLng(SettlementGeometryItem settlement)
        {
            if (settlement.FallbackPoint != null &&
                _tryConvertUtmToLatLng(
                    new PointF(
                        settlement.FallbackPoint.X,
                        settlement.FallbackPoint.Y),
                    out var centerLat,
                    out var centerLng))
            {
                return new PointLatLng(centerLat, centerLng);
            }

            var polygonPoints = SettlementGeometryMerger
                .GetPreferredPolygonLines(settlement)
                .SelectMany(SettlementGeometryService.ParsePolygonLine)
                .ToList();

            if (polygonPoints.Count == 0)
            {
                return null;
            }

            var average = new PointF(
                (float)polygonPoints.Average(x => x.X),
                (float)polygonPoints.Average(x => x.Y));

            return _tryConvertUtmToLatLng(average, out var lat, out var lng)
                ? new PointLatLng(lat, lng)
                : null;
        }

        private bool TryConvertForGeoJson(
            double lat,
            double lng,
            out PointF utm,
            out int zone,
            out string band)
        {
            return _tryConvertLatLngToUtm(
                lat,
                lng,
                out utm,
                out zone,
                out band);
        }

        private static HashSet<string> GetGMapGeocoderFamilies(
            IEnumerable<GMapProvider>? mapProviders)
        {
            var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var provider in mapProviders ?? Array.Empty<GMapProvider>())
            {
                if (provider is not GeocodingProvider)
                {
                    continue;
                }

                var identity =
                    (provider.GetType().FullName ?? string.Empty) +
                    " " +
                    provider.Name;

                if (identity.Contains("GraphHopper", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add("GraphHopper");
                }
                else if (identity.Contains("Google", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add("Google");
                }
                else if (identity.Contains("Bing", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add("Bing");
                }
                else if (identity.Contains("Yahoo", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add("Yahoo");
                }
                else if (identity.Contains("OpenStreet", StringComparison.OrdinalIgnoreCase) ||
                         identity.Contains("OpenCycle", StringComparison.OrdinalIgnoreCase) ||
                         identity.Contains("OSM", StringComparison.OrdinalIgnoreCase))
                {
                    result.Add("OpenStreetMap");
                }
            }

            return result;
        }

        private static bool HasCenterFromProvider(
            SettlementGeometryItem settlement,
            string provider)
        {
            return settlement.CenterCandidates.Any(x =>
                string.Equals(x.Provider, provider, StringComparison.OrdinalIgnoreCase));
        }

        private static void AddKeyedProvider(
            List<string> active,
            List<string> skipped,
            string provider,
            string? apiKey)
        {
            if (string.IsNullOrWhiteSpace(apiKey))
            {
                skipped.Add($"{provider}: немає API-ключа");
            }
            else
            {
                active.Add(provider);
            }
        }

        private static void AddGMapProvider(
            List<string> active,
            List<string> skipped,
            HashSet<string> families,
            string family,
            string provider,
            string? apiKey)
        {
            if (!families.Contains(family))
            {
                return;
            }

            AddKeyedProvider(active, skipped, provider, apiKey);
        }

        private static bool IsInsideBounds(
            PointLatLng point,
            SettlementCacheBounds bounds)
        {
            var north = Math.Max(bounds.Top, bounds.Bottom);
            var south = Math.Min(bounds.Top, bounds.Bottom);
            var east = Math.Max(bounds.Left, bounds.Right);
            var west = Math.Min(bounds.Left, bounds.Right);

            return point.Lat >= south &&
                   point.Lat <= north &&
                   point.Lng >= west &&
                   point.Lng <= east;
        }

        private static double GetDistanceMeters(
            PointLatLng first,
            PointLatLng second)
        {
            const double earthRadiusMeters = 6371000.0;
            var firstLat = first.Lat * Math.PI / 180.0;
            var secondLat = second.Lat * Math.PI / 180.0;
            var deltaLat = (second.Lat - first.Lat) * Math.PI / 180.0;
            var deltaLng = (second.Lng - first.Lng) * Math.PI / 180.0;
            var a = Math.Sin(deltaLat / 2.0) * Math.Sin(deltaLat / 2.0) +
                    Math.Cos(firstLat) * Math.Cos(secondLat) *
                    Math.Sin(deltaLng / 2.0) * Math.Sin(deltaLng / 2.0);

            return earthRadiusMeters *
                   2.0 *
                   Math.Atan2(Math.Sqrt(a), Math.Sqrt(1.0 - a));
        }

        private static string NormalizeName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return string.Empty;
            }

            return new string(
                value
                    .Trim()
                    .ToLowerInvariant()
                    .Where(char.IsLetterOrDigit)
                    .ToArray());
        }

        private static string CreateCoordinateId(PointLatLng point)
        {
            return string.Format(
                CultureInfo.InvariantCulture,
                "{0:0.0000000},{1:0.0000000}",
                point.Lat,
                point.Lng);
        }

        private sealed class GeocodedPoint
        {
            public GeocodedPoint(
                double? lat,
                double? lng,
                string? externalId)
            {
                if (lat.HasValue && lng.HasValue)
                {
                    Point = new PointLatLng(lat.Value, lng.Value);
                }

                ExternalId = string.IsNullOrWhiteSpace(externalId)
                    ? Point.HasValue
                        ? CreateCoordinateId(Point.Value)
                        : string.Empty
                    : externalId;
            }

            public PointLatLng? Point { get; }

            public string ExternalId { get; }
        }

        private sealed class ProviderLookupResult
        {
            public ProviderLookupResult(
                string provider,
                string externalId,
                int priority,
                IEnumerable<string> polygons,
                PointLatLng? center,
                string state,
                bool attempted = true)
            {
                Provider = provider;
                ExternalId = externalId;
                Priority = priority;
                Polygons = polygons
                    .Where(x => !string.IsNullOrWhiteSpace(x))
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToList();
                Center = center;
                State = state;
                Attempted = attempted;
            }

            public string Provider { get; }

            public string ExternalId { get; }

            public int Priority { get; }

            public IReadOnlyList<string> Polygons { get; }

            public PointLatLng? Center { get; }

            public string State { get; }

            public bool Attempted { get; }

            public static ProviderLookupResult Empty(
                string provider,
                string state)
            {
                return new ProviderLookupResult(
                    provider,
                    string.Empty,
                    SettlementDataSources.GetDefaultPriority(provider),
                    Array.Empty<string>(),
                    null,
                    state);
            }
        }
    }
}
