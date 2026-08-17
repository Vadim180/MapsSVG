using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace MapsWPF.Services.Settlements
{
    public sealed class SettlementOverpassLoader
    {
        private static readonly string[] OverpassUrls =
        {
            "https://overpass-api.de/api/interpreter",
            "https://overpass.private.coffee/api/interpreter",
            "https://maps.mail.ru/osm/tools/overpass/api/interpreter"
        };

        private const string OpenStreetMapApiBaseUrl =
            "https://api.openstreetmap.org/api/0.6";
        private const int OpenStreetMapRelationTimeoutSeconds = 20;

        private const double MaxResidentialAttachDistanceMeters = 8000.0;

        private const double SettlementTileSizeDegrees = 0.20;

        // Tiles are still requested sequentially; this pause prevents bursts
        // without adding several minutes to a normal small work area.
        private const int SettlementTileDelayMs = 1000;
        private const int SettlementTileTimeoutSeconds = 30;

        private const double ResidentialTileSizeDegrees = 0.10;
        private const int ResidentialTileDelayMs = 2000;

        // Normal relation requests remain sequential. 429/504 responses use
        // the longer adaptive backoffs declared below.
        private const int RelationGeometryDelayMs = 250;
        private const int RelationGeometryRequestTimeoutSeconds = 60;
        private const int RelationGeometryDelayAfter429Ms = 15000;
        private const int RelationGeometryDelayAfterServerErrorMs = 5000;
        private const int MaxConsecutiveRelationFailures = 3;

        private const double MaxRelationAttachDistanceMeters = 12000.0;

        private static readonly HashSet<string> AllowedPlaceTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "city",
                "town",
                "village",
                "hamlet",
                "isolated_dwelling"
            };

        private static readonly HashSet<string> AllowedBoundaryAdminLevels =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "9",
                "10",
                "11"
            };

        private readonly HttpClient _httpClient = new();
        private readonly TryConvertLatLngToUtmDelegate _tryConvertLatLngToUtm;
        private int _preferredOverpassEndpointIndex;

        public delegate bool TryConvertLatLngToUtmDelegate(
            double lat,
            double lng,
            out PointF utm,
            out int zone,
            out string band);

        private delegate bool TryConvertToUtm(
            double lat,
            double lng,
            out PointF utm);

        public SettlementOverpassLoader(TryConvertLatLngToUtmDelegate tryConvertLatLngToUtm)
        {
            _tryConvertLatLngToUtm = tryConvertLatLngToUtm
                ?? throw new ArgumentNullException(nameof(tryConvertLatLngToUtm));

            _httpClient.DefaultRequestHeaders.TryAddWithoutValidation(
                "User-Agent",
                "MapsWPF/1.0"
            );
        }

        public async Task<List<string>> LoadRelationPolygonsForTestAsync(
    long relationId,
    CancellationToken cancellationToken = default)
        {
            int? expectedZone = null;
            var expectedBand = string.Empty;

            TryConvertToUtm tryConvert = (
                double lat,
                double lng,
                out PointF utm) =>
            {
                utm = PointF.Empty;

                if (!_tryConvertLatLngToUtm(
                        lat,
                        lng,
                        out var converted,
                        out var zone,
                        out var band))
                {
                    return false;
                }

                if (!expectedZone.HasValue)
                {
                    expectedZone = zone;
                    expectedBand = band;
                }

                if (zone != expectedZone.Value ||
                    !string.Equals(
                        band,
                        expectedBand,
                        StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }

                utm = converted;
                return true;
            };

            Debug.WriteLine(
                $"[SETTLEMENT TEST] Relation {relationId}: loading started");

            var rings = await LoadRelationOuterPolygonsByIdAsync(
                relationId,
                cancellationToken);

            var polygonLines = new List<string>();

            foreach (var ring in rings)
            {
                if (string.IsNullOrWhiteSpace(ring.OuterRing))
                {
                    continue;
                }

                polygonLines.Add(ring.OuterRing);
            }

            Debug.WriteLine(
                $"[SETTLEMENT TEST] Relation {relationId}: " +
                $"rings={rings.Count}, polygons={polygonLines.Count}, " +
                $"zone={expectedZone}, band={expectedBand}");

            return polygonLines;
        }

        public async Task<SettlementGeometryCache> LoadAsync(
     SettlementCacheBounds bounds,
     CancellationToken cancellationToken = default,
     Action<SettlementGeometryCache>? onPartialCacheReady = null)
        {
            if (bounds == null)
            {
                throw new ArgumentNullException(nameof(bounds));
            }

            int? cacheZone = null;
            string cacheBand = string.Empty;

            TryConvertToUtm tryConvert = (
                double lat,
                double lng,
                out PointF utm) =>
            {
                utm = PointF.Empty;

                if (!_tryConvertLatLngToUtm(
                        lat,
                        lng,
                        out var converted,
                        out var zone,
                        out var band))
                {
                    return false;
                }

                if (!cacheZone.HasValue)
                {
                    cacheZone = zone;
                    cacheBand = band ?? string.Empty;
                }

                utm = converted;
                return true;
            };

            var settlements = await LoadSettlementsByTilesAsync(
     bounds,
     tryConvert,
     cancellationToken,
     currentSettlements =>
     {
         onPartialCacheReady?.Invoke(
             CreateCacheSnapshot(
                 bounds,
                 currentSettlements,
                 cacheZone,
                 cacheBand,
                 "overpass-place-nodes-tiles"));
     });

            settlements = Deduplicate(settlements);

            Debug.WriteLine(
                $"[SETTLEMENT LOAD] Parsed settlements before residential: {settlements.Count}");

            onPartialCacheReady?.Invoke(
                CreateCacheSnapshot(
                    bounds,
                    settlements,
                    cacheZone,
                    cacheBand,
                    "overpass-place-nodes-tiles"));

            var relationLoad = await LoadRelationGeometryCandidatesAsync(
     bounds,
     cancellationToken);
            var relationCandidates = relationLoad.Candidates;

            await AttachRelationGeometriesAsync(
     settlements,
     relationCandidates,
     tryConvert,
     cancellationToken,
     () =>
     {
         onPartialCacheReady?.Invoke(
             CreateCacheSnapshot(
                 bounds,
                 settlements,
                 cacheZone,
                 cacheBand,
                 "overpass"));
     });

            Debug.WriteLine(
                $"[SETTLEMENT LOAD] Parsed settlements={settlements.Count}, " +
                $"relationCandidates={relationCandidates.Count}");

            return CreateCacheSnapshot(
     bounds,
     settlements,
     cacheZone,
     cacheBand,
      "overpass");
        }

        public async Task<SettlementGeometryCache> LoadContoursAsync(
            SettlementCacheBounds bounds,
            CancellationToken cancellationToken = default,
            Action<SettlementGeometryCache>? onSnapshotReady = null,
            Action<SettlementContourLoadProgress>? onProgress = null,
            SettlementGeometryCache? existingCache = null,
            Func<SettlementGeometryItem, CancellationToken,
                Task<SettlementCityProviderResult>>? enrichSettlementAsync = null,
            bool forceRefresh = false)
        {
            if (bounds == null)
            {
                throw new ArgumentNullException(nameof(bounds));
            }

            int? cacheZone = null;
            string cacheBand = string.Empty;

            TryConvertToUtm tryConvert = (
                double lat,
                double lng,
                out PointF utm) =>
            {
                utm = PointF.Empty;

                if (!_tryConvertLatLngToUtm(
                        lat,
                        lng,
                        out var converted,
                        out var zone,
                        out var band))
                {
                    return false;
                }

                if (!cacheZone.HasValue)
                {
                    cacheZone = zone;
                    cacheBand = band ?? string.Empty;
                }

                utm = converted;
                return true;
            };

            var settlements = await LoadSettlementsByTilesAsync(
                bounds,
                tryConvert,
                cancellationToken,
                currentSettlements =>
                {
                    onSnapshotReady?.Invoke(
                        CreateCacheSnapshot(
                            bounds,
                            currentSettlements,
                            cacheZone,
                            cacheBand,
                            "overpass-place-nodes-tiles"));
                });

            settlements = Deduplicate(settlements);

            // Старий контур зберігаємо до отримання успішної відповіді
            // провайдера. Це не дає примусовому оновленню стерти справну
            // геометрію через тимчасовий timeout/429/5xx.
            MergeExistingGeometryForContourQueue(
                existingCache?.Settlements,
                settlements);

            onProgress?.Invoke(new SettlementContourLoadProgress
            {
                Completed = 0,
                Total = 0,
                State = "Отримання списку relation..."
            });

            var relationLoad = await LoadRelationGeometryCandidatesAsync(
                bounds,
                cancellationToken);
            var relationCandidates = relationLoad.Candidates;

            var relationsBySettlement =
                new Dictionary<SettlementGeometryItem, List<RelationGeometryCandidate>>();

            foreach (var candidate in relationCandidates)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var target = FindSettlementTargetForRelation(
                    settlements,
                    candidate);

                if (target == null)
                {
                    if (candidate.IsAdministrativeBoundaryOnly)
                    {
                        // Не створюємо «новий населений пункт» лише з
                        // адміністративної relation: це може бути громада.
                        // Таку relation використовуємо тільки як кандидат
                        // межі вже знайденого однойменного НП.
                        continue;
                    }

                    target = CreateSettlementFromRelationCandidate(candidate);

                    CopyExistingGeometry(
                        FindExistingSettlement(
                            existingCache?.Settlements,
                            target),
                        target);

                    settlements.Add(target);
                }

                SettlementGeometryMerger.AddProviderReference(
                    target,
                    SettlementDataSources.OpenStreetMapOverpass,
                    candidate.RelationId.ToString(CultureInfo.InvariantCulture),
                    "relation");

                foreach (var alias in candidate.NameAliases)
                {
                    if (!target.NameAliases.Contains(
                            alias,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        target.NameAliases.Add(alias);
                    }
                }

                if (string.IsNullOrWhiteSpace(target.Boundary))
                {
                    target.Boundary = candidate.Boundary;
                }

                if (string.IsNullOrWhiteSpace(target.AdminLevel))
                {
                    target.AdminLevel = candidate.AdminLevel;
                }

                if (string.IsNullOrWhiteSpace(target.Region))
                {
                    target.Region = candidate.Region;
                }

                if (string.IsNullOrWhiteSpace(target.District))
                {
                    target.District = candidate.District;
                }

                if (target.FallbackPoint == null && candidate.Center != null)
                {
                    SettlementGeometryMerger.AddCenterCandidate(
                        target,
                        candidate.Center,
                        SettlementDataSources.OpenStreetMapOverpass,
                        candidate.RelationId.ToString(CultureInfo.InvariantCulture),
                        SettlementDataSources.OpenStreetMapPriority);
                }

                if (!relationsBySettlement.TryGetValue(target, out var targetRelations))
                {
                    targetRelations = new List<RelationGeometryCandidate>();
                    relationsBySettlement[target] = targetRelations;
                }

                if (targetRelations.All(x => x.RelationId != candidate.RelationId))
                {
                    targetRelations.Add(candidate);
                }
            }

            if (forceRefresh && relationLoad.Succeeded)
            {
                // Якщо список relation завантажився коректно, відсутність
                // кандидата для конкретного НП є остаточною відповіддю, а не
                // тимчасовою помилкою. Старий OSM-полігон можна прибрати.
                foreach (var settlement in settlements.Where(x =>
                             !relationsBySettlement.ContainsKey(x)))
                {
                    SettlementGeometryMerger.RemoveProviderCandidates(
                        settlement,
                        SettlementDataSources.OpenStreetMapOverpass,
                        removeCenters: false,
                        removePolygons: true);
                }
            }

            settlements = settlements
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ThenBy(x => x.Place, StringComparer.OrdinalIgnoreCase)
                .ToList();

            var total = settlements.Count;

            onProgress?.Invoke(new SettlementContourLoadProgress
            {
                Completed = 0,
                Total = total,
                State = "Черга контурів сформована"
            });

            onSnapshotReady?.Invoke(
                CreateCacheSnapshot(
                    bounds,
                    settlements,
                    cacheZone,
                    cacheBand,
                    "overpass-place-nodes-tiles"));

            var consecutiveRelationFailures = 0;
            var relationProviderDisabled = false;
            var retryTargets = new List<SettlementRetryTarget>();

            for (var i = 0; i < settlements.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var settlement = settlements[i];
                var hadTransientRelationFailure = false;
                var receivedDefinitiveRelationResponse = false;
                var receivedRelationGeometry = false;
                var supplementalTaskFailed = false;
                var openStreetMapBeforeRefresh = forceRefresh
                    ? SettlementGeometryMerger.CloneSettlementItem(settlement)
                    : null;
                relationsBySettlement.TryGetValue(
                    settlement,
                    out var relationCandidatesForSettlement);

                Task<SettlementCityProviderResult>? supplementalTask = null;

                if (enrichSettlementAsync != null)
                {
                    supplementalTask = enrichSettlementAsync(
                        SettlementGeometryMerger.CloneSettlementItem(settlement),
                        cancellationToken);

                    onProgress?.Invoke(new SettlementContourLoadProgress
                    {
                        Completed = i,
                        Total = total,
                        CurrentSettlementName = settlement.Name,
                        State = "Паралельний запит до доступних провайдерів...",
                        HasFallbackCenter = settlement.FallbackPoint != null
                    });
                }

                var hasPolygon = HasUsablePolygons(settlement);
                var hasOpenStreetMapPolygon =
                    !forceRefresh &&
                    SettlementGeometryMerger.HasPolygonsFromProvider(
                        settlement,
                        SettlementDataSources.OpenStreetMapOverpass);

                var state = hasOpenStreetMapPolygon
                    ? "Контур OpenStreetMap уже є в кеші"
                    : hasPolygon
                        ? "Збережено контур іншого джерела"
                        : "Центр збережено, relation не знайдена";
                var delayAfterCurrentAttemptMs = 0;

                if (!hasOpenStreetMapPolygon &&
                    relationCandidatesForSettlement != null &&
                    relationCandidatesForSettlement.Count > 0 &&
                    !relationProviderDisabled)
                {
                    onProgress?.Invoke(new SettlementContourLoadProgress
                    {
                        Completed = i,
                        Total = total,
                        CurrentSettlementName = settlement.Name,
                        State = relationCandidatesForSettlement.Count > 1
                            ? "Перевірка кількох кандидатів Overpass..."
                            : supplementalTask == null
                                ? "Витягування контуру..."
                                : "Overpass і зовнішні API працюють паралельно...",
                        HasFallbackCenter = settlement.FallbackPoint != null
                    });

                    for (var relationIndex = 0;
                         relationIndex < relationCandidatesForSettlement.Count;
                         relationIndex++)
                    {
                        var relationCandidate =
                            relationCandidatesForSettlement[relationIndex];
                        delayAfterCurrentAttemptMs = RelationGeometryDelayMs;

                        try
                        {
                            var rings = await LoadRelationOuterPolygonsByIdAsync(
                                relationCandidate.RelationId,
                                cancellationToken);

                            consecutiveRelationFailures = 0;
                            receivedDefinitiveRelationResponse = true;

                            if (rings.Count == 0)
                            {
                                state = "Relation не містить замкненого контуру";
                                continue;
                            }

                            receivedRelationGeometry = true;

                            if (forceRefresh)
                            {
                                SettlementGeometryMerger.RemoveProviderCandidates(
                                    settlement,
                                    SettlementDataSources.OpenStreetMapOverpass,
                                    removeCenters: false,
                                    removePolygons: true);
                            }

                            var added = AttachRingsToSettlement(
                                settlement,
                                rings,
                                relationCandidate);

                            // Перевіряємо новий кандидат у контексті всіх НП,
                            // а не лише поточного міста.
                            SettlementGeometryQualitySelector.RecalculateAll(
                                settlements);
                            hasPolygon = HasUsablePolygons(settlement);
                            hasOpenStreetMapPolygon =
                                SettlementGeometryMerger.HasPolygonsFromProvider(
                                    settlement,
                                    SettlementDataSources.OpenStreetMapOverpass);

                            if (hasOpenStreetMapPolygon)
                            {
                                settlement.GeometrySourceOsmType = "relation";
                                settlement.GeometrySourceOsmId =
                                    relationCandidate.RelationId;
                                state = added > 0
                                    ? "Контур завантажено"
                                    : "Контур знайдено в кеші";
                                break;
                            }

                            var relationRejectionReason =
                                SettlementGeometryQualitySelector
                                    .GetPreferredRejectionReason(settlement);
                            state = !string.IsNullOrWhiteSpace(
                                    relationRejectionReason)
                                ? $"Кандидат relation відкинуто: " +
                                  relationRejectionReason
                                : "Кандидат relation не пройшов перевірку";
                        }
                        catch (Exception ex) when (
                            ex is TimeoutException ||
                            ex is HttpRequestException ||
                            ex is TaskCanceledException)
                        {
                            consecutiveRelationFailures++;
                            hadTransientRelationFailure = true;
                            delayAfterCurrentAttemptMs =
                                GetRelationDelayAfterErrorMs(ex);

                            state = settlement.FallbackPoint != null
                                ? "Помилка контуру, показано центр"
                                : "Помилка завантаження контуру";

                            Debug.WriteLine(
                                $"[SETTLEMENT CONTOUR QUEUE] {settlement.Name}: " +
                                $"{ex.Message}");
                        }

                        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
                        {
                            consecutiveRelationFailures++;
                            hadTransientRelationFailure = true;
                            delayAfterCurrentAttemptMs =
                                RelationGeometryDelayAfterServerErrorMs;

                            state = settlement.FallbackPoint != null
                                ? "Помилка контуру, показано центр"
                                : "Помилка завантаження контуру";

                            Debug.WriteLine(
                                $"[SETTLEMENT CONTOUR QUEUE] {settlement.Name}: " +
                                $"unexpected error: {ex}");
                        }

                        if (consecutiveRelationFailures >=
                            MaxConsecutiveRelationFailures)
                        {
                            relationProviderDisabled = true;
                            delayAfterCurrentAttemptMs = 0;
                        }

                        if (hasOpenStreetMapPolygon || relationProviderDisabled)
                        {
                            break;
                        }

                        if (relationIndex < relationCandidatesForSettlement.Count - 1 &&
                            delayAfterCurrentAttemptMs > 0)
                        {
                            await Task.Delay(
                                delayAfterCurrentAttemptMs,
                                cancellationToken);
                            delayAfterCurrentAttemptMs = 0;
                        }
                    }
                }

                if (forceRefresh &&
                    relationCandidatesForSettlement?.Count > 0 &&
                    !hasOpenStreetMapPolygon)
                {
                    if (hadTransientRelationFailure)
                    {
                        RestoreProviderPolygons(
                            openStreetMapBeforeRefresh,
                            settlement,
                            SettlementDataSources.OpenStreetMapOverpass);
                    }
                    else if (receivedDefinitiveRelationResponse &&
                             !receivedRelationGeometry)
                    {
                        // Усі доступні відповіді були остаточними, але нового
                        // придатного полігона немає: старий OSM-кандидат більше
                        // не вважаємо актуальним.
                        SettlementGeometryMerger.RemoveProviderCandidates(
                            settlement,
                            SettlementDataSources.OpenStreetMapOverpass,
                            removeCenters: false,
                            removePolygons: true);
                    }
                }

                SettlementCityProviderResult? supplementalResult = null;

                if (supplementalTask != null)
                {
                    try
                    {
                        supplementalResult = await supplementalTask;

                        if (forceRefresh)
                        {
                            SettlementGeometryMerger
                                .MergeSettlementCandidatesReplacingProviders(
                                    settlement,
                                    supplementalResult.Settlement,
                                    supplementalResult.RefreshedCenterProviders,
                                    supplementalResult.RefreshedPolygonProviders);
                        }
                        else
                        {
                            SettlementGeometryMerger.MergeSettlementCandidates(
                                settlement,
                                supplementalResult.Settlement);
                        }
                    }
                    catch (OperationCanceledException) when (
                        cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        supplementalTaskFailed = true;
                        Debug.WriteLine(
                            $"[SETTLEMENT PROVIDER POOL] {settlement.Name}: {ex}");
                    }
                }

                SettlementGeometryQualitySelector.RecalculateAll(settlements);
                hasPolygon = HasUsablePolygons(settlement);

                var relationWasSkippedAfterFailures =
                    (forceRefresh || !hasPolygon) &&
                    relationProviderDisabled &&
                    relationCandidatesForSettlement?.Count > 0;
                var retryExternal =
                    (forceRefresh || !hasPolygon) &&
                    (supplementalTaskFailed ||
                     supplementalResult?.HadTransientPolygonFailure == true);

                if ((forceRefresh || !hasPolygon) &&
                    (hadTransientRelationFailure ||
                     relationWasSkippedAfterFailures ||
                     retryExternal))
                {
                    retryTargets.Add(new SettlementRetryTarget
                    {
                        Settlement = settlement,
                        RelationCandidates =
                            relationCandidatesForSettlement ??
                            new List<RelationGeometryCandidate>(),
                        RetryOpenStreetMap =
                            hadTransientRelationFailure ||
                            relationWasSkippedAfterFailures,
                        RetryExternalProviders = retryExternal
                    });
                }

                var rejectionReason = SettlementGeometryQualitySelector
                    .GetPreferredRejectionReason(settlement);

                var preferredProvider =
                    SettlementGeometryQualitySelector.GetPreferredPolygonProvider(
                        settlement);

                if (!string.IsNullOrWhiteSpace(preferredProvider))
                {
                    var candidateProviderCount = settlement.PolygonCandidates
                        .Where(SettlementGeometryQualitySelector.IsUsablePolygonCandidate)
                        .Select(x => x.Provider)
                        .Distinct(StringComparer.OrdinalIgnoreCase)
                        .Count();

                    state = candidateProviderCount > 1
                        ? $"Вибрано контур: {preferredProvider} " +
                          $"({candidateProviderCount} кандидатів)"
                        : $"Вибрано контур: {preferredProvider}";

                    if (relationProviderDisabled)
                    {
                        state += "; Overpass призупинено після 3 помилок";
                    }
                }
                else if (settlement.FallbackPoint != null)
                {
                    state = !string.IsNullOrWhiteSpace(rejectionReason)
                        ? $"Контур відкинуто: {rejectionReason}; показано центр"
                        : supplementalResult?.AttemptedProviderCount > 0
                            ? "Контур не знайдено, вибрано найкращий центр"
                            : state;

                    if (relationProviderDisabled)
                    {
                        state += "; Overpass призупинено після 3 помилок";
                    }
                }

                onSnapshotReady?.Invoke(
                    CreateCacheSnapshot(
                        bounds,
                        settlements,
                        cacheZone,
                        cacheBand,
                        "overpass-contours"));

                onProgress?.Invoke(new SettlementContourLoadProgress
                {
                    Completed = i + 1,
                    Total = total,
                    CurrentSettlementName = settlement.Name,
                    State = state,
                    HasPolygon = hasPolygon,
                    HasFallbackCenter = settlement.FallbackPoint != null,
                    OverallPercent = total <= 0
                        ? 90.0
                        : (i + 1) * 90.0 / total
                });

                if (delayAfterCurrentAttemptMs > 0 && i < settlements.Count - 1)
                {
                    await Task.Delay(
                        delayAfterCurrentAttemptMs,
                        cancellationToken);
                }
            }

            await RetryTransientPolygonFailuresAsync(
                bounds,
                settlements,
                retryTargets,
                cacheZone,
                cacheBand,
                enrichSettlementAsync,
                forceRefresh,
                cancellationToken,
                onSnapshotReady,
                onProgress);

            return CreateCacheSnapshot(
                bounds,
                settlements,
                cacheZone,
                cacheBand,
                "overpass-contours");
        }

        private async Task RetryTransientPolygonFailuresAsync(
            SettlementCacheBounds bounds,
            List<SettlementGeometryItem> settlements,
            List<SettlementRetryTarget> retryTargets,
            int? cacheZone,
            string cacheBand,
            Func<SettlementGeometryItem, CancellationToken,
                Task<SettlementCityProviderResult>>? enrichSettlementAsync,
            bool forceRefresh,
            CancellationToken cancellationToken,
            Action<SettlementGeometryCache>? onSnapshotReady,
            Action<SettlementContourLoadProgress>? onProgress)
        {
            if (retryTargets.Count == 0)
            {
                onProgress?.Invoke(new SettlementContourLoadProgress
                {
                    Completed = settlements.Count,
                    Total = settlements.Count,
                    State = "Завантаження завершено; повторних спроб не потрібно",
                    OverallPercent = 100.0
                });
                return;
            }

            for (var index = 0; index < retryTargets.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var target = retryTargets[index];
                var settlement = target.Settlement;

                if (HasUsablePolygons(settlement) && !forceRefresh)
                {
                    continue;
                }

                var openStreetMapBeforeRetry = forceRefresh
                    ? SettlementGeometryMerger.CloneSettlementItem(settlement)
                    : null;
                var retryHadTransientRelationFailure = false;
                var receivedDefinitiveRelationResponse = false;
                var receivedRelationGeometry = false;
                var refreshedOpenStreetMapPolygon = false;
                var keptCachedPolygonAfterFailure = false;

                onProgress?.Invoke(new SettlementContourLoadProgress
                {
                    Completed = index,
                    Total = retryTargets.Count,
                    CurrentSettlementName = settlement.Name,
                    State = "Повторна спроба після тимчасової помилки...",
                    HasFallbackCenter = settlement.FallbackPoint != null,
                    OverallPercent = 90.0 +
                        index * 10.0 / retryTargets.Count
                });

                Task<SettlementCityProviderResult>? externalTask = null;

                if (target.RetryExternalProviders &&
                    enrichSettlementAsync != null)
                {
                    externalTask = enrichSettlementAsync(
                        SettlementGeometryMerger.CloneSettlementItem(settlement),
                        cancellationToken);
                }

                if (target.RetryOpenStreetMap)
                {
                    foreach (var relation in target.RelationCandidates)
                    {
                        try
                        {
                            var rings = await LoadRelationOuterPolygonsByIdAsync(
                                relation.RelationId,
                                cancellationToken);

                            receivedDefinitiveRelationResponse = true;

                            if (rings.Count == 0)
                            {
                                continue;
                            }

                            receivedRelationGeometry = true;

                            if (forceRefresh)
                            {
                                SettlementGeometryMerger.RemoveProviderCandidates(
                                    settlement,
                                    SettlementDataSources.OpenStreetMapOverpass,
                                    removeCenters: false,
                                    removePolygons: true);
                            }

                            AttachRingsToSettlement(
                                settlement,
                                rings,
                                relation);
                            SettlementGeometryQualitySelector.RecalculateAll(
                                settlements);

                            if (SettlementGeometryMerger.HasPolygonsFromProvider(
                                    settlement,
                                    SettlementDataSources.OpenStreetMapOverpass))
                            {
                                refreshedOpenStreetMapPolygon = true;
                                settlement.GeometrySourceOsmType = "relation";
                                settlement.GeometrySourceOsmId = relation.RelationId;
                                break;
                            }
                        }
                        catch (Exception ex) when (
                            !cancellationToken.IsCancellationRequested &&
                            (ex is TimeoutException ||
                             ex is HttpRequestException ||
                             ex is TaskCanceledException))
                        {
                            retryHadTransientRelationFailure = true;
                            Debug.WriteLine(
                                $"[SETTLEMENT RETRY] {settlement.Name}, " +
                                $"Overpass: {ex.Message}");
                        }
                        catch (Exception ex) when (
                            !cancellationToken.IsCancellationRequested)
                        {
                            retryHadTransientRelationFailure = true;
                            Debug.WriteLine(
                                $"[SETTLEMENT RETRY] {settlement.Name}, " +
                                $"Overpass unexpected error: {ex}");
                        }
                    }

                    if (forceRefresh && !refreshedOpenStreetMapPolygon)
                    {
                        if (retryHadTransientRelationFailure)
                        {
                            RestoreProviderPolygons(
                                openStreetMapBeforeRetry,
                                settlement,
                                SettlementDataSources.OpenStreetMapOverpass);
                            keptCachedPolygonAfterFailure =
                                SettlementGeometryMerger.HasPolygonsFromProvider(
                                    settlement,
                                    SettlementDataSources.OpenStreetMapOverpass);
                        }
                        else if (receivedDefinitiveRelationResponse &&
                                 !receivedRelationGeometry)
                        {
                            SettlementGeometryMerger.RemoveProviderCandidates(
                                settlement,
                                SettlementDataSources.OpenStreetMapOverpass,
                                removeCenters: false,
                                removePolygons: true);
                        }
                    }
                }

                if (externalTask != null)
                {
                    try
                    {
                        var externalResult = await externalTask;

                        if (forceRefresh)
                        {
                            SettlementGeometryMerger
                                .MergeSettlementCandidatesReplacingProviders(
                                    settlement,
                                    externalResult.Settlement,
                                    externalResult.RefreshedCenterProviders,
                                    externalResult.RefreshedPolygonProviders);
                        }
                        else
                        {
                            SettlementGeometryMerger.MergeSettlementCandidates(
                                settlement,
                                externalResult.Settlement);
                        }
                    }
                    catch (OperationCanceledException) when (
                        cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine(
                            $"[SETTLEMENT RETRY] {settlement.Name}, " +
                            $"external providers: {ex.Message}");
                    }
                }

                SettlementGeometryQualitySelector.RecalculateAll(settlements);
                var hasPolygon = HasUsablePolygons(settlement);

                onSnapshotReady?.Invoke(
                    CreateCacheSnapshot(
                        bounds,
                        settlements,
                        cacheZone,
                        cacheBand,
                        "provider-retry"));

                onProgress?.Invoke(new SettlementContourLoadProgress
                {
                    Completed = index + 1,
                    Total = retryTargets.Count,
                    CurrentSettlementName = settlement.Name,
                    State = keptCachedPolygonAfterFailure
                        ? "Провайдер тимчасово недоступний; залишено попередній контур"
                        : hasPolygon
                        ? "Контур отримано з повторної спроби"
                        : "Повторна спроба не дала полігон; залишено центр",
                    HasPolygon = hasPolygon,
                    HasFallbackCenter = settlement.FallbackPoint != null,
                    OverallPercent = 90.0 +
                        (index + 1) * 10.0 / retryTargets.Count
                });

                if (index < retryTargets.Count - 1)
                {
                    await Task.Delay(500, cancellationToken);
                }
            }
        }

        private static SettlementGeometryCache CreateCacheSnapshot(
    SettlementCacheBounds bounds,
    List<SettlementGeometryItem> settlements,
    int? cacheZone,
    string cacheBand,
    string source)
        {
            var cache = new SettlementGeometryCache
            {
                Source = source,
                Sources = new List<string>
                {
                    SettlementDataSources.OpenStreetMapOverpass
                },
                CoordinateSystem = "UTM",
                UtmZone = cacheZone ?? 37,
                UtmBand = string.IsNullOrWhiteSpace(cacheBand) ? "U" : cacheBand,
                Bounds = bounds,
                Settlements = settlements
            };

            SettlementGeometryMerger.Normalize(cache);
            return cache;
        }

        private async Task<RelationCandidateLoadResult> LoadRelationGeometryCandidatesAsync(
            SettlementCacheBounds bounds,
            CancellationToken cancellationToken)
        {
            try
            {
                var query = BuildRelationListQuery(bounds);

                var json = await ExecuteOverpassQueryAsync(
                    query,
                    TimeSpan.FromSeconds(30),
                    cancellationToken);

                var candidates = ParseRelationGeometryCandidates(json);

                Debug.WriteLine(
                    $"[SETTLEMENT RELATION] Candidates loaded: {candidates.Count}");

                return new RelationCandidateLoadResult
                {
                    Candidates = candidates,
                    Succeeded = true
                };
            }
            catch (Exception ex) when (
                ex is TimeoutException ||
                ex is HttpRequestException ||
                ex is TaskCanceledException)
            {
                Debug.WriteLine(
                    $"[SETTLEMENT RELATION] Candidates skipped: {ex.Message}");

                return new RelationCandidateLoadResult();
            }
            catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
            {
                Debug.WriteLine(
                    $"[SETTLEMENT RELATION] Candidates parse error: {ex}");

                return new RelationCandidateLoadResult();
            }
        }

        private static string BuildRelationListQuery(SettlementCacheBounds bounds)
        {
            var south = Math.Min(bounds.Bottom, bounds.Top);
            var north = Math.Max(bounds.Bottom, bounds.Top);
            var west = Math.Min(bounds.Left, bounds.Right);
            var east = Math.Max(bounds.Left, bounds.Right);

            return
                "[out:json][timeout:30];" +
                "(" +
                $"relation[\"place\"~\"^(city|town|village|hamlet|isolated_dwelling)$\"]({ToInvariant(south)},{ToInvariant(west)},{ToInvariant(north)},{ToInvariant(east)});" +
                $"relation[\"boundary\"=\"administrative\"][\"admin_level\"~\"^(9|10|11)$\"]({ToInvariant(south)},{ToInvariant(west)},{ToInvariant(north)},{ToInvariant(east)});" +
                ");" +
                "out body center;";
        }

        private List<RelationGeometryCandidate> ParseRelationGeometryCandidates(
            string json)
        {
            var result = new List<RelationGeometryCandidate>();

            var root = JObject.Parse(json);
            var elements = root["elements"] as JArray;

            if (elements == null)
            {
                return result;
            }

            foreach (var token in elements)
            {
                if (token is not JObject element)
                {
                    continue;
                }

                var type = GetString(element["type"]);

                if (!string.Equals(type, "relation", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var tags = element["tags"] as JObject;

                var place = GetTag(tags, "place");
                var boundary = GetTag(tags, "boundary");
                var adminLevel = GetTag(tags, "admin_level");
                var isPlaceRelation = AllowedPlaceTypes.Contains(place);
                var isAdministrativeBoundary =
                    string.Equals(
                        boundary,
                        "administrative",
                        StringComparison.OrdinalIgnoreCase) &&
                    AllowedBoundaryAdminLevels.Contains(adminLevel);

                if (!isPlaceRelation && !isAdministrativeBoundary)
                {
                    continue;
                }

                var name = GetBestName(tags);

                if (!IsUsableSettlementName(name))
                {
                    continue;
                }

                var relationId = GetLong(element["id"]);

                if (relationId <= 0)
                {
                    continue;
                }

                SettlementPoint? center = null;

                if (TryReadLatLng(element["center"], out var lat, out var lng) &&
                    TryCreateSettlementPoint(lat, lng, out var centerUtm))
                {
                    center = centerUtm;
                }

                var labelNodeIds = ReadSettlementLabelNodeIds(element);

                result.Add(new RelationGeometryCandidate
                {
                    Name = name,
                    NameAliases = GetNameAliases(tags),
                    Place = isPlaceRelation ? place : string.Empty,
                    Boundary = boundary,
                    AdminLevel = adminLevel,
                    Region = GetFirstNonEmptyTag(
                        tags,
                        "addr:region",
                        "is_in:state",
                        "is_in:region"),
                    District = GetFirstNonEmptyTag(
                        tags,
                        "addr:district",
                        "is_in:district"),
                    IsAdministrativeBoundaryOnly = !isPlaceRelation,
                    RelationId = relationId,
                    Center = center,
                    LabelNodeIds = labelNodeIds
                });
            }

            return DeduplicateRelationGeometryCandidates(result);
        }

        internal static List<long> ReadSettlementLabelNodeIds(
            JObject relationElement)
        {
            return (relationElement?["members"] as JArray)?
                .OfType<JObject>()
                .Where(member =>
                    string.Equals(
                        GetString(member["type"]),
                        "node",
                        StringComparison.OrdinalIgnoreCase) &&
                    (string.Equals(
                         GetString(member["role"]),
                         "label",
                         StringComparison.OrdinalIgnoreCase) ||
                     string.Equals(
                         GetString(member["role"]),
                         "admin_centre",
                         StringComparison.OrdinalIgnoreCase)))
                .Select(member => GetLong(member["ref"]))
                .Where(id => id > 0)
                .Distinct()
                .ToList() ?? new List<long>();
        }

        private static List<RelationGeometryCandidate> DeduplicateRelationGeometryCandidates(
            List<RelationGeometryCandidate> candidates)
        {
            var result = new List<RelationGeometryCandidate>();
            var seen = new HashSet<long>();

            foreach (var candidate in candidates)
            {
                if (candidate.RelationId <= 0)
                {
                    continue;
                }

                if (!seen.Add(candidate.RelationId))
                {
                    continue;
                }

                result.Add(candidate);
            }

            return result;
        }

        private async Task AttachRelationGeometriesAsync(
     List<SettlementGeometryItem> settlements,
     List<RelationGeometryCandidate> relationCandidates,
     TryConvertToUtm tryConvert,
     CancellationToken cancellationToken,
     Action? onGeometryAttached)
        {
            if (settlements == null)
            {
                return;
            }

            if (relationCandidates == null || relationCandidates.Count == 0)
            {
                Debug.WriteLine("[SETTLEMENT RELATION] No relation candidates");
                return;
            }

            Debug.WriteLine(
                $"[SETTLEMENT RELATION] Geometry loading started: {relationCandidates.Count}");

            for (var i = 0; i < relationCandidates.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var candidate = relationCandidates[i];
                var delayAfterCurrentAttemptMs = RelationGeometryDelayMs;

                var existingTarget = FindSettlementTargetForRelation(
                    settlements,
                    candidate);

                if (SettlementGeometryMerger.HasPolygonsFromProvider(
                        existingTarget,
                        SettlementDataSources.OpenStreetMapOverpass))
                {
                    Debug.WriteLine(
                        $"[SETTLEMENT RELATION] {i + 1}/{relationCandidates.Count}: " +
                        $"{candidate.Name}, relation={candidate.RelationId} skipped: already has polygon");

                    continue;
                }

                try
                {
                    Debug.WriteLine(
                        $"[SETTLEMENT RELATION] {i + 1}/{relationCandidates.Count}: " +
                        $"{candidate.Name}, relation={candidate.RelationId}");

                    var rings = await LoadRelationOuterPolygonsByIdAsync(
                        candidate.RelationId,
                        cancellationToken);

                    Debug.WriteLine(
                        $"[SETTLEMENT RELATION] {candidate.Name}: rings={rings.Count}");

                    if (rings.Count == 0)
                    {
                        continue;
                    }

                    var target = existingTarget;

                    if (target == null)
                    {
                        if (candidate.IsAdministrativeBoundaryOnly)
                        {
                            continue;
                        }

                        target = CreateSettlementFromRelationCandidate(candidate);
                        settlements.Add(target);
                    }

                    var added = AttachRingsToSettlement(
                        target,
                        rings,
                        candidate);

                    if (added > 0)
                    {
                        target.GeometrySourceOsmType = "relation";
                        target.GeometrySourceOsmId = candidate.RelationId;

                        onGeometryAttached?.Invoke();
                    }

                    Debug.WriteLine(
                        $"[SETTLEMENT RELATION] {candidate.Name}: polygons attached={added}");
                }
                catch (Exception ex) when (
     ex is TimeoutException ||
     ex is HttpRequestException ||
     ex is TaskCanceledException)
                {
                    delayAfterCurrentAttemptMs = GetRelationDelayAfterErrorMs(ex);

                    Debug.WriteLine(
                        $"[SETTLEMENT RELATION] {candidate.Name}: skipped: {ex.Message}");

                    Debug.WriteLine(
                        $"[SETTLEMENT RELATION] Delay after error: {delayAfterCurrentAttemptMs / 1000}s");
                }
                catch (Exception ex)
                {
                    delayAfterCurrentAttemptMs = RelationGeometryDelayAfterServerErrorMs;

                    Debug.WriteLine(
                        $"[SETTLEMENT RELATION] {candidate.Name}: error: {ex}");

                    Debug.WriteLine(
                        $"[SETTLEMENT RELATION] Delay after error: {delayAfterCurrentAttemptMs / 1000}s");
                }

                if (delayAfterCurrentAttemptMs > 0)
                {
                    await Task.Delay(delayAfterCurrentAttemptMs, cancellationToken);
                }
            }
        }

        private static bool HasUsablePolygons(SettlementGeometryItem? settlement)
        {
            return SettlementGeometryMerger.HasUsablePolygons(settlement);
        }

        private static int AttachRingsToSettlement(
            SettlementGeometryItem settlement,
            List<SettlementPolygonGeometry> rings,
            RelationGeometryCandidate relation)
        {
            settlement.Polygons ??= new List<string>();

            var existing = new HashSet<string>(
                settlement.Polygons,
                StringComparer.OrdinalIgnoreCase);

            var added = 0;
            var hasExplicitSettlementLink =
                IsExplicitRelationLink(settlement, relation);

            foreach (var ring in rings)
            {
                if (string.IsNullOrWhiteSpace(ring.OuterRing))
                {
                    continue;
                }

                var line = ring.OuterRing;

                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                SettlementGeometryMerger.AddPolygonCandidate(
                    settlement,
                    ring,
                    SettlementDataSources.OpenStreetMapOverpass,
                    relation.RelationId.ToString(CultureInfo.InvariantCulture),
                    SettlementDataSources.OpenStreetMapPriority,
                    settlement.Boundary,
                    settlement.AdminLevel,
                    hasExplicitSettlementLink);

                if (existing.Add(line))
                {
                    added++;
                }
            }

            return added;
        }

        private static void RestoreProviderPolygons(
            SettlementGeometryItem? snapshot,
            SettlementGeometryItem target,
            string provider)
        {
            if (snapshot == null || string.IsNullOrWhiteSpace(provider))
            {
                return;
            }

            foreach (var candidate in snapshot.PolygonCandidates ??
                     new List<SettlementPolygonCandidate>())
            {
                if (!string.Equals(
                        candidate.Provider,
                        provider,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                SettlementGeometryMerger.AddPolygonCandidate(
                    target,
                    candidate.Polygon,
                    candidate.Provider,
                    candidate.ExternalId,
                    candidate.Priority,
                    candidate.UtmZone,
                    candidate.UtmBand,
                    candidate.InteriorRings,
                    candidate.Boundary,
                    candidate.AdminLevel);
            }

            foreach (var reference in snapshot.ProviderReferences ??
                     new List<SettlementProviderReference>())
            {
                if (!string.Equals(
                        reference.Provider,
                        provider,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                SettlementGeometryMerger.AddProviderReference(
                    target,
                    reference.Provider,
                    reference.ExternalId,
                    reference.FeatureType);
            }
        }

        private static void MergeExistingGeometryForContourQueue(
            List<SettlementGeometryItem>? existingSettlements,
            List<SettlementGeometryItem> settlements)
        {
            if (existingSettlements == null || existingSettlements.Count == 0)
            {
                return;
            }

            foreach (var settlement in settlements)
            {
                CopyExistingGeometry(
                    FindExistingSettlement(existingSettlements, settlement),
                    settlement);
            }
        }

        private static SettlementGeometryItem? FindExistingSettlement(
            List<SettlementGeometryItem>? existingSettlements,
            SettlementGeometryItem settlement)
        {
            if (existingSettlements == null)
            {
                return null;
            }

            if (settlement.OsmId > 0)
            {
                var exact = existingSettlements.FirstOrDefault(x =>
                    x.OsmId == settlement.OsmId &&
                    string.Equals(
                        x.OsmType,
                        settlement.OsmType,
                        StringComparison.OrdinalIgnoreCase));

                if (exact != null)
                {
                    return exact;
                }
            }

            var names = GetNormalizedSettlementNames(settlement);

            if (names.Count == 0)
            {
                return null;
            }

            var candidates = existingSettlements
                .Where(x => GetNormalizedSettlementNames(x).Overlaps(names))
                .Where(x =>
                    string.IsNullOrWhiteSpace(settlement.Place) ||
                    string.IsNullOrWhiteSpace(x.Place) ||
                    string.Equals(
                        x.Place,
                        settlement.Place,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            if (settlement.FallbackPoint == null)
            {
                return null;
            }

            SettlementGeometryItem? best = null;
            var bestDistance = double.MaxValue;

            foreach (var candidate in candidates)
            {
                if (candidate.FallbackPoint == null)
                {
                    continue;
                }

                var distance = SettlementUtmProjection.GetDistanceMeters(
                    settlement.FallbackPoint,
                    candidate.FallbackPoint);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best != null &&
                   bestDistance <= GetMaximumRelationAttachDistanceMeters(
                       settlement.Place)
                ? best
                : null;
        }

        private static void CopyExistingGeometry(
            SettlementGeometryItem? existing,
            SettlementGeometryItem target,
            string? excludedProvider = null)
        {
            if (existing == null)
            {
                return;
            }

            foreach (var alias in existing.NameAliases ?? new List<string>())
            {
                if (!target.NameAliases.Contains(
                        alias,
                        StringComparer.OrdinalIgnoreCase))
                {
                    target.NameAliases.Add(alias);
                }
            }

            target.Boundary = string.IsNullOrWhiteSpace(target.Boundary)
                ? existing.Boundary
                : target.Boundary;
            target.AdminLevel = string.IsNullOrWhiteSpace(target.AdminLevel)
                ? existing.AdminLevel
                : target.AdminLevel;
            target.Region = string.IsNullOrWhiteSpace(target.Region)
                ? existing.Region
                : target.Region;
            target.District = string.IsNullOrWhiteSpace(target.District)
                ? existing.District
                : target.District;

            target.Polygons ??= new List<string>();

            var polygons = new HashSet<string>(
                target.Polygons,
                StringComparer.OrdinalIgnoreCase);

            foreach (var polygon in string.IsNullOrWhiteSpace(excludedProvider)
                         ? existing.Polygons ?? new List<string>()
                         : new List<string>())
            {
                if (!string.IsNullOrWhiteSpace(polygon) && polygons.Add(polygon))
                {
                    target.Polygons.Add(polygon);
                }
            }

            foreach (var reference in existing.ProviderReferences ??
                     new List<SettlementProviderReference>())
            {
                if (string.Equals(
                        reference.Provider,
                        excludedProvider,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                SettlementGeometryMerger.AddProviderReference(
                    target,
                    reference.Provider,
                    reference.ExternalId,
                    reference.FeatureType);
            }

            foreach (var center in existing.CenterCandidates ??
                     new List<SettlementCenterCandidate>())
            {
                if (string.Equals(
                        center.Provider,
                        excludedProvider,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                SettlementGeometryMerger.AddCenterCandidate(
                    target,
                    center.Point,
                    center.Provider,
                    center.ExternalId,
                    center.Priority);
            }

            foreach (var polygon in existing.PolygonCandidates ??
                     new List<SettlementPolygonCandidate>())
            {
                if (string.Equals(
                        polygon.Provider,
                        excludedProvider,
                        StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                SettlementGeometryMerger.AddPolygonCandidate(
                    target,
                    polygon.Polygon,
                    polygon.Provider,
                    polygon.ExternalId,
                    polygon.Priority,
                    polygon.UtmZone,
                    polygon.UtmBand,
                    polygon.InteriorRings,
                    polygon.Boundary,
                    polygon.AdminLevel);
            }

            if (target.FallbackPoint == null &&
                existing.FallbackPoint != null &&
                string.IsNullOrWhiteSpace(excludedProvider))
            {
                target.FallbackPoint = new SettlementPoint(
                    existing.FallbackPoint.X,
                    existing.FallbackPoint.Y,
                    existing.FallbackPoint.UtmZone,
                    existing.FallbackPoint.UtmBand,
                    existing.FallbackPoint.Latitude,
                    existing.FallbackPoint.Longitude);
            }

            if (target.GeometrySourceOsmId <= 0 &&
                existing.GeometrySourceOsmId > 0)
            {
                target.GeometrySourceOsmType =
                    existing.GeometrySourceOsmType;
                target.GeometrySourceOsmId =
                    existing.GeometrySourceOsmId;
            }
        }

        private static int GetRelationDelayAfterErrorMs(Exception ex)
        {
            var message = ex.Message ?? string.Empty;

            if (ex is HttpRequestException &&
                message.Contains("429", StringComparison.OrdinalIgnoreCase))
            {
                return RelationGeometryDelayAfter429Ms;
            }

            if (ex is HttpRequestException &&
                message.Contains("504", StringComparison.OrdinalIgnoreCase))
            {
                return RelationGeometryDelayAfterServerErrorMs;
            }

            if (ex is TimeoutException || ex is TaskCanceledException)
            {
                return RelationGeometryDelayAfterServerErrorMs;
            }

            return RelationGeometryDelayMs;
        }

        private static SettlementGeometryItem? FindSettlementTargetForRelation(
            List<SettlementGeometryItem> settlements,
            RelationGeometryCandidate relation)
        {
            var relationNameKey = NormalizeNameKey(relation.Name);

            if (string.IsNullOrWhiteSpace(relationNameKey))
            {
                return null;
            }

            var relationNames = new HashSet<string>(
                relation.NameAliases
                    .Append(relation.Name)
                    .Select(NormalizeNameKey)
                    .Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);

            var candidates = settlements
                .Where(s => GetNormalizedSettlementNames(s).Overlaps(relationNames))
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            var explicitlyLinked = candidates
                .Where(candidate => IsExplicitRelationLink(
                    candidate,
                    relation))
                .ToList();

            if (explicitlyLinked.Count == 1)
            {
                return explicitlyLinked[0];
            }

            if (explicitlyLinked.Count > 1)
            {
                candidates = explicitlyLinked;
            }

            var samePlaceCandidates = candidates
                .Where(s =>
                    !string.IsNullOrWhiteSpace(relation.Place) &&
                    string.Equals(
                        s.Place,
                        relation.Place,
                        StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (samePlaceCandidates.Count > 0)
            {
                candidates = samePlaceCandidates;
            }

            if (relation.Center == null)
            {
                // Краще створити окремий кандидат relation і не приклеїти
                // його навмання до однойменного населеного пункту.
                return null;
            }

            var administrativeMatches = candidates
                .Where(candidate => HasSameRelationAdministrativeContext(
                    candidate,
                    relation))
                .ToList();

            if (administrativeMatches.Count == 1)
            {
                candidates = administrativeMatches;
            }

            var ranked = candidates
                .Where(candidate => candidate.FallbackPoint != null)
                .Select(candidate => new
                {
                    Settlement = candidate,
                    Distance = SettlementUtmProjection.GetDistanceMeters(
                        relation.Center,
                        candidate.FallbackPoint!)
                })
                .OrderBy(x => x.Distance)
                .ToList();
            var best = ranked.FirstOrDefault();

            if (best != null &&
                best.Distance <= GetMaximumRelationAttachDistanceMeters(
                    relation.Place) &&
                IsNearestRelationMatchUnambiguous(
                    ranked.Select(x => x.Distance)))
            {
                return best.Settlement;
            }

            // Не приєднуємо relation до іншої однойменної НП, якщо її
            // центр занадто далеко. Це захищає, зокрема, Ковалівки.
            return null;
        }

        private static bool IsExplicitRelationLink(
            SettlementGeometryItem settlement,
            RelationGeometryCandidate relation)
        {
            if (settlement.OsmId > 0 &&
                string.Equals(
                    settlement.OsmType,
                    "relation",
                    StringComparison.OrdinalIgnoreCase) &&
                settlement.OsmId == relation.RelationId)
            {
                return true;
            }

            if (settlement.OsmId > 0 &&
                string.Equals(
                    settlement.OsmType,
                    "node",
                    StringComparison.OrdinalIgnoreCase) &&
                relation.LabelNodeIds.Contains(settlement.OsmId))
            {
                return true;
            }

            return (settlement.ProviderReferences ??
                    new List<SettlementProviderReference>())
                .Any(reference =>
                    string.Equals(
                        reference.Provider,
                        SettlementDataSources.OpenStreetMapOverpass,
                        StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(
                        reference.FeatureType,
                        "node",
                        StringComparison.OrdinalIgnoreCase) &&
                    long.TryParse(
                        reference.ExternalId,
                        NumberStyles.Integer,
                        CultureInfo.InvariantCulture,
                        out var nodeId) &&
                    relation.LabelNodeIds.Contains(nodeId));
        }

        private static SettlementGeometryItem CreateSettlementFromRelationCandidate(
            RelationGeometryCandidate candidate)
        {
            var item = new SettlementGeometryItem
            {
                Name = candidate.Name,
                NameAliases = candidate.NameAliases.ToList(),
                Place = candidate.Place,
                Boundary = candidate.Boundary,
                AdminLevel = candidate.AdminLevel,
                Region = candidate.Region,
                District = candidate.District,
                OsmType = "relation",
                OsmId = candidate.RelationId,
                GeometrySourceOsmType = "relation",
                GeometrySourceOsmId = candidate.RelationId
            };

            if (candidate.Center != null)
            {
                SettlementGeometryMerger.AddCenterCandidate(
                    item,
                    candidate.Center,
                    SettlementDataSources.OpenStreetMapOverpass,
                    candidate.RelationId.ToString(CultureInfo.InvariantCulture),
                    SettlementDataSources.OpenStreetMapPriority);
            }

            SettlementGeometryMerger.AddProviderReference(
                item,
                SettlementDataSources.OpenStreetMapOverpass,
                candidate.RelationId.ToString(CultureInfo.InvariantCulture),
                "relation");

            return item;
        }

        private async Task<List<SettlementPolygonGeometry>>
            LoadRelationOuterPolygonsByIdAsync(
            long relationId,
            CancellationToken cancellationToken)
        {
            var query =
     $"[out:json][timeout:{RelationGeometryRequestTimeoutSeconds}];" +
     $"relation({relationId});" +
     "out body;" +
     ">;" +
     "out body qt;";

            Exception? overpassError = null;

            try
            {
                var json = await ExecuteOverpassQueryAsync(
                    query,
                    TimeSpan.FromSeconds(RelationGeometryRequestTimeoutSeconds),
                    cancellationToken);
                var polygons = ParseRelationPolygons(json, relationId);

                if (polygons.Count > 0)
                {
                    return polygons;
                }

                Debug.WriteLine(
                    $"[SETTLEMENT RELATION] Relation {relationId}: " +
                    "Overpass returned no rings, trying OSM API");
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                overpassError = ex;
                Debug.WriteLine(
                    $"[SETTLEMENT RELATION] Relation {relationId}: " +
                    $"Overpass failed, trying OSM API: {ex.Message}");
            }

            try
            {
                return await LoadRelationFromOpenStreetMapApiAsync(
                    relationId,
                    cancellationToken);
            }
            catch (OperationCanceledException) when (
                cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception fallbackError)
            {
                throw new HttpRequestException(
                    $"Не вдалося отримати relation {relationId} ні з " +
                    "Overpass, ні з основного OSM API.",
                    overpassError == null
                        ? fallbackError
                        : new AggregateException(
                            overpassError,
                            fallbackError));
            }
        }

        private async Task<List<SettlementPolygonGeometry>>
            LoadRelationFromOpenStreetMapApiAsync(
            long relationId,
            CancellationToken cancellationToken)
        {
            using var timeoutCts =
                CancellationTokenSource.CreateLinkedTokenSource(
                    cancellationToken);
            timeoutCts.CancelAfter(
                TimeSpan.FromSeconds(OpenStreetMapRelationTimeoutSeconds));

            var url = $"{OpenStreetMapApiBaseUrl}/relation/" +
                      $"{relationId}/full.json";
            using var response = await _httpClient.GetAsync(
                url,
                HttpCompletionOption.ResponseHeadersRead,
                timeoutCts.Token);

            if (!response.IsSuccessStatusCode)
            {
                throw new HttpRequestException(
                    $"OSM API повернув HTTP {(int)response.StatusCode} " +
                    $"для relation {relationId}.",
                    null,
                    response.StatusCode);
            }

            var json = await response.Content.ReadAsStringAsync(
                timeoutCts.Token);
            var polygons = ParseRelationPolygons(json, relationId);

            Debug.WriteLine(
                $"[SETTLEMENT RELATION] Relation {relationId}: " +
                $"OSM API fallback rings={polygons.Count}");

            return polygons;
        }

        internal static List<SettlementPolygonGeometry> ParseRelationPolygons(
            string json,
            long relationId)
        {
            var root = JObject.Parse(json);
            var elements = root["elements"] as JArray;

            if (elements == null)
            {
                return new List<SettlementPolygonGeometry>();
            }

            JObject? relation = null;
            var ways = new Dictionary<long, JObject>();
            var nodes = new Dictionary<long, JObject>();

            foreach (var token in elements)
            {
                if (token is not JObject element)
                {
                    continue;
                }

                var type = GetString(element["type"]);
                var id = GetLong(element["id"]);

                if (string.Equals(type, "relation", StringComparison.OrdinalIgnoreCase) &&
                    id == relationId)
                {
                    relation = element;
                    continue;
                }

                if (string.Equals(type, "way", StringComparison.OrdinalIgnoreCase))
                {
                    ways[id] = element;
                    continue;
                }

                if (string.Equals(type, "node", StringComparison.OrdinalIgnoreCase))
                {
                    nodes[id] = element;
                }
            }

            if (relation == null)
            {
                Debug.WriteLine($"[SETTLEMENT RELATION TEST] Relation {relationId} not found");
                return new List<SettlementPolygonGeometry>();
            }

            var members = relation["members"] as JArray;

            if (members == null)
            {
                Debug.WriteLine($"[SETTLEMENT RELATION TEST] Relation {relationId} has no members");
                return new List<SettlementPolygonGeometry>();
            }

            var outerSegments = new List<List<PointF>>();
            var innerSegments = new List<List<PointF>>();

            foreach (var token in members)
            {
                if (token is not JObject member)
                {
                    continue;
                }

                var memberType = GetString(member["type"]);

                if (!string.Equals(memberType, "way", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var role = GetString(member["role"]);

                if (!string.IsNullOrWhiteSpace(role) &&
                    !string.Equals(role, "outer", StringComparison.OrdinalIgnoreCase) &&
                    !string.Equals(role, "inner", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var wayRef = GetLong(member["ref"]);

                if (!ways.TryGetValue(wayRef, out var way))
                {
                    continue;
                }

                var segment = ReadWayNodesAsGeoPoints(
                    way,
                    nodes);

                if (segment.Count >= 2)
                {
                    if (string.Equals(
                            role,
                            "inner",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        innerSegments.Add(segment);
                    }
                    else
                    {
                        outerSegments.Add(segment);
                    }
                }
            }

            var outerGeoRings = BuildGeoRingsFromSegments(outerSegments);
            var innerGeoRings = BuildGeoRingsFromSegments(innerSegments);

            Debug.WriteLine(
                $"[SETTLEMENT RELATION] Relation {relationId}: " +
                $"ways={ways.Count}, nodes={nodes.Count}, " +
                $"outerSegments={outerSegments.Count}, " +
                $"innerSegments={innerSegments.Count}");

            return CreateProjectedRelationGeometries(
                outerGeoRings,
                innerGeoRings);
        }

        private static List<PointF> ReadWayNodesAsGeoPoints(
            JObject way,
            Dictionary<long, JObject> nodes)
        {
            var result = new List<PointF>();

            if (way["nodes"] is not JArray nodeRefs)
            {
                return result;
            }

            foreach (var token in nodeRefs)
            {
                var nodeRef = GetLong(token);

                if (!nodes.TryGetValue(nodeRef, out var node))
                {
                    continue;
                }

                if (!TryReadLatLng(node, out var lat, out var lng))
                {
                    continue;
                }

                // Для складання relation тимчасово використовуємо X=lon,
                // Y=lat. Після замикання кілець усі точки проєктуються в одну
                // UTM-зону relation, включно з контурами на межі зон.
                result.Add(new PointF((float)lng, (float)lat));
            }

            // Тут не прибираємо повтор першої точки в кінці: він доводить,
            // що окремий OSM way уже є замкненим outer/inner кільцем.
            return RemoveDuplicateConsecutiveGeoPoints(
                result,
                removeClosingPoint: false);
        }

        private static List<SettlementPolygonGeometry>
            CreateProjectedRelationGeometries(
                List<List<PointF>> outerGeoRings,
                List<List<PointF>> innerGeoRings)
        {
            var result = new List<SettlementPolygonGeometry>();
            var first = outerGeoRings
                .SelectMany(x => x)
                .FirstOrDefault();

            if (first == PointF.Empty ||
                !SettlementUtmProjection.TryProject(
                    first.Y,
                    first.X,
                    null,
                    out var anchor))
            {
                return result;
            }

            foreach (var ring in outerGeoRings)
            {
                var projected = ProjectGeoRing(ring, anchor.UtmZone);

                if (projected.Count < 3)
                {
                    continue;
                }

                result.Add(new SettlementPolygonGeometry
                {
                    OuterRing = SettlementGeometryService.ToPolygonLine(projected),
                    UtmZone = anchor.UtmZone,
                    UtmBand = anchor.UtmBand
                });
            }

            foreach (var innerGeoRing in innerGeoRings)
            {
                var projected = ProjectGeoRing(
                    innerGeoRing,
                    anchor.UtmZone);

                if (projected.Count < 3)
                {
                    continue;
                }

                var owner = result.FirstOrDefault(x =>
                    IsPointInsidePolygon(
                        projected[0],
                        SettlementGeometryService.ParsePolygonLine(
                            x.OuterRing)));

                if (owner == null)
                {
                    continue;
                }

                var line = SettlementGeometryService.ToPolygonLine(projected);

                if (!string.IsNullOrWhiteSpace(line) &&
                    !owner.InteriorRings.Contains(
                        line,
                        StringComparer.OrdinalIgnoreCase))
                {
                    owner.InteriorRings.Add(line);
                }
            }

            return result;
        }

        private static List<PointF> ProjectGeoRing(
            IEnumerable<PointF> geoRing,
            int zone)
        {
            var result = new List<PointF>();

            foreach (var geoPoint in geoRing)
            {
                if (!SettlementUtmProjection.TryProject(
                        geoPoint.Y,
                        geoPoint.X,
                        zone,
                        out var projected))
                {
                    continue;
                }

                var point = new PointF(projected.X, projected.Y);

                if (result.Count == 0 ||
                    !AreSameUtmPoint(result[^1], point))
                {
                    result.Add(point);
                }
            }

            if (result.Count > 3 && AreSameUtmPoint(result[0], result[^1]))
            {
                result.RemoveAt(result.Count - 1);
            }

            return result;
        }

        private static List<List<PointF>> BuildGeoRingsFromSegments(
            List<List<PointF>> segments)
        {
            var result = new List<List<PointF>>();
            var remaining = segments
                .Where(x => x.Count >= 2)
                .Select(x => new List<PointF>(x))
                .ToList();

            while (remaining.Count > 0)
            {
                var ring = remaining[0];
                remaining.RemoveAt(0);
                var changed = true;

                while (changed)
                {
                    changed = false;

                    for (var index = remaining.Count - 1; index >= 0; index--)
                    {
                        if (!TryAttachGeoSegment(ring, remaining[index]))
                        {
                            continue;
                        }

                        remaining.RemoveAt(index);
                        changed = true;
                    }
                }

                var isClosed = ring.Count >= 4 &&
                               AreSameGeoPoint(ring[0], ring[^1]);

                if (!isClosed)
                {
                    Debug.WriteLine(
                        "[SETTLEMENT RELATION] Незамкнений набір outer/inner " +
                        "ways пропущено, щоб не створити хибний полігон.");
                    continue;
                }

                ring = RemoveDuplicateConsecutiveGeoPoints(ring);

                if (ring.Count >= 3)
                {
                    result.Add(ring);
                }
            }

            return result;
        }

        private static bool TryAttachGeoSegment(
            List<PointF> ring,
            List<PointF> segment)
        {
            if (ring.Count == 0 || segment.Count == 0)
            {
                return false;
            }

            var ringFirst = ring[0];
            var ringLast = ring[^1];
            var segmentFirst = segment[0];
            var segmentLast = segment[^1];

            if (AreSameGeoPoint(ringLast, segmentFirst))
            {
                ring.AddRange(segment.Skip(1));
                return true;
            }

            if (AreSameGeoPoint(ringLast, segmentLast))
            {
                ring.AddRange(segment.AsEnumerable().Reverse().Skip(1));
                return true;
            }

            if (AreSameGeoPoint(ringFirst, segmentLast))
            {
                ring.InsertRange(0, segment.Take(segment.Count - 1));
                return true;
            }

            if (AreSameGeoPoint(ringFirst, segmentFirst))
            {
                ring.InsertRange(
                    0,
                    segment.AsEnumerable().Reverse().Take(segment.Count - 1));
                return true;
            }

            return false;
        }

        private static List<PointF> RemoveDuplicateConsecutiveGeoPoints(
            IEnumerable<PointF> points,
            bool removeClosingPoint = true)
        {
            var result = new List<PointF>();

            foreach (var point in points)
            {
                if (result.Count == 0 ||
                    !AreSameGeoPoint(result[^1], point))
                {
                    result.Add(point);
                }
            }

            if (removeClosingPoint &&
                result.Count > 1 &&
                AreSameGeoPoint(result[0], result[^1]))
            {
                result.RemoveAt(result.Count - 1);
            }

            return result;
        }

        private static bool AreSameGeoPoint(PointF first, PointF second)
        {
            return Math.Abs(first.X - second.X) <= 0.000001 &&
                   Math.Abs(first.Y - second.Y) <= 0.000001;
        }

        private static bool IsPointInsidePolygon(
            PointF point,
            IReadOnlyList<PointF> polygon)
        {
            var inside = false;

            for (int index = 0, previousIndex = polygon.Count - 1;
                 index < polygon.Count;
                 previousIndex = index++)
            {
                var current = polygon[index];
                var previous = polygon[previousIndex];
                var intersects =
                    (current.Y > point.Y) != (previous.Y > point.Y) &&
                    point.X <
                    (previous.X - current.X) *
                    (point.Y - current.Y) /
                    ((previous.Y - current.Y) + 0.000001f) +
                    current.X;

                if (intersects)
                {
                    inside = !inside;
                }
            }

            return inside;
        }

        private SettlementGeometryItem? ParseElement(
            JObject element,
            TryConvertToUtm tryConvert)
        {
            var osmType = GetString(element["type"]);
            var osmId = GetLong(element["id"]);

            var tags = element["tags"] as JObject;

            var place = GetTag(tags, "place");
            var boundary = GetTag(tags, "boundary");
            var adminLevel = GetTag(tags, "admin_level");

            var isAllowedPlace =
                AllowedPlaceTypes.Contains(place);

            var isAllowedAdministrativeBoundary =
                string.Equals(boundary, "administrative", StringComparison.OrdinalIgnoreCase) &&
                AllowedBoundaryAdminLevels.Contains(adminLevel);

            if (!isAllowedPlace && !isAllowedAdministrativeBoundary)
            {
                return null;
            }

            var name = GetBestName(tags);

            if (!IsUsableSettlementName(name))
            {
                return null;
            }

            if (string.IsNullOrWhiteSpace(place))
            {
                place = string.IsNullOrWhiteSpace(adminLevel)
                    ? "boundary"
                    : $"boundary:{adminLevel}";
            }

            var item = new SettlementGeometryItem
            {
                Name = name,
                NameAliases = GetNameAliases(tags),
                Place = place,
                Boundary = boundary,
                AdminLevel = adminLevel,
                CountryCode = GetTag(tags, "addr:country"),
                Region = GetFirstNonEmptyTag(
                    tags,
                    "addr:region",
                    "is_in:state",
                    "is_in:region"),
                District = GetFirstNonEmptyTag(
                    tags,
                    "addr:district",
                    "is_in:district"),
                OsmType = osmType,
                OsmId = osmId
            };

            if (string.Equals(osmType, "node", StringComparison.OrdinalIgnoreCase))
            {
                if (TryReadLatLng(element, out var lat, out var lng) &&
                    TryCreateSettlementPoint(lat, lng, out var point))
                {
                    item.FallbackPoint = point;
                }
            }
            else if (string.Equals(osmType, "way", StringComparison.OrdinalIgnoreCase))
            {
                var polygon = ReadGeometryAsUtmPoints(element["geometry"], tryConvert);

                if (polygon.Count >= 3)
                {
                    item.Polygons.Add(SettlementGeometryService.ToPolygonLine(polygon));
                }

                if (item.Polygons.Count == 0 &&
                    TryReadLatLng(element["center"], out var lat, out var lng) &&
                    TryCreateSettlementPoint(lat, lng, out var point))
                {
                    item.FallbackPoint = point;
                }
            }
            else if (string.Equals(osmType, "relation", StringComparison.OrdinalIgnoreCase))
            {
                var directGeometry = ReadGeometryAsUtmPoints(element["geometry"], tryConvert);

                if (directGeometry.Count >= 3)
                {
                    item.Polygons.Add(SettlementGeometryService.ToPolygonLine(directGeometry));
                }

                var segments = ReadRelationOuterSegments(element["members"], tryConvert);
                var rings = BuildRingsFromSegments(segments);

                foreach (var ring in rings)
                {
                    if (ring.Count >= 3)
                    {
                        item.Polygons.Add(SettlementGeometryService.ToPolygonLine(ring));
                    }
                }

                if (item.Polygons.Count == 0 &&
                    TryReadLatLng(element["center"], out var lat, out var lng) &&
                    TryCreateSettlementPoint(lat, lng, out var point))
                {
                    item.FallbackPoint = point;
                }
            }

            if (item.Polygons.Count == 0 && item.FallbackPoint == null)
            {
                return null;
            }

            StampOpenStreetMapMetadata(item);

            return item;
        }

        private static void StampOpenStreetMapMetadata(
            SettlementGeometryItem item)
        {
            var externalId = item.OsmId.ToString(CultureInfo.InvariantCulture);

            SettlementGeometryMerger.AddProviderReference(
                item,
                SettlementDataSources.OpenStreetMapOverpass,
                externalId,
                item.OsmType);

            if (item.FallbackPoint != null)
            {
                SettlementGeometryMerger.AddCenterCandidate(
                    item,
                    item.FallbackPoint,
                    SettlementDataSources.OpenStreetMapOverpass,
                    externalId,
                    SettlementDataSources.OpenStreetMapPriority);
            }

            var polygonExternalId = item.GeometrySourceOsmId > 0
                ? item.GeometrySourceOsmId.ToString(CultureInfo.InvariantCulture)
                : externalId;

            foreach (var polygon in item.Polygons)
            {
                SettlementGeometryMerger.AddPolygonCandidate(
                    item,
                    polygon,
                    SettlementDataSources.OpenStreetMapOverpass,
                    polygonExternalId,
                    SettlementDataSources.OpenStreetMapPriority);
            }
        }

        private static ResidentialGeometryItem? ParseResidentialGeometry(
            JObject element,
            TryConvertToUtm tryConvert)
        {
            var osmType = GetString(element["type"]);

            if (string.Equals(osmType, "node", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            var tags = element["tags"] as JObject;

            var item = new ResidentialGeometryItem
            {
                Name = GetBestName(tags),
                OsmType = osmType,
                OsmId = GetLong(element["id"])
            };

            var directGeometry = ReadGeometryAsUtmPoints(element["geometry"], tryConvert);

            if (directGeometry.Count >= 3)
            {
                item.Polygons.Add(directGeometry);
            }

            var segments = ReadRelationOuterSegments(element["members"], tryConvert);
            var rings = BuildRingsFromSegments(segments);

            foreach (var ring in rings)
            {
                if (ring.Count >= 3)
                {
                    item.Polygons.Add(ring);
                }
            }

            if (item.Polygons.Count == 0)
            {
                return null;
            }

            if (TryReadLatLng(element["center"], out var lat, out var lng) &&
                tryConvert(lat, lng, out var centerUtm))
            {
                item.Center = centerUtm;
            }
            else
            {
                item.Center = GetAveragePoint(item.Polygons);
            }

            return item;
        }

        private static void AttachResidentialPolygonsToSettlements(
            List<SettlementGeometryItem> settlements,
            List<ResidentialGeometryItem> residentialAreas)
        {
            if (settlements.Count == 0 || residentialAreas.Count == 0)
            {
                return;
            }

            foreach (var residentialArea in residentialAreas)
            {
                if (residentialArea.Polygons.Count == 0)
                {
                    continue;
                }

                var target = FindNamedSettlementTarget(
                    settlements,
                    residentialArea);

                target ??= FindNearestSettlementTarget(
                    settlements,
                    residentialArea.Center,
                    MaxResidentialAttachDistanceMeters);

                if (target == null)
                {
                    continue;
                }

                var existing = new HashSet<string>(
                    target.Polygons ?? new List<string>(),
                    StringComparer.OrdinalIgnoreCase);

                target.Polygons ??= new List<string>();

                foreach (var polygon in residentialArea.Polygons)
                {
                    if (polygon.Count < 3)
                    {
                        continue;
                    }

                    var line = SettlementGeometryService.ToPolygonLine(polygon);

                    if (string.IsNullOrWhiteSpace(line))
                    {
                        continue;
                    }

                    if (!existing.Add(line))
                    {
                        continue;
                    }

                    target.Polygons.Add(line);
                }
            }
        }

        private static SettlementGeometryItem? FindNamedSettlementTarget(
            List<SettlementGeometryItem> settlements,
            ResidentialGeometryItem residentialArea)
        {
            if (string.IsNullOrWhiteSpace(residentialArea.Name))
            {
                return null;
            }

            var residentialNameKey = NormalizeNameKey(residentialArea.Name);

            if (string.IsNullOrWhiteSpace(residentialNameKey))
            {
                return null;
            }

            var candidates = settlements
                .Where(s => NormalizeNameKey(s.Name) == residentialNameKey)
                .ToList();

            if (candidates.Count == 0)
            {
                return null;
            }

            SettlementGeometryItem? best = null;
            double bestDistance = double.MaxValue;

            foreach (var candidate in candidates)
            {
                if (!TryGetFallbackPoint(candidate, out var anchor))
                {
                    best ??= candidate;
                    continue;
                }

                var distance = GetDistance(residentialArea.Center, anchor);

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = candidate;
                }
            }

            return best;
        }

        private static SettlementGeometryItem? FindNearestSettlementTarget(
            List<SettlementGeometryItem> settlements,
            PointF residentialCenter,
            double maxDistanceMeters)
        {
            SettlementGeometryItem? best = null;
            double bestDistance = double.MaxValue;

            foreach (var settlement in settlements)
            {
                if (!TryGetFallbackPoint(settlement, out var anchor))
                {
                    continue;
                }

                var distance = GetDistance(residentialCenter, anchor);

                if (distance > maxDistanceMeters)
                {
                    continue;
                }

                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = settlement;
                }
            }

            return best;
        }

        private static bool TryGetFallbackPoint(
            SettlementGeometryItem settlement,
            out PointF point)
        {
            point = PointF.Empty;

            if (settlement.FallbackPoint == null)
            {
                return false;
            }

            point = new PointF(
                settlement.FallbackPoint.X,
                settlement.FallbackPoint.Y);

            return true;
        }

        private static bool IsResidentialElement(JObject element)
        {
            var tags = element["tags"] as JObject;

            return string.Equals(
                GetTag(tags, "landuse"),
                "residential",
                StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildSettlementListQuery(SettlementCacheBounds bounds)
        {
            var south = Math.Min(bounds.Bottom, bounds.Top);
            var north = Math.Max(bounds.Bottom, bounds.Top);
            var west = Math.Min(bounds.Left, bounds.Right);
            var east = Math.Max(bounds.Left, bounds.Right);

            return
                $"[out:json][timeout:{SettlementTileTimeoutSeconds}];" +
                "(" +
                $"node[\"place\"~\"^(city|town|village|hamlet|isolated_dwelling)$\"]({ToInvariant(south)},{ToInvariant(west)},{ToInvariant(north)},{ToInvariant(east)});" +
                ");" +
                "out body qt;";
        }

        private static string BuildResidentialAreaQuery(SettlementCacheBounds bounds)
        {
            var south = Math.Min(bounds.Bottom, bounds.Top);
            var north = Math.Max(bounds.Bottom, bounds.Top);
            var west = Math.Min(bounds.Left, bounds.Right);
            var east = Math.Max(bounds.Left, bounds.Right);

            return
                "[out:json][timeout:25];" +
                "(" +
                $"way[\"landuse\"=\"residential\"]({ToInvariant(south)},{ToInvariant(west)},{ToInvariant(north)},{ToInvariant(east)});" +
                $"relation[\"landuse\"=\"residential\"]({ToInvariant(south)},{ToInvariant(west)},{ToInvariant(north)},{ToInvariant(east)});" +
                ");" +
                "out body geom center;";
        }

        private async Task<string> ExecuteOverpassQueryAsync(
    string query,
    TimeSpan timeout,
    CancellationToken cancellationToken)
        {
            Exception? lastException = null;

            for (var attempt = 0; attempt < OverpassUrls.Length; attempt++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var endpointIndex =
                    (_preferredOverpassEndpointIndex + attempt) %
                    OverpassUrls.Length;

                var endpoint = OverpassUrls[endpointIndex];

                using var timeoutCts =
                    CancellationTokenSource.CreateLinkedTokenSource(
                        cancellationToken);

                timeoutCts.CancelAfter(timeout);

                try
                {
                    using var content = new FormUrlEncodedContent(
                        new[]
                        {
                            new KeyValuePair<string, string>("data", query)
                        });

                    using var response = await _httpClient.PostAsync(
                        endpoint,
                        content,
                        timeoutCts.Token);

                    if (!response.IsSuccessStatusCode)
                    {
                        var statusCode = (int)response.StatusCode;
                        var error = new HttpRequestException(
                            $"Overpass {endpoint} повернув HTTP {statusCode}",
                            null,
                            response.StatusCode);

                        if (statusCode != 429 && statusCode < 500)
                        {
                            throw error;
                        }

                        lastException = error;
                        continue;
                    }

                    var result = await response.Content.ReadAsStringAsync(
                        timeoutCts.Token);

                    _preferredOverpassEndpointIndex = endpointIndex;

                    return result;
                }
                catch (OperationCanceledException) when (
                    !cancellationToken.IsCancellationRequested)
                {
                    lastException = new TimeoutException(
                        $"Overpass {endpoint} не відповів вчасно.");
                }
                catch (HttpRequestException ex)
                {
                    if (ex.StatusCode.HasValue &&
                        (int)ex.StatusCode.Value < 500 &&
                        (int)ex.StatusCode.Value != 429)
                    {
                        throw;
                    }

                    lastException = ex;
                }
            }

            throw lastException ??
                  new HttpRequestException("Жоден Overpass endpoint не відповів.");
        }

        private List<SettlementGeometryItem> ParseSettlements(
            string json,
            TryConvertToUtm tryConvert)
        {
            var result = new List<SettlementGeometryItem>();

            var root = JObject.Parse(json);
            var elements = root["elements"] as JArray;

            if (elements == null)
            {
                return result;
            }

            foreach (var token in elements)
            {
                if (token is not JObject element)
                {
                    continue;
                }

                var settlement = ParseElement(element, tryConvert);

                if (settlement == null)
                {
                    continue;
                }

                result.Add(settlement);
            }

            return result;
        }

        private async Task<List<SettlementGeometryItem>> LoadSettlementsByTilesAsync(
            SettlementCacheBounds bounds,
            TryConvertToUtm tryConvert,
            CancellationToken cancellationToken,
            Action<List<SettlementGeometryItem>>? onProgress = null)
        {
            var result = new List<SettlementGeometryItem>();
            var tiles = CreateSettlementTiles(bounds).ToList();
            var failedTiles = new List<SettlementCacheBounds>();

            Debug.WriteLine($"[SETTLEMENT LOAD] Settlement tiles: {tiles.Count}");

            async Task<bool> TryLoadTileAsync(
                SettlementCacheBounds tile,
                string attemptLabel)
            {
                try
                {
                    Debug.WriteLine(
                        $"[SETTLEMENT LOAD] Settlement tile {attemptLabel}: " +
                        $"Top={tile.Top}, Bottom={tile.Bottom}, " +
                        $"Left={tile.Left}, Right={tile.Right}");

                    var query = BuildSettlementListQuery(tile);
                    var json = await ExecuteOverpassQueryAsync(
                        query,
                        TimeSpan.FromSeconds(SettlementTileTimeoutSeconds),
                        cancellationToken);
                    var items = ParseSettlements(json, tryConvert);

                    if (items.Count > 0)
                    {
                        result.AddRange(items);
                        result = Deduplicate(result);
                        onProgress?.Invoke(
                            new List<SettlementGeometryItem>(result));
                    }

                    Debug.WriteLine(
                        $"[SETTLEMENT LOAD] Settlement tile {attemptLabel}: " +
                        $"items={items.Count}, total={result.Count}");
                    return true;
                }
                catch (OperationCanceledException) when (
                    cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex) when (
                    ex is TimeoutException ||
                    ex is HttpRequestException ||
                    ex is TaskCanceledException)
                {
                    Debug.WriteLine(
                        $"[SETTLEMENT LOAD] Settlement tile {attemptLabel} " +
                        $"temporarily failed: {ex.Message}");
                    return false;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine(
                        $"[SETTLEMENT LOAD] Settlement tile {attemptLabel} " +
                        $"error: {ex}");
                    return false;
                }
            }

            for (var i = 0; i < tiles.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var tile = tiles[i];

                if (!await TryLoadTileAsync(
                        tile,
                        $"{i + 1}/{tiles.Count}"))
                {
                    failedTiles.Add(tile);
                }

                if (SettlementTileDelayMs > 0)
                {
                    await Task.Delay(SettlementTileDelayMs, cancellationToken);
                }
            }

            // Друге коло тільки для плиток, які справді впали. Успішна
            // порожня відповідь означає, що в плитці немає НП, її не ганяємо
            // повторно без потреби.
            for (var retryIndex = 0;
                 retryIndex < failedTiles.Count;
                 retryIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                await TryLoadTileAsync(
                    failedTiles[retryIndex],
                    $"retry {retryIndex + 1}/{failedTiles.Count}");

                if (SettlementTileDelayMs > 0 &&
                    retryIndex < failedTiles.Count - 1)
                {
                    await Task.Delay(SettlementTileDelayMs, cancellationToken);
                }
            }

            return result;
        }

        private async Task<List<ResidentialGeometryItem>> LoadResidentialAreasByTilesAsync(
            SettlementCacheBounds bounds,
            TryConvertToUtm tryConvert,
            CancellationToken cancellationToken)
        {
            var result = new List<ResidentialGeometryItem>();

            var tiles = CreateResidentialTiles(bounds).ToList();

            Debug.WriteLine($"[SETTLEMENT LOAD] Residential tiles: {tiles.Count}");

            for (var i = 0; i < tiles.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var tile = tiles[i];

                try
                {
                    Debug.WriteLine(
                        $"[SETTLEMENT LOAD] Residential tile {i + 1}/{tiles.Count}: " +
                        $"Top={tile.Top}, Bottom={tile.Bottom}, Left={tile.Left}, Right={tile.Right}");

                    var query = BuildResidentialAreaQuery(tile);

                    var json = await ExecuteOverpassQueryAsync(
                        query,
                        TimeSpan.FromSeconds(25),
                        cancellationToken);

                    var tileAreas = ParseResidentialAreas(
                        json,
                        tryConvert);

                    result.AddRange(tileAreas);

                    Debug.WriteLine(
                        $"[SETTLEMENT LOAD] Residential tile {i + 1}/{tiles.Count}: " +
                        $"areas={tileAreas.Count}, total={result.Count}");
                }
                catch (Exception ex) when (
                    ex is TimeoutException ||
                    ex is HttpRequestException ||
                    ex is TaskCanceledException)
                {
                    Debug.WriteLine(
                        $"[SETTLEMENT LOAD] Residential tile {i + 1}/{tiles.Count} skipped: {ex.Message}");
                }

                if (ResidentialTileDelayMs > 0)
                {
                    await Task.Delay(ResidentialTileDelayMs, cancellationToken);
                }
            }

            return result;
        }

        private static List<ResidentialGeometryItem> ParseResidentialAreas(
            string json,
            TryConvertToUtm tryConvert)
        {
            var result = new List<ResidentialGeometryItem>();

            var root = JObject.Parse(json);
            var elements = root["elements"] as JArray;

            if (elements == null)
            {
                return result;
            }

            foreach (var token in elements)
            {
                if (token is not JObject element)
                {
                    continue;
                }

                if (!IsResidentialElement(element))
                {
                    continue;
                }

                var area = ParseResidentialGeometry(element, tryConvert);

                if (area == null)
                {
                    continue;
                }

                result.Add(area);
            }

            return DeduplicateResidentialAreas(result);
        }

        private static IEnumerable<SettlementCacheBounds> CreateSettlementTiles(
    SettlementCacheBounds bounds)
        {
            var south = Math.Min(bounds.Bottom, bounds.Top);
            var north = Math.Max(bounds.Bottom, bounds.Top);
            var west = Math.Min(bounds.Left, bounds.Right);
            var east = Math.Max(bounds.Left, bounds.Right);

            for (var tileSouth = south; tileSouth < north; tileSouth += SettlementTileSizeDegrees)
            {
                var tileNorth = Math.Min(
                    tileSouth + SettlementTileSizeDegrees,
                    north);

                for (var tileWest = west; tileWest < east; tileWest += SettlementTileSizeDegrees)
                {
                    var tileEast = Math.Min(
                        tileWest + SettlementTileSizeDegrees,
                        east);

                    yield return new SettlementCacheBounds
                    {
                        Top = tileNorth,
                        Bottom = tileSouth,
                        Left = tileWest,
                        Right = tileEast,
                        PaddingKm = bounds.PaddingKm
                    };
                }
            }
        }

        private static IEnumerable<SettlementCacheBounds> CreateResidentialTiles(
            SettlementCacheBounds bounds)
        {
            var south = Math.Min(bounds.Bottom, bounds.Top);
            var north = Math.Max(bounds.Bottom, bounds.Top);
            var west = Math.Min(bounds.Left, bounds.Right);
            var east = Math.Max(bounds.Left, bounds.Right);

            for (var tileSouth = south; tileSouth < north; tileSouth += ResidentialTileSizeDegrees)
            {
                var tileNorth = Math.Min(
                    tileSouth + ResidentialTileSizeDegrees,
                    north);

                for (var tileWest = west; tileWest < east; tileWest += ResidentialTileSizeDegrees)
                {
                    var tileEast = Math.Min(
                        tileWest + ResidentialTileSizeDegrees,
                        east);

                    yield return new SettlementCacheBounds
                    {
                        Top = tileNorth,
                        Bottom = tileSouth,
                        Left = tileWest,
                        Right = tileEast,
                        PaddingKm = bounds.PaddingKm
                    };
                }
            }
        }

        private static List<ResidentialGeometryItem> DeduplicateResidentialAreas(
            List<ResidentialGeometryItem> areas)
        {
            var result = new List<ResidentialGeometryItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var area in areas)
            {
                var key = area.OsmId > 0
                    ? $"{area.OsmType}:{area.OsmId}"
                    : $"{area.Name}:{area.Center.X:0}:{area.Center.Y:0}";

                if (!seen.Add(key))
                {
                    continue;
                }

                result.Add(area);
            }

            return result;
        }

        private static List<PointF> ReadGeometryAsUtmPoints(
            JToken? geometryToken,
            TryConvertToUtm tryConvert)
        {
            var result = new List<PointF>();

            if (geometryToken is not JArray geometry)
            {
                return result;
            }

            foreach (var token in geometry)
            {
                if (token is not JObject pointObject)
                {
                    continue;
                }

                if (!TryReadLatLng(pointObject, out var lat, out var lng))
                {
                    continue;
                }

                if (!tryConvert(lat, lng, out var utm))
                {
                    continue;
                }

                result.Add(utm);
            }

            return RemoveDuplicateConsecutivePoints(result);
        }

        private static List<List<PointF>> ReadRelationOuterSegments(
            JToken? membersToken,
            TryConvertToUtm tryConvert)
        {
            var result = new List<List<PointF>>();

            if (membersToken is not JArray members)
            {
                return result;
            }

            foreach (var token in members)
            {
                if (token is not JObject member)
                {
                    continue;
                }

                var role = GetString(member["role"]);

                if (!string.IsNullOrWhiteSpace(role) &&
                    !string.Equals(role, "outer", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var segment = ReadGeometryAsUtmPoints(member["geometry"], tryConvert);

                if (segment.Count >= 2)
                {
                    result.Add(segment);
                }
            }

            return result;
        }

        private static List<List<PointF>> BuildRingsFromSegments(
            List<List<PointF>> segments)
        {
            var result = new List<List<PointF>>();

            var remaining = segments
                .Where(s => s != null && s.Count >= 2)
                .Select(s => new List<PointF>(s))
                .ToList();

            while (remaining.Count > 0)
            {
                var ring = remaining[0];
                remaining.RemoveAt(0);

                var changed = true;

                while (changed)
                {
                    changed = false;

                    for (var i = remaining.Count - 1; i >= 0; i--)
                    {
                        var segment = remaining[i];

                        if (TryAttachSegment(ring, segment))
                        {
                            remaining.RemoveAt(i);
                            changed = true;
                        }
                    }
                }

                ring = RemoveDuplicateConsecutivePoints(ring);

                if (ring.Count >= 3)
                {
                    result.Add(ring);
                }
            }

            return result;
        }

        private static bool TryAttachSegment(
            List<PointF> ring,
            List<PointF> segment)
        {
            if (ring.Count == 0 || segment.Count == 0)
            {
                return false;
            }

            var ringFirst = ring[0];
            var ringLast = ring[ring.Count - 1];

            var segmentFirst = segment[0];
            var segmentLast = segment[segment.Count - 1];

            if (AreSameUtmPoint(ringLast, segmentFirst))
            {
                ring.AddRange(segment.Skip(1));
                return true;
            }

            if (AreSameUtmPoint(ringLast, segmentLast))
            {
                var reversed = segment.AsEnumerable().Reverse().Skip(1);
                ring.AddRange(reversed);
                return true;
            }

            if (AreSameUtmPoint(ringFirst, segmentLast))
            {
                ring.InsertRange(0, segment.Take(segment.Count - 1));
                return true;
            }

            if (AreSameUtmPoint(ringFirst, segmentFirst))
            {
                var reversed = segment.AsEnumerable().Reverse().Take(segment.Count - 1);
                ring.InsertRange(0, reversed);
                return true;
            }

            return false;
        }

        private static List<PointF> RemoveDuplicateConsecutivePoints(
            List<PointF> points)
        {
            var result = new List<PointF>();

            foreach (var point in points)
            {
                if (result.Count == 0 ||
                    !AreSameUtmPoint(result[result.Count - 1], point))
                {
                    result.Add(point);
                }
            }

            if (result.Count > 1 &&
                AreSameUtmPoint(result[0], result[result.Count - 1]))
            {
                result.RemoveAt(result.Count - 1);
            }

            return result;
        }

        private static bool TryReadLatLng(
            JToken? token,
            out double lat,
            out double lng)
        {
            lat = 0;
            lng = 0;

            if (token is not JObject obj)
            {
                return false;
            }

            if (!TryReadDouble(obj["lat"], out lat))
            {
                return false;
            }

            if (!TryReadDouble(obj["lon"], out lng) &&
                !TryReadDouble(obj["lng"], out lng))
            {
                return false;
            }

            return true;
        }

        private static bool TryReadDouble(
            JToken? token,
            out double value)
        {
            value = 0;

            if (token == null)
            {
                return false;
            }

            if (token.Type == JTokenType.Float ||
                token.Type == JTokenType.Integer)
            {
                value = token.Value<double>();
                return true;
            }

            return double.TryParse(
                token.ToString(),
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out value);
        }

        private static string GetBestName(JObject? tags)
        {
            var nameUk = GetTag(tags, "name:uk");

            if (!string.IsNullOrWhiteSpace(nameUk))
            {
                return nameUk.Trim();
            }

            var name = GetTag(tags, "name");

            return name.Trim();
        }

        private bool TryCreateSettlementPoint(
            double latitude,
            double longitude,
            out SettlementPoint point)
        {
            point = new SettlementPoint();

            if (!_tryConvertLatLngToUtm(
                    latitude,
                    longitude,
                    out var utm,
                    out var zone,
                    out var band))
            {
                return false;
            }

            point = new SettlementPoint(
                utm.X,
                utm.Y,
                zone,
                band,
                latitude,
                longitude);
            return true;
        }

        private static List<string> GetNameAliases(JObject? tags)
        {
            var result = new List<string>();

            foreach (var key in new[]
                     {
                         "name",
                         "name:uk",
                         "name:en",
                         "name:ru",
                         "official_name",
                         "short_name",
                         "alt_name",
                         "old_name"
                     })
            {
                var value = GetTag(tags, key);

                foreach (var alias in value.Split(
                             ';',
                             StringSplitOptions.RemoveEmptyEntries |
                             StringSplitOptions.TrimEntries))
                {
                    if (!result.Contains(
                            alias,
                            StringComparer.OrdinalIgnoreCase))
                    {
                        result.Add(alias);
                    }
                }
            }

            return result;
        }

        private static string GetFirstNonEmptyTag(
            JObject? tags,
            params string[] keys)
        {
            foreach (var key in keys)
            {
                var value = GetTag(tags, key);

                if (!string.IsNullOrWhiteSpace(value))
                {
                    return value;
                }
            }

            return string.Empty;
        }

        private static string GetTag(
            JObject? tags,
            string key)
        {
            if (tags == null)
            {
                return string.Empty;
            }

            return tags[key]?.ToString()?.Trim() ?? string.Empty;
        }

        private static long GetLong(JToken? token)
        {
            if (token == null)
            {
                return 0;
            }

            return long.TryParse(
                token.ToString(),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out var value)
                ? value
                : 0;
        }

        private static string GetString(JToken? token)
        {
            return token?.ToString()?.Trim() ?? string.Empty;
        }

        private static List<SettlementGeometryItem> Deduplicate(
            List<SettlementGeometryItem> settlements)
        {
            var result = new List<SettlementGeometryItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var settlement in settlements)
            {
                var key = settlement.OsmId > 0
                    ? $"{settlement.OsmType}:{settlement.OsmId}"
                    : $"{settlement.Name}:{settlement.Place}";

                if (!seen.Add(key))
                {
                    continue;
                }

                result.Add(settlement);
            }

            return result;
        }

        private static bool IsUsableSettlementName(string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return false;
            }

            var name = value.Trim().ToLowerInvariant();

            return !name.Contains("область") &&
                   !name.Contains("район") &&
                   !name.Contains("громада") &&
                   !name.Contains("територіальна громада") &&
                   !name.Contains("міська громада") &&
                   !name.Contains("сільська громада") &&
                   !name.Contains("селищна громада") &&
                   !name.Contains("міська рада") &&
                   !name.Contains("сільська рада") &&
                   !name.Contains("селищна рада") &&
                   !name.Contains("вулиця") &&
                   !name.Contains("вул.") &&
                   !name.Contains("площа") &&
                   !name.Contains("дорога") &&
                   !name.Contains("шосе") &&
                   !name.Contains("стадіон") &&
                   !name.Contains("парк") &&
                   !name.Contains("кладовище") &&
                   !name.Contains("урочище");
        }

        private static bool AreSameUtmPoint(PointF a, PointF b)
        {
            return Math.Abs(a.X - b.X) <= 0.5 &&
                   Math.Abs(a.Y - b.Y) <= 0.5;
        }

        private static double GetDistance(PointF a, PointF b)
        {
            var dx = a.X - b.X;
            var dy = a.Y - b.Y;

            return Math.Sqrt(dx * dx + dy * dy);
        }

        private static PointF GetAveragePoint(List<List<PointF>> polygons)
        {
            double sumX = 0;
            double sumY = 0;
            var count = 0;

            foreach (var polygon in polygons)
            {
                foreach (var point in polygon)
                {
                    sumX += point.X;
                    sumY += point.Y;
                    count++;
                }
            }

            if (count == 0)
            {
                return PointF.Empty;
            }

            return new PointF(
                (float)(sumX / count),
                (float)(sumY / count));
        }

        private static string NormalizeNameKey(string? value)
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

        private static HashSet<string> GetNormalizedSettlementNames(
            SettlementGeometryItem settlement)
        {
            return new HashSet<string>(
                (settlement.NameAliases ?? new List<string>())
                    .Append(settlement.Name)
                    .Select(NormalizeNameKey)
                    .Where(x => !string.IsNullOrWhiteSpace(x)),
                StringComparer.OrdinalIgnoreCase);
        }

        private static double GetMaximumRelationAttachDistanceMeters(
            string? place)
        {
            return place?.Trim().ToLowerInvariant() switch
            {
                "city" => 8000.0,
                "town" => 5000.0,
                "village" => 2500.0,
                "hamlet" => 1500.0,
                "isolated_dwelling" => 800.0,
                _ => Math.Min(2000.0, MaxRelationAttachDistanceMeters)
            };
        }

        private static bool IsNearestRelationMatchUnambiguous(
            IEnumerable<double> orderedDistances)
        {
            var distances = orderedDistances.Take(2).ToList();

            if (distances.Count < 2 || distances[0] <= 250.0)
            {
                return true;
            }

            return distances[1] - distances[0] >= 500.0;
        }

        private static bool HasSameRelationAdministrativeContext(
            SettlementGeometryItem settlement,
            RelationGeometryCandidate relation)
        {
            var hasContext =
                !string.IsNullOrWhiteSpace(settlement.Region) ||
                !string.IsNullOrWhiteSpace(settlement.District) ||
                !string.IsNullOrWhiteSpace(relation.Region) ||
                !string.IsNullOrWhiteSpace(relation.District);

            return hasContext &&
                   string.Equals(
                       settlement.Region,
                       relation.Region,
                       StringComparison.OrdinalIgnoreCase) &&
                   string.Equals(
                       settlement.District,
                       relation.District,
                       StringComparison.OrdinalIgnoreCase);
        }

        private static string ToInvariant(double value)
        {
            return value.ToString("0.########", CultureInfo.InvariantCulture);
        }

        private sealed class ResidentialGeometryItem
        {
            public string Name { get; set; } = string.Empty;

            public string OsmType { get; set; } = string.Empty;

            public long OsmId { get; set; }

            public PointF Center { get; set; }

            public List<List<PointF>> Polygons { get; set; } = new();
        }

        private sealed class RelationGeometryCandidate
        {
            public string Name { get; set; } = string.Empty;

            public List<string> NameAliases { get; set; } = new();

            public string Place { get; set; } = string.Empty;

            public string Boundary { get; set; } = string.Empty;

            public string AdminLevel { get; set; } = string.Empty;

            public string Region { get; set; } = string.Empty;

            public string District { get; set; } = string.Empty;

            public bool IsAdministrativeBoundaryOnly { get; set; }

            public long RelationId { get; set; }

            public SettlementPoint? Center { get; set; }

            public List<long> LabelNodeIds { get; set; } = new();
        }

        private sealed class RelationCandidateLoadResult
        {
            public List<RelationGeometryCandidate> Candidates { get; set; } =
                new();

            public bool Succeeded { get; set; }
        }

        private sealed class SettlementRetryTarget
        {
            public SettlementGeometryItem Settlement { get; set; } = new();

            public List<RelationGeometryCandidate> RelationCandidates { get; set; } =
                new();

            public bool RetryOpenStreetMap { get; set; }

            public bool RetryExternalProviders { get; set; }
        }

    }
}
