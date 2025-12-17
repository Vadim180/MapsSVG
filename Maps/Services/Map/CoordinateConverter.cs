using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using Maps.Models;
using ProjNet.CoordinateSystems;
using ProjNet.CoordinateSystems.Transformations;
using CoordinateSharp;
using Accord.Math;

namespace Maps.Services.Map
{
    public class CoordinateConverter
    {
        private double[]? eastingCoeffs;
        private double[]? northingCoeffs;
        private double[,]? _inv;
        private double _e0, _n0;

        public void SetCoefficients(double[] easting, double[] northing)
        {
            eastingCoeffs = easting;
            northingCoeffs = northing;
            RebuildInverse();
        }

        public void Clear()
        {
            eastingCoeffs = null;
            northingCoeffs = null;
            _inv = null;
        }

        public PointF PixelToUTM(PointF pixel)
        {
            if (eastingCoeffs == null || northingCoeffs == null)
                return PointF.Empty;

            double x = pixel.X;
            double y = pixel.Y;

            double easting = eastingCoeffs[0] * x + eastingCoeffs[1] * y + eastingCoeffs[2];
            double northing = northingCoeffs[0] * x + northingCoeffs[1] * y + northingCoeffs[2];

            return new PointF((float)easting, (float)northing);
        }

        public PointF UTMToPixel(PointF utm)
        {
            if (_inv == null) return PointF.Empty;

            double e = utm.X - _e0;
            double n = utm.Y - _n0;

            double x = _inv[0, 0] * e + _inv[0, 1] * n;
            double y = _inv[1, 0] * e + _inv[1, 1] * n;

            return new PointF((float)x, (float)y);
        }

        public void RebuildInverse()
        {
            if (eastingCoeffs == null || northingCoeffs == null || eastingCoeffs.Length < 3 || northingCoeffs.Length < 3)
            {
                _inv = null;
                return;
            }

            try
            {
                var m = new double[,] {
                  { eastingCoeffs[0], eastingCoeffs[1] },
                  { northingCoeffs[0], northingCoeffs[1] }
                };

                _inv = Matrix.Inverse(m);
                _e0 = eastingCoeffs[2];
                _n0 = northingCoeffs[2];
            }
            catch
            {
                _inv = null;
            }
        }

        /// <summary>
        /// Повертає true, коли калібрування встановлено та інверсна матриця доступна.
        /// </summary>
        public bool IsCalibrated => _inv != null;

        public double[] SolveAffineTransform(List<PointF> pixels, List<PointF> coords)
        {
            if (pixels.Count < 4 || coords.Count < 4)
                return new double[3];

            double[,] matrix = new double[4, 3];
            var vector = new double[4];

            for (int i = 0; i < 4; i++)
            {
                matrix[i, 0] = pixels[i].X;
                matrix[i, 1] = pixels[i].Y;
                matrix[i, 2] = 1;
                vector[i] = coords[i].X;
            }

            return matrix.PseudoInverse().Dot(vector);
        }

        public string FormatShortMGRSFromUTM(PointF utm)
        {
            try
            {
                string hemisphere = utm.Y > 0 ? "N" : "S";

                var tempUtm = new UniversalTransverseMercator(hemisphere, 37, utm.X, utm.Y);
                var coord = UniversalTransverseMercator.ConvertUTMtoLatLong(tempUtm);

                double latitude = coord.Latitude.DecimalDegree;
                double longitude = coord.Longitude.DecimalDegree;

                int utmZone = (int)Math.Floor((longitude + 180) / 6) + 1;

                char bandLetter = GetUTMBandLetter(latitude);

                var utmWithCorrectZone = new UniversalTransverseMercator(hemisphere, utmZone, utm.X, utm.Y);
                var correctedCoord = UniversalTransverseMercator.ConvertUTMtoLatLong(utmWithCorrectZone);
                string fullMgrsString = correctedCoord.MGRS.ToString();

                var parts = fullMgrsString.Split(' ');
                if (parts.Length >= 4 && parts[2].Length >= 2 && parts[3].Length >= 2)
                {
                    string square = parts[1];
                    string shortEast = parts[2].Substring(0, 2);
                    string shortNorth = parts[3].Substring(0, 2);
                    return $"{utmZone}{bandLetter} {square} {shortEast} {shortNorth}";
                }

                return fullMgrsString;
            }
            catch
            {
                return "Невірні координати";
            }
        }

        /// <summary>
        /// Спроба перетворити UTM (Easting, Northing) в Lat/Lon (WGS84). Повертає true якщо вдалося.
        /// Виконує двоетапну корекцію з визначенням зони за отриманою довготою.
        /// </summary>
        public bool TryUTMToLatLng(PointF utm, out double latitude, out double longitude)
        {
            latitude = double.NaN;
            longitude = double.NaN;

            try
            {
                string hemisphere = utm.Y > 0 ? "N" : "S";

                // First-pass with a default zone (37) to get approximate longitude
                var tempUtm = new UniversalTransverseMercator(hemisphere, 37, utm.X, utm.Y);
                var approx = UniversalTransverseMercator.ConvertUTMtoLatLong(tempUtm);

                double approxLon = approx.Longitude.DecimalDegree;

                int utmZone = (int)Math.Floor((approxLon + 180) / 6) + 1;

                // Recreate with correct zone
                var correctedUtm = new UniversalTransverseMercator(hemisphere, utmZone, utm.X, utm.Y);
                var corrected = UniversalTransverseMercator.ConvertUTMtoLatLong(correctedUtm);

                latitude = corrected.Latitude.DecimalDegree;
                longitude = corrected.Longitude.DecimalDegree;
                return true;
            }
            catch
            {
                return false;
            }
        }

        private char GetUTMBandLetter(double latitude)
        {
            string bands = "CDEFGHJKLMNPQRSTUVWX";
            int index = (int)Math.Floor((latitude + 80) / 8);

            if (index < 0) index = 0;
            if (index > 19) index = 19;

            return bands[index];
        }
    }
}
