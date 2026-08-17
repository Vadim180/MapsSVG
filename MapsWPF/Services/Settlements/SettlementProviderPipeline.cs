using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GMap.NET.MapProviders;

namespace MapsWPF.Services.Settlements
{
    public sealed class SettlementProviderPipeline
    {
        private readonly SettlementOverpassLoader _overpassLoader;
        private readonly SettlementExternalGeometryLoader _externalLoader;

        public SettlementProviderPipeline(
            SettlementOverpassLoader overpassLoader,
            SettlementExternalGeometryLoader externalLoader)
        {
            _overpassLoader = overpassLoader ??
                throw new ArgumentNullException(nameof(overpassLoader));
            _externalLoader = externalLoader ??
                throw new ArgumentNullException(nameof(externalLoader));
        }

        public async Task<SettlementGeometryCache> LoadAsync(
            SettlementCacheBounds bounds,
            SettlementGeometryCache existingCache,
            IEnumerable<GMapProvider> mapProviders,
            SettlementProviderOptions options,
            CancellationToken cancellationToken,
            Action<SettlementGeometryCache>? onSnapshotReady = null,
            Action<SettlementContourLoadProgress>? onProgress = null,
            bool forceRefresh = false)
        {
            options ??= new SettlementProviderOptions();
            var providers = mapProviders?.ToList() ?? new List<GMapProvider>();
            var availability = _externalLoader.GetAvailability(options, providers);
            var activeProviders = availability.ActiveProviders.ToList();

            if (options.UseOpenStreetMapOverpass)
            {
                activeProviders.Insert(
                    0,
                    SettlementDataSources.OpenStreetMapOverpass);
            }

            if (activeProviders.Count == 0)
            {
                throw new InvalidOperationException(
                    "Не налаштовано жодного активного джерела даних населених пунктів.");
            }

            if (!options.UseOpenStreetMapOverpass)
            {
                return await EnrichExistingCacheAsync(
                    bounds,
                    existingCache,
                    providers,
                    options,
                    activeProviders,
                    cancellationToken,
                    onSnapshotReady,
                    onProgress,
                    forceRefresh);
            }

            Func<SettlementGeometryItem, CancellationToken,
                Task<SettlementCityProviderResult>>? enrichSettlement = null;

            if (options.UseAllAvailableProviders &&
                availability.ActiveProviders.Count > 0)
            {
                enrichSettlement = (settlement, token) =>
                    _externalLoader.EnrichSettlementAsync(
                        bounds,
                        settlement,
                        options,
                        providers,
                        token,
                        forceRefresh);
            }

            return await _overpassLoader.LoadContoursAsync(
                bounds,
                cancellationToken,
                onSnapshotReady,
                progress => onProgress?.Invoke(
                    WrapProgress(progress, activeProviders)),
                existingCache,
                enrichSettlement,
                forceRefresh);
        }

