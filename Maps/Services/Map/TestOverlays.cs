using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Reflection;
using GMap.NET.WindowsForms;

namespace Maps.Services.Map
{
    // Тестовий оверлей, який малює червону крапку в центрі видимої області карти і логгить виклики Draw.
    public class TestOverlay : IMapOverlay
    {
        private readonly GMapControl _map;
        private int _drawCount = 0;

        public TestOverlay(GMapControl map)
        {
            _map = map;
        }

        public void Draw(Graphics g)
        {
            _drawCount++;
            Debug.WriteLine($"TestOverlay.Draw called #{_drawCount}");

            var center = _map.FromLatLngToLocal(_map.Position);
            float x = (float)center.X;
            float y = (float)center.Y;

            // Малюємо помітну червону точку у центрі
            using var b = new SolidBrush(Color.Red);
            g.FillEllipse(b, x - 8, y - 8, 16, 16);

            // Додаємо тонкий чорний контур
            using var p = new Pen(Color.Black, 1);
            g.DrawEllipse(p, x - 8, y - 8, 16, 16);
        }
    }

    // Оверлей, який дублює текст позиції на поверхню карти (щоб бути завжди on-top)
    public class PositionOverlay : IMapOverlay
    {
        private readonly GMapControl _map;
        private volatile float _lat;
        private volatile float _lng;

        public PositionOverlay(GMapControl map)
        {
            _map = map;
            // Ініціальна позиція
            _lat = (float)_map.Position.Lat;
            _lng = (float)_map.Position.Lng;

            // Підписуємося на внутрішню подію карти — так краще реагувати на зміни
            _map.OnPositionChanged += _ => Update(_);
        }

        private void Update(GMap.NET.PointLatLng p)
        {
            _lat = (float)p.Lat;
            _lng = (float)p.Lng;
        }

        public void Draw(Graphics g)
        {
            // Підпис у верхньому лівому куті карти
            double lat = _lat;
            double lng = _lng;

            string dmsLat = ToDMS(lat, true);
            string dmsLng = ToDMS(lng, false);

            var (zone, hemisphere, easting, northing) = LatLonToUTM(lat, lng);
            string utmText = $"Zone {zone}{hemisphere}  E: {easting:F1}  N: {northing:F1}";

            string fullMgrs = GetMGRSFromUTM(zone, hemisphere, easting, northing);

            string text = $"Позиція: {lat:F6}, {lng:F6} | Zoom: {_map.Zoom:F2}\n" +
                          $"DMS: {dmsLat}  {dmsLng}\n" +
                          $"UTM: {utmText}\n" +
                          $"MGRS: {fullMgrs}";

            using var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near };
            var font = SystemFonts.DefaultFont;
            var size = g.MeasureString(text, font);

            var rect = new RectangleF(8, 8, size.Width + 8, size.Height + 8);
            using var back = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
            using var fore = new SolidBrush(Color.White);

            g.FillRectangle(back, rect);
            g.DrawString(text, font, fore, new PointF(rect.X + 4, rect.Y + 4), sf);
        }

        private static string ToDMS(double value, bool isLatitude)
        {
            double absVal = Math.Abs(value);
            int deg = (int)Math.Floor(absVal);
            double rem = (absVal - deg) * 60.0;
            int min = (int)Math.Floor(rem);
            double sec = (rem - min) * 60.0;
            string dir;
            if (isLatitude)
                dir = value >= 0 ? "N" : "S";
            else
                dir = value >= 0 ? "E" : "W";
            return $"{deg}°{min:00}'{sec:00.##}\"{dir}";
        }

        private static (int zone, char hemisphere, double easting, double northing) LatLonToUTM(double lat, double lon)
        {
            const double a = 6378137.0; // WGS84
            const double f = 1.0 / 298.257223563;
            const double k0 = 0.9996;

            double latRad = lat * Math.PI / 180.0;
            double lonRad = lon * Math.PI / 180.0;

            int zone = (int)Math.Floor((lon + 180.0) / 6.0) + 1;
            double lonOrigin = (zone - 1) * 6 - 180 + 3;
            double lonOriginRad = lonOrigin * Math.PI / 180.0;

            double e2 = f * (2 - f);
            double ePrime2 = e2 / (1 - e2);
            double N = a / Math.Sqrt(1 - e2 * Math.Sin(latRad) * Math.Sin(latRad));
            double T = Math.Tan(latRad) * Math.Tan(latRad);
            double C = ePrime2 * Math.Cos(latRad) * Math.Cos(latRad);
            double A = Math.Cos(latRad) * (lonRad - lonOriginRad);

            double M = a * ((1 - e2 / 4 - 3 * e2 * e2 / 64 - 5 * e2 * e2 * e2 / 256) * latRad
                            - (3 * e2 / 8 + 3 * e2 * e2 / 32 + 45 * e2 * e2 * e2 / 1024) * Math.Sin(2 * latRad)
                            + (15 * e2 * e2 / 256 + 45 * e2 * e2 * e2 / 1024) * Math.Sin(4 * latRad)
                            - (35 * e2 * e2 * e2 / 3072) * Math.Sin(6 * latRad));

            double easting = k0 * N * (A + (1 - T + C) * Math.Pow(A, 3) / 6 + (5 - 18 * T + T * T + 72 * C - 58 * ePrime2) * Math.Pow(A, 5) / 120) + 500000.0;

            double northing = k0 * (M + N * Math.Tan(latRad) * (A * A / 2 + (5 - T + 9 * C + 4 * C * C) * Math.Pow(A, 4) / 24
                                + (61 - 58 * T + T * T + 600 * C - 330 * ePrime2) * Math.Pow(A, 6) / 720));

            char hemisphere = lat >= 0 ? 'N' : 'S';
            if (lat < 0) northing += 10000000.0;

            return (zone, hemisphere, easting, northing);
        }

        private static string GetMGRSFromUTM(int zone, char hemisphere, double easting, double northing)
        {
            // Prefer existing CoordinateConverter implementation which already builds a short MGRS.
            try
            {
                var conv = new CoordinateConverter();
                // CoordinateConverter.FormatShortMGRSFromUTM expects a PointF (Easting, Northing)
                var mgrsShort = conv.FormatShortMGRSFromUTM(new PointF((float)easting, (float)northing));
                if (!string.IsNullOrWhiteSpace(mgrsShort)) return mgrsShort;
            }
            catch { /* continue to reflection fallback */ }

            // Fallback: try to use UniversalTransverseMercator via reflection if available
            try
            {
                var utmType = AppDomain.CurrentDomain.GetAssemblies()
                    .SelectMany(a => { try { return a.GetTypes(); } catch { return new Type[0]; } })
                    .FirstOrDefault(t => string.Equals(t.Name, "UniversalTransverseMercator", StringComparison.Ordinal));

                if (utmType != null)
                {
                    var utmInstance = Activator.CreateInstance(utmType, hemisphere.ToString(), zone, easting, northing);
                    var convertMethod = utmType.GetMethod("ConvertUTMtoLatLong", BindingFlags.Public | BindingFlags.Static);
                    if (convertMethod != null && utmInstance != null)
                    {
                        var corrected = convertMethod.Invoke(null, new object[] { utmInstance });
                        if (corrected != null)
                        {
                            var mgrsProp = corrected.GetType().GetProperty("MGRS");
                            if (mgrsProp != null)
                            {
                                var mgrsVal = mgrsProp.GetValue(corrected);
                                if (mgrsVal != null) return mgrsVal.ToString() ?? "N/A";
                            }
                        }
                    }
                }
            }
            catch { }

            return "N/A";
        }
    }
}
