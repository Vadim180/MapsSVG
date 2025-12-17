using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;
using Maps.Services.Map;
using RenderingOverlay = Maps.Rendering.Overlays.IMapOverlay;

namespace Maps.Controllers
{
    /// <summary>
    /// Контролер карти: відповідає за ініціалізацію провайдера карти,
    /// підписку на події, управління оверлеями і прості операції калібрування (делегує в CoordinateConverter).
    /// </summary>
    public class MapController : IDisposable
    {
        private IMapProvider? _provider;
        private CoordinateConverter? _coordinateConverter;

        // Зібрані адаптери/оверлеї для можливості видаляти/перемикати провайдер
        private readonly List<IMapOverlay> _providerOverlays = new(); // provider-side overlays (Services.Map.IMapOverlay)

        public event Action<PointF>? OnClick;
        public event Action<PointF>? OnPositionChanged;

        public MapController(CoordinateConverter? converter = null)
        {
            _coordinateConverter = converter;
        }

        public CoordinateConverter? CoordinateConverter => _coordinateConverter;

        public void SetCoordinateConverter(CoordinateConverter conv)
        {
            _coordinateConverter = conv ?? throw new ArgumentNullException(nameof(conv));
            // If current provider is SVG, ensure it has the converter assigned
            if (_provider is SvgMapProvider svg)
            {
                svg.CoordinateConverter = _coordinateConverter;
            }
        }

        /// <summary>
        /// Встановити/перемкнути провайдера карти. Ініціалізує провайдера у вказаному контейнері
        /// і підписується на його події.
        /// </summary>
        public void SetProvider(IMapProvider provider, Control container)
        {
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            if (container == null) throw new ArgumentNullException(nameof(container));

            // Очистимо попередні підписки і оверлеї
            if (_provider != null)
            {
                try
                {
                    _provider.OnClick -= Provider_OnClick;
                    _provider.OnPositionChanged -= Provider_OnPositionChanged;
                }
                catch { }
            }

            _provider = provider;

            // Якщо це SVG — встановимо CoordinateConverter
            if (_provider is SvgMapProvider svgProvider && _coordinateConverter != null)
            {
                svgProvider.CoordinateConverter = _coordinateConverter;
            }

            _provider.OnClick += Provider_OnClick;
            _provider.OnPositionChanged += Provider_OnPositionChanged;

            _provider.Initialize(container);

            // Ре-додаємо збережені оверлеї (якщо були)
            foreach (var ov in _providerOverlays)
            {
                try { _provider.AddOverlay(ov); } catch { }
            }
        }

        private void Provider_OnPositionChanged(PointF p) => OnPositionChanged?.Invoke(p);
        private void Provider_OnClick(PointF p) => OnClick?.Invoke(p);

        public void AddProviderOverlay(IMapOverlay overlay)
        {
            if (overlay == null) return;
            _providerOverlays.Add(overlay);
            try { _provider?.AddOverlay(overlay); } catch { }
        }

        /// <summary>
        /// Додає Rendering overlay (логічні координати) — створює адаптер і додає до провайдера.
        /// geoToScreen буде делегувати в провайдера GeoToScreen.
        /// </summary>
        public void AddRenderingOverlay(RenderingOverlay overlay)
        {
            if (overlay == null) return;
            if (_provider == null) throw new InvalidOperationException("Provider not set");

            Func<PointF, Point> geoToScreen = geo => _provider.GeoToScreen(geo);
            var adapter = new RenderingOverlayAdapter(overlay, geoToScreen);
            AddProviderOverlay(adapter);
        }

        public void Refresh() => _provider?.Refresh();

        public void SetBounds(double north, double south, double east, double west) => _provider?.SetBounds(north, south, east, west);

        /// <summary>
        /// Застосувати коефіцієнти калібрування у CoordinateConverter та передати їх SVG провайдеру якщо він активний.
        /// </summary>
        public void ApplyCalibrationCoefficients(double[] eastingCoeffs, double[] northingCoeffs)
        {
            if (_coordinateConverter == null) _coordinateConverter = new CoordinateConverter();
            _coordinateConverter.SetCoefficients(eastingCoeffs, northingCoeffs);
            _coordinateConverter.RebuildInverse();

            if (_provider is SvgMapProvider svg) svg.CoordinateConverter = _coordinateConverter;
        }

        public void Dispose()
        {
            if (_provider != null)
            {
                try
                {
                    _provider.OnClick -= Provider_OnClick;
                    _provider.OnPositionChanged -= Provider_OnPositionChanged;
                }
                catch { }
            }
        }
    }
}