        private async Task<SettlementGeometryCache> EnrichExistingCacheAsync(
            SettlementCacheBounds bounds,
            SettlementGeometryCache existingCache,
            IReadOnlyList<GMapProvider> mapProviders,
            SettlementProviderOptions options,
            IReadOnlyList<string> activeProviders,
            CancellationToken cancellationToken,
            Action<SettlementGeometryCache>? onSnapshotReady,
            Action<SettlementContourLoadProgress>? onProgress,
            bool forceRefresh)
        {
            var working = SettlementGeometryMerger.CloneCache(existingCache);
            SettlementGeometryMerger.Normalize(working);
            working.Bounds = bounds;

            var targets = working.Settlements
                .Where(x => _externalLoader.IsSettlementInsideBounds(x, bounds))
                .OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
                .ToList();

            if (targets.Count == 0)
            {
                throw new InvalidOperationException(
                    "У кеші немає населених пунктів цієї області. " +
                    "Увімкніть OpenStreetMap/Overpass для формування черги.");
            }

            var retryTargets = new List<SettlementGeometryItem>();

            for (var index = 0; index < targets.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var settlement = targets[index];

                onProgress?.Invoke(WrapProgress(
                    new SettlementContourLoadProgress
                    {
                        Completed = index,
                        Total = targets.Count,
                        CurrentSettlementName = settlement.Name,
                        State = "Паралельний запит до доступних API..."
                    },
                    activeProviders));

                var result = await _externalLoader.EnrichSettlementAsync(
                    bounds,
                    settlement,
                    options,
                    mapProviders,
                    cancellationToken,
                    forceRefresh);

                if (forceRefresh)
                {
                    SettlementGeometryMerger
                        .MergeSettlementCandidatesReplacingProviders(
                            settlement,
                            result.Settlement,
                            result.RefreshedCenterProviders,
                            result.RefreshedPolygonProviders);
                }
                else
                {
                    SettlementGeometryMerger.MergeSettlementCandidates(
                        settlement,
                        result.Settlement);
                }
                SettlementGeometryQualitySelector.RecalculateAll(
                    working.Settlements);

                if ((forceRefresh ||
                     !SettlementGeometryMerger.HasUsablePolygons(settlement)) &&
                    result.HadTransientPolygonFailure)
                {
                    retryTargets.Add(settlement);
                }

                onSnapshotReady?.Invoke(
                    SettlementGeometryMerger.CloneCache(working));

                var preferredProvider =
                    SettlementGeometryQualitySelector.GetPreferredPolygonProvider(
                        settlement);
                var rejectionReason = SettlementGeometryQualitySelector
                    .GetPreferredRejectionReason(settlement);
                var state = string.IsNullOrWhiteSpace(preferredProvider)
                    ? settlement.FallbackPoint != null
                        ? !string.IsNullOrWhiteSpace(rejectionReason)
                            ? $"Контур відкинуто: {rejectionReason}; " +
                              "показано центр"
                            : "Контур не знайдено, вибрано найкращий центр"
                        : "Геометрію не знайдено"
                    : $"Вибрано контур: {preferredProvider}";

                onProgress?.Invoke(WrapProgress(
                    new SettlementContourLoadProgress
                    {
                        Completed = index + 1,
                        Total = targets.Count,
                        CurrentSettlementName = settlement.Name,
                        State = state,
                        HasPolygon = SettlementGeometryMerger.HasUsablePolygons(
                            settlement),
                        HasFallbackCenter = settlement.FallbackPoint != null,
                        OverallPercent = targets.Count <= 0
                            ? 90.0
                            : (index + 1) * 90.0 / targets.Count
                    },
                    activeProviders));
            }

            for (var retryIndex = 0;
                 retryIndex < retryTargets.Count;
                 retryIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var settlement = retryTargets[retryIndex];

                onProgress?.Invoke(WrapProgress(
                    new SettlementContourLoadProgress
                    {
                        Completed = retryIndex,
                        Total = retryTargets.Count,
                        CurrentSettlementName = settlement.Name,
                        State = "Повторна спроба після тимчасової помилки...",
                        OverallPercent = 90.0 +
                            retryIndex * 10.0 / retryTargets.Count
                    },
                    activeProviders));

                var result = await _externalLoader.EnrichSettlementAsync(
                    bounds,
                    settlement,
                    options,
                    mapProviders,
                    cancellationToken,
                    forceRefresh);

                if (forceRefresh)
                {
                    SettlementGeometryMerger
                        .MergeSettlementCandidatesReplacingProviders(
                            settlement,
                            result.Settlement,
                            result.RefreshedCenterProviders,
                            result.RefreshedPolygonProviders);
                }
                else
                {
                    SettlementGeometryMerger.MergeSettlementCandidates(
                        settlement,
                        result.Settlement);
                }

                SettlementGeometryQualitySelector.RecalculateAll(
                    working.Settlements);
                onSnapshotReady?.Invoke(
                    SettlementGeometryMerger.CloneCache(working));

                onProgress?.Invoke(WrapProgress(
                    new SettlementContourLoadProgress
                    {
                        Completed = retryIndex + 1,
                        Total = retryTargets.Count,
                        CurrentSettlementName = settlement.Name,
                        State = SettlementGeometryMerger.HasUsablePolygons(
                            settlement)
                            ? "Контур отримано з повторної спроби"
                            : "Повторна спроба не дала полігон; залишено центр",
                        HasPolygon = SettlementGeometryMerger.HasUsablePolygons(
                            settlement),
                        HasFallbackCenter = settlement.FallbackPoint != null,
                        OverallPercent = 90.0 +
                            (retryIndex + 1) * 10.0 / retryTargets.Count
                    },
                    activeProviders));
            }

            if (retryTargets.Count == 0)
            {
                onProgress?.Invoke(WrapProgress(
                    new SettlementContourLoadProgress
                    {
                        Completed = targets.Count,
                        Total = targets.Count,
                        State = "Завантаження завершено; повторних спроб не потрібно",
                        OverallPercent = 100.0
                    },
                    activeProviders));
            }

            return working;
        }

        private static SettlementContourLoadProgress WrapProgress(
            SettlementContourLoadProgress progress,
            IReadOnlyList<string> activeProviders)
        {
            var fraction = progress.Total <= 0
                ? 0.0
                : Math.Clamp(
                    progress.Completed / (double)progress.Total,
                    0.0,
                    1.0);
            var providerName = activeProviders.Count == 1
                ? activeProviders[0]
                : "паралельний пул";

            return new SettlementContourLoadProgress
            {
                Completed = progress.Completed,
                Total = progress.Total,
                CurrentSettlementName = progress.CurrentSettlementName,
                State = progress.State,
                HasPolygon = progress.HasPolygon,
                HasFallbackCenter = progress.HasFallbackCenter,
                ProviderName = providerName,
                ProviderIndex = 1,
                ProviderCount = activeProviders.Count,
                OverallPercent = progress.OverallPercent ?? fraction * 100.0
            };
        }
    }
}
