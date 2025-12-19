using System;
using System.Drawing;

namespace Maps.Rendering.Overlays
{
    public interface IMapOverlay
    {
        bool IsVisible { get; set; }
        int ZIndex { get; }
        void Draw(Graphics g, RectangleF viewArea, Func<PointF, PointF>? coordTransform = null);
    }
}
