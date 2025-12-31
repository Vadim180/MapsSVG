using System.Drawing;
using GMap.NET;

namespace MapsWPF.Services
{
    public interface IMapService
    {
        bool TryLatLngToUTM(double lat, double lng, out PointF utm, out int utmZone, out char band);
        bool TryUTMToLatLng(PointF utm, out double lat, out double lng);
        string FormatShortMGRSFromUTM(PointF utm, int utmZone);
        string FormatUTM(PointF utm, int utmZone, char band);
    }
}