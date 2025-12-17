using System;
using System.Drawing;
using Maps.Rendering.Overlays;

namespace Maps.Services.Map
{
    /// <summary>
    /// Adapter to allow Rendering.IMapOverlay to be used by Services.Map provider overlays (which expect Draw(Graphics)).
    /// </summary>
    public class RenderingOverlayAdapter : IMapOverlay
    {
        private readonly global::Maps.Rendering.Overlays.IMapOverlay _inner;
        private readonly Func<PointF, Point> _geoToScreen;

        public RenderingOverlayAdapter(global::Maps.Rendering.Overlays.IMapOverlay inner, Func<PointF, Point> geoToScreen)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _geoToScreen = geoToScreen ?? throw new ArgumentNullException(nameof(geoToScreen));
        }

        public void Draw(Graphics g)
        {
            System.Diagnostics.Debug.WriteLine($"RenderingOverlayAdapter.Draw called for {_inner.GetType().Name}");
            Console.WriteLine($"RenderingOverlayAdapter.Draw called for {_inner.GetType().Name}");
            _inner.Render(g, _geoToScreen);
        }
    }
}
