using System.Drawing;
using GMap.NET;

namespace MapsWPF.Services
{
    public class MapService
    {
        private readonly CoordinateConverter _converter;

        public MapService(CoordinateConverter converter)
        {
            _converter = converter;
        }

        public bool TryLatLngToUTM(double lat, double lng, out PointF utm, out int utmZone, out char band)
        {
            return _converter.TryLatLngToUTM(lat, lng, out utm, out utmZone, out band);
        }

        public bool TryUTMToLatLng(PointF utm, out double lat, out double lng)
        {
            return _converter.TryUTMToLatLng(utm, out lat, out lng);
        }

        public string FormatShortMGRSFromUTM(PointF utm, int utmZone)
        {
            return _converter.FormatShortMGRSFromUTM(utm, utmZone);
        }

        public string FormatUTM(PointF utm, int utmZone, char band)
        {
            return _converter.FormatUTM(utm, utmZone, band);
        }
    }
}