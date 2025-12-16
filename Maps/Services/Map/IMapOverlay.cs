using System.Drawing;

namespace Maps.Services.Map
{
    public interface IMapOverlay
    {
        void Draw(Graphics g);
    }
}