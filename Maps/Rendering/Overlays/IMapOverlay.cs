using System;
using System.Drawing;

namespace Maps.Rendering.Overlays
{
    /// <summary>
    /// Basic overlay abstraction for rendering map overlays.
    /// </summary>
    public interface IMapOverlay
    {
        bool Visible { get; set; }
        /// <summary>Render overlay. geoToScreen maps a point in map coordinates into screen pixels.</summary>
        void Render(Graphics g, Func<PointF, Point> geoToScreen);
    }
}
