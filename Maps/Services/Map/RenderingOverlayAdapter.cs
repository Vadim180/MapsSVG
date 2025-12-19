using System;
using System.Drawing;
using Maps.Rendering.Overlays;

namespace Maps.Services.Map
{
    // Адаптер для Rendering.IMapOverlay до Services.Map provider
    public class RenderingOverlayAdapter : IMapOverlay
    {
        private readonly global::Maps.Rendering.Overlays.IMapOverlay _inner;
        private readonly Func<PointF, PointF> _geoToScreen;

        public RenderingOverlayAdapter(global::Maps.Rendering.Overlays.IMapOverlay inner, Func<PointF, PointF> geoToScreen)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _geoToScreen = geoToScreen ?? throw new ArgumentNullException(nameof(geoToScreen));
        }

        public void Draw(Graphics g)
        {
            _inner.Draw(g, RectangleF.Empty, _geoToScreen);
        }
    }
}
