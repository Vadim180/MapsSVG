using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;
using Svg;

namespace Maps.Services.Map
{
    /// <summary>
    /// Тимчасова обгортка для поточного SVG-рендеру.
    /// Реалізує IMapProvider для поступової міграції від in-form SVG logic -> провайдер.
    /// Події OnClick / OnPositionChanged передають UTM-координати (Easting, Northing) як PointF.
    /// </summary>
    public class SvgMapProvider : IMapProvider, IDisposable
    {
        private PictureBox? _pictureBox;
        private Bitmap? _cachedBitmap;
        private readonly List<IMapOverlay> _overlays = new List<IMapOverlay>();

        // Внутрішній стан як у старій реалізації
        private Size _originalImageSize = Size.Empty;
        private float _scale = 1.0f;
        private PointF _imageOffset = new PointF(0, 0);

        private bool _isPanning = false;
        private Point _panStart;

        // Координатний конвертер (UTM <-> pixel)
        public CoordinateConverter? CoordinateConverter { get; set; }

        // IMapProvider events (UTM coords for SVG)
        public event Action<PointF>? OnClick;
        public event Action<PointF>? OnPositionChanged;

        public SvgMapProvider() { }

        public void Initialize(Control container)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));

            if (_pictureBox != null)
            {
                _pictureBox.MouseDown -= PictureBox_MouseDown;
                _pictureBox.MouseMove -= PictureBox_MouseMove;
                _pictureBox.MouseUp -= PictureBox_MouseUp;
                _pictureBox.Paint -= PictureBox_Paint;
                container.Controls.Remove(_pictureBox);
                _pictureBox.Dispose();
                _pictureBox = null;
            }

            _pictureBox = new PictureBox
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                Visible = true
            };

            _pictureBox.MouseDown += PictureBox_MouseDown;
            _pictureBox.MouseMove += PictureBox_MouseMove;
            _pictureBox.MouseUp += PictureBox_MouseUp;
            _pictureBox.Paint += PictureBox_Paint;
            _pictureBox.SizeMode = PictureBoxSizeMode.Normal; // ми самі потім трансформуємо через Graphics

            container.Controls.Add(_pictureBox);
            // Розміщуємо pictureBox позаду інших контролів (лейбли, кнопки залишаються поверх)
            _pictureBox.SendToBack();
        }

        public void SetBounds(double north, double south, double east, double west)
        {
            // Для SVG провайдера поки немає реалізації по обмеженню області (повна карта rasterized)
            // Тут можна запам'ятати allowedArea, якщо пізніше потрібно
        }

        public void SetBitmap(Bitmap bmp)
        {
            _cachedBitmap?.Dispose();
            _cachedBitmap = (Bitmap)bmp.Clone();

            _originalImageSize = _cachedBitmap.Size;
            FitMapToScreen();
            CenterImage();
            _pictureBox?.Invalidate();
        }

        public PictureBox? Control => _pictureBox;

        public float Scale => _scale;
        public Size OriginalImageSize => _originalImageSize;
        public Size CurrentBitmapSize => _cachedBitmap?.Size ?? Size.Empty;

        public void LoadFromFile(string filePath)
        {
            if (!File.Exists(filePath)) throw new FileNotFoundException(filePath);

            SvgDocument doc = SvgDocument.Open(filePath);

            int natW = 0, natH = 0;
            if (doc.Width != null && doc.Height != null && doc.Width.Value > 0 && doc.Height.Value > 0)
            {
                natW = (int)Math.Round(ConvertToPixels(doc.Width));
                natH = (int)Math.Round(ConvertToPixels(doc.Height));
            }
            else if (doc.ViewBox.Width > 0 && doc.ViewBox.Height > 0)
            {
                natW = (int)Math.Round(doc.ViewBox.Width);
                natH = (int)Math.Round(doc.ViewBox.Height);
            }

            if (natW <= 0 || natH <= 0)
            {
                // Fallback to container size
                if (_pictureBox != null)
                {
                    natW = Math.Max(_pictureBox.ClientSize.Width, 1);
                    natH = Math.Max(_pictureBox.ClientSize.Height, 1);
                }
                else
                {
                    natW = Math.Max(1024, 1);
                    natH = Math.Max(768, 1);
                }
            }

            const int MAX_DIM = 8000;
            float scaleClamp = Math.Min(1f, Math.Min((float)MAX_DIM / natW, (float)MAX_DIM / natH));

            int targetW = natW;
            int targetH = natH;

            if (_pictureBox != null && _pictureBox.ClientSize.Width > 0 && _pictureBox.ClientSize.Height > 0)
            {
                float scaleToPanelW = (float)_pictureBox.ClientSize.Width / natW;
                float scaleToPanelH = (float)_pictureBox.ClientSize.Height / natH;
                float suggested = Math.Max(1f, Math.Min(scaleToPanelW, scaleToPanelH));
                float finalScale = Math.Min(suggested, scaleClamp);

                targetW = Math.Max(1, (int)Math.Round(natW * finalScale));
                targetH = Math.Max(1, (int)Math.Round(natH * finalScale));
            }
            else
            {
                targetW = Math.Max(1, (int)Math.Round(natW * scaleClamp));
                targetH = Math.Max(1, (int)Math.Round(natH * scaleClamp));
            }

            Bitmap? bmp = null;
            try
            {
                bmp = doc.Draw(targetW, targetH);
                if (bmp != null) SetBitmap(bmp);
            }
            finally
            {
                bmp?.Dispose();
            }
        }

        public PointF ScreenToGeo(Point screen)
        {
            // Конвертуємо скіни точку в пікселі карти (без масштабу)
            var pixel = ScreenToImagePixel(screen);
            if (CoordinateConverter == null) return PointF.Empty;
            return CoordinateConverter.PixelToUTM(pixel);
        }

        public Point GeoToScreen(PointF geo)
        {
            if (CoordinateConverter == null) throw new InvalidOperationException("CoordinateConverter not set");
            var pixel = CoordinateConverter.UTMToPixel(geo);
            return new Point((int)Math.Round(pixel.X * _scale + _imageOffset.X), (int)Math.Round(pixel.Y * _scale + _imageOffset.Y));
        }

        public void AddOverlay(IMapOverlay overlay)
        {
            if (overlay == null) return;
            _overlays.Add(overlay);
            _pictureBox?.Invalidate();
        }

        public void Refresh()
        {
            _pictureBox?.Invalidate();
        }

        private void PictureBox_Paint(object? sender, PaintEventArgs e)
        {
            if (_cachedBitmap == null) return;

            var g = e.Graphics;

            // Налаштування аналогічні Maps.cs
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = System.Drawing.Drawing2D.PixelOffsetMode.None;

            if (_imageOffset != PointF.Empty)
                g.TranslateTransform(_imageOffset.X, _imageOffset.Y);

            if (Math.Abs(_scale - 1.0f) > 0.0001f)
                g.ScaleTransform(_scale, _scale);

            // Базова карта
            g.DrawImage(_cachedBitmap, 0, 0);

            // Оверлеї
            try
            {
                foreach (var ov in _overlays)
                    ov.Draw(g);
            }
            catch { }
        }

        private void PictureBox_MouseDown(object? sender, MouseEventArgs e)
        {
            if (_cachedBitmap == null) return;
            if (e.Button == MouseButtons.Left)
            {
                _isPanning = true;
                _panStart = e.Location;
            }
            else if (e.Button == MouseButtons.Right)
            {
                // початок можливого click/measure — зберігаємо старт
                _panStart = e.Location;
            }
        }

        private void PictureBox_MouseMove(object? sender, MouseEventArgs e)
        {
            if (_cachedBitmap == null) return;

            // NOTE: When Maps is subscribing to the provider events, Maps will handle panning.
            // We avoid double-handling here.
            if (_isPanning && e.Button == MouseButtons.Left)
            {
                float deltaX = e.X - _panStart.X;
                float deltaY = e.Y - _panStart.Y;

                float factor = 0.8f;
                _imageOffset.X += deltaX * factor;
                _imageOffset.Y += deltaY * factor;

                _panStart = e.Location;
                ConstrainImagePosition();
                _pictureBox?.Invalidate();
                return;
            }

            // Позиція миші => UTM
            var pixel = ScreenToImagePixel(e.Location);
            if (CoordinateConverter != null)
            {
                var utm = CoordinateConverter.PixelToUTM(pixel);
                if (utm != PointF.Empty)
                    OnPositionChanged?.Invoke(utm);
            }
        }

        private void PictureBox_MouseUp(object? sender, MouseEventArgs e)
        {
            if (_cachedBitmap == null) return;

            if (e.Button == MouseButtons.Left)
            {
                _isPanning = false;
                return;
            }

            if (e.Button == MouseButtons.Right)
            {
                // Клік — передаємо UTM
                var pixel = ScreenToImagePixel(e.Location);
                if (CoordinateConverter != null)
                {
                    var utm = CoordinateConverter.PixelToUTM(pixel);
                    if (utm != PointF.Empty)
                    {
                        OnClick?.Invoke(utm);
                    }
                }
            }
        }

        private PointF ScreenToImagePixel(Point screen)
        {
            float x = (screen.X - _imageOffset.X) / _scale;
            float y = (screen.Y - _imageOffset.Y) / _scale;
            return new PointF(x, y);
        }

        private void FitMapToScreen()
        {
            if (_originalImageSize == Size.Empty || _pictureBox == null) return;
            float scaleX = (float)_pictureBox.Width / _originalImageSize.Width;
            float scaleY = (float)_pictureBox.Height / _originalImageSize.Height;
            _scale = Math.Max(scaleX, scaleY);
        }

        private void CenterImage()
        {
            if (_originalImageSize == Size.Empty || _pictureBox == null) return;
            int newWidth = (int)(_originalImageSize.Width * _scale);
            int newHeight = (int)(_originalImageSize.Height * _scale);

            _imageOffset.X = ( _pictureBox.Width - newWidth ) / 2;
            _imageOffset.Y = ( _pictureBox.Height - newHeight ) / 2;

            ConstrainImagePosition();
        }

        private void ConstrainImagePosition()
        {
            // Movement constraints removed: allow free panning of the SVG image. No clamping performed.
            return;
        }

        private float ConvertToPixels(SvgUnit unit)
        {
            const float DPI = 96f;
            const float PT_TO_PX = DPI / 72f;
            const float PC_TO_PX = DPI / 6f;
            const float MM_TO_PX = DPI / 25.4f;
            const float CM_TO_PX = DPI / 2.54f;
            const float IN_TO_PX = DPI;

            switch (unit.Type)
            {
                case SvgUnitType.Point: return unit.Value * PT_TO_PX;
                case SvgUnitType.Pica: return unit.Value * PC_TO_PX;
                case SvgUnitType.Millimeter: return unit.Value * MM_TO_PX;
                case SvgUnitType.Centimeter: return unit.Value * CM_TO_PX;
                case SvgUnitType.Inch: return unit.Value * IN_TO_PX;
                case SvgUnitType.Percentage: return unit.Value;
                case SvgUnitType.Em:
                case SvgUnitType.Ex: return unit.Value;
                case SvgUnitType.User:
                default: return unit.Value;
            }
        }

        public void Dispose()
        {
            if (_pictureBox != null)
            {
                _pictureBox.MouseDown -= PictureBox_MouseDown;
                _pictureBox.MouseMove -= PictureBox_MouseMove;
                _pictureBox.MouseUp -= PictureBox_MouseUp;
                _pictureBox.Paint -= PictureBox_Paint;
                _pictureBox?.Dispose();
                _pictureBox = null;
            }

            _cachedBitmap?.Dispose();
            _cachedBitmap = null;
        }
    }
}
