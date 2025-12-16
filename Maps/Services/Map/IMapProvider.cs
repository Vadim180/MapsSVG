using System;
using System.Drawing;
using System.Windows.Forms;

namespace Maps.Services.Map
{
    public interface IMapProvider
    {
        void Initialize(Control container);
        void SetBounds(double north, double south, double east, double west);
        PointF ScreenToGeo(Point screen);
        Point GeoToScreen(PointF geo);
        void AddOverlay(IMapOverlay overlay);
        void Refresh();
        event Action<PointF>? OnClick;
        event Action<PointF>? OnPositionChanged;
    }
}