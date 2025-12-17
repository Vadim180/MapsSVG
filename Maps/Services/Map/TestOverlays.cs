using System.Diagnostics;
using System.Drawing;
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
            // Текст у верхньому лівому куті карти
            string text = $"Позиція: {_lat:F6}, {_lng:F6} | Zoom: {_map.Zoom}";

            using var sf = new StringFormat { Alignment = StringAlignment.Near, LineAlignment = StringAlignment.Near };
            var font = SystemFonts.DefaultFont;
            var size = g.MeasureString(text, font);

            var rect = new RectangleF(8, 8, size.Width + 8, size.Height + 8);
            using var back = new SolidBrush(Color.FromArgb(160, 0, 0, 0));
            using var fore = new SolidBrush(Color.White);

            g.FillRectangle(back, rect);
            g.DrawString(text, font, fore, new PointF(rect.X + 4, rect.Y + 4), sf);
        }
    }
}
