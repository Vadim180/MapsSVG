using System.Drawing;

namespace Maps.Services.Map
{
    // Простий інтерфейс для overlay в GMapProvider
    public interface IMapOverlay
    {
        void Draw(Graphics g);
    }
}
